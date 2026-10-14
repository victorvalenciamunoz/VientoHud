using System.Globalization;
using System.Text.Json;

namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Hourly wind forecast from the Open-Meteo forecast API (https://open-meteo.com/en/docs).</summary>
/// <remarks>
/// Models are requested one per call, in order of preference, and only while some hour is still missing.
/// Asking for several models in one call is ambiguous: when a model does not cover the location Open-Meteo
/// drops it silently and returns unsuffixed variables, so the data can no longer be traced to a model.
/// </remarks>
public sealed class OpenMeteoWindForecastProvider(HttpClient httpClient, OpenMeteoOptions? options = null)
    : IWindForecastProvider
{
    public const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0)";

    private const string SpeedVariable = "wind_speed_10m";
    private const string DirectionVariable = "wind_direction_10m";
    private const string GustVariable = "wind_gusts_10m";

    // Reason Open-Meteo gives (HTTP 400) when a model's domain does not include the location.
    private const string NoDataForLocation = "No data is available for this location";

    private readonly OpenMeteoOptions _options = options ?? new OpenMeteoOptions();

    public async Task<WindForecast> GetHourlyAsync(
        Coordinate location,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hours, 1);
        if (_options.Models.Count == 0)
        {
            throw new InvalidOperationException("At least one Open-Meteo model must be configured.");
        }

        var start = TruncateToHour(from.ToUniversalTime());
        var samples = new WindSample?[hours];
        ModelResponse? first = null;

        foreach (var model in _options.Models)
        {
            if (samples.All(s => s is not null))
            {
                break;
            }

            var response = await FetchAsync(model, location, start, hours, cancellationToken);
            if (response is null)
            {
                continue;
            }

            var filledAny = false;
            for (var h = 0; h < hours; h++)
            {
                if (samples[h] is null && response.Samples.TryGetValue(start.AddHours(h), out var sample))
                {
                    samples[h] = sample;
                    filledAny = true;
                }
            }
            if (filledAny)
            {
                first ??= response;
            }
        }

        var missing = Array.FindIndex(samples, s => s is null);
        if (missing >= 0)
        {
            throw new WindForecastException(
                $"Open-Meteo has no wind data for {start.AddHours(missing):yyyy-MM-dd HH:mm} UTC in any of the models " +
                $"{string.Join(", ", _options.Models)}.");
        }

        // Models use different grids; report the one that supplied the first hours.
        return new WindForecast(location, first!.GridPoint, first.ElevationMeters, samples!, Attribution);
    }

    /// <summary>Returns null when the model does not cover the location.</summary>
    private async Task<ModelResponse?> FetchAsync(
        string model,
        Coordinate location,
        DateTimeOffset start,
        int hours,
        CancellationToken cancellationToken)
    {
        var uri = BuildUri(model, location, start, start.AddHours(hours - 1));
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Interpret(model, response, body);
        }
        catch (HttpRequestException ex)
        {
            throw new WindForecastException($"Open-Meteo request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WindForecastException("Open-Meteo request timed out.", ex);
        }
    }

    private static ModelResponse? Interpret(string model, HttpResponseMessage response, string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new WindForecastException(
                $"Open-Meteo returned HTTP {(int)response.StatusCode} with a body that is not JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode || IsError(root))
            {
                var reason = root.TryGetProperty("reason", out var r) ? r.GetString() : null;
                if (reason == NoDataForLocation)
                {
                    return null;
                }
                throw new WindForecastException(
                    $"Open-Meteo returned HTTP {(int)response.StatusCode} for model {model}: {reason ?? "no reason given"}.");
            }

            return Parse(model, root);
        }
    }

    private Uri BuildUri(string model, Coordinate location, DateTimeOffset firstHour, DateTimeOffset lastHour)
    {
        var query = string.Join('&',
            $"latitude={location.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)}",
            $"longitude={location.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)}",
            $"hourly={SpeedVariable},{DirectionVariable},{GustVariable}",
            $"models={model}",
            $"start_hour={firstHour.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}",
            $"end_hour={lastHour.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}",
            "wind_speed_unit=kmh",
            "timezone=GMT",
            "timeformat=unixtime");

        return new UriBuilder(_options.ForecastEndpoint) { Query = query }.Uri;
    }

    // With a single model per request, Open-Meteo names the variables without a model suffix.
    private static ModelResponse Parse(string model, JsonElement root)
    {
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var speeds = hourly.GetProperty(SpeedVariable);
        var directions = hourly.GetProperty(DirectionVariable);
        var gusts = hourly.GetProperty(GustVariable);

        var samples = new Dictionary<DateTimeOffset, WindSample>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            if (ValueAt(speeds, i) is { } speed && ValueAt(directions, i) is { } direction && ValueAt(gusts, i) is { } gust)
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64());
                samples[time] = new WindSample(time, speed, gust, direction, model);
            }
        }

        var gridPoint = new Coordinate(root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble());
        return new ModelResponse(gridPoint, root.GetProperty("elevation").GetDouble(), samples);
    }

    private static double? ValueAt(JsonElement series, int index) =>
        index < series.GetArrayLength() && series[index].ValueKind == JsonValueKind.Number
            ? series[index].GetDouble()
            : null;

    private static bool IsError(JsonElement root) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True;

    private static DateTimeOffset TruncateToHour(DateTimeOffset utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

    private sealed record ModelResponse(
        Coordinate GridPoint,
        double ElevationMeters,
        IReadOnlyDictionary<DateTimeOffset, WindSample> Samples);
}
