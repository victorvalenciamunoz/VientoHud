using System.Globalization;
using System.Text.Json;

namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Hourly wind forecast from the Open-Meteo forecast API (https://open-meteo.com/en/docs).</summary>
public sealed class OpenMeteoWindForecastProvider(HttpClient httpClient, OpenMeteoOptions? options = null)
    : IWindForecastProvider
{
    public const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0)";

    private const string SpeedVariable = "wind_speed_10m";
    private const string DirectionVariable = "wind_direction_10m";
    private const string GustVariable = "wind_gusts_10m";

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
        var uri = BuildUri(location, start, start.AddHours(hours - 1));

        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Interpret(response, body, location, start, hours);
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

    private WindForecast Interpret(
        HttpResponseMessage response,
        string body,
        Coordinate location,
        DateTimeOffset start,
        int hours)
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
                throw new WindForecastException(
                    $"Open-Meteo returned HTTP {(int)response.StatusCode}: {reason ?? "no reason given"}.");
            }

            return Parse(root, location, start, hours);
        }
    }

    private Uri BuildUri(Coordinate location, DateTimeOffset firstHour, DateTimeOffset lastHour)
    {
        var query = string.Join('&',
            $"latitude={location.Latitude.ToString("0.#####", CultureInfo.InvariantCulture)}",
            $"longitude={location.Longitude.ToString("0.#####", CultureInfo.InvariantCulture)}",
            $"hourly={SpeedVariable},{DirectionVariable},{GustVariable}",
            $"models={string.Join(',', _options.Models)}",
            $"start_hour={firstHour.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}",
            $"end_hour={lastHour.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)}",
            "wind_speed_unit=kmh",
            "timezone=GMT",
            "timeformat=unixtime");

        return new UriBuilder(_options.ForecastEndpoint) { Query = query }.Uri;
    }

    private WindForecast Parse(JsonElement root, Coordinate requested, DateTimeOffset start, int hours)
    {
        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time");

        // Open-Meteo suffixes each variable with the model name, except when a single model is requested.
        var series = _options.Models
            .Select(model => new ModelSeries(
                model,
                ReadSeries(hourly, SpeedVariable, model),
                ReadSeries(hourly, DirectionVariable, model),
                ReadSeries(hourly, GustVariable, model)))
            .ToList();

        var samplesByTime = new Dictionary<DateTimeOffset, WindSample>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64());
            var sample = series
                .Select(s => s.SampleAt(i, time))
                .FirstOrDefault(s => s is not null);
            if (sample is not null)
            {
                samplesByTime[time] = sample;
            }
        }

        var samples = new List<WindSample>(hours);
        for (var h = 0; h < hours; h++)
        {
            var time = start.AddHours(h);
            if (!samplesByTime.TryGetValue(time, out var sample))
            {
                throw new WindForecastException(
                    $"Open-Meteo has no wind data for {time:yyyy-MM-dd HH:mm} UTC in any of the models " +
                    $"{string.Join(", ", _options.Models)}.");
            }
            samples.Add(sample);
        }

        var gridPoint = new Coordinate(root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble());
        var elevation = root.GetProperty("elevation").GetDouble();
        return new WindForecast(requested, gridPoint, elevation, samples, Attribution);
    }

    private JsonElement? ReadSeries(JsonElement hourly, string variable, string model)
    {
        if (hourly.TryGetProperty($"{variable}_{model}", out var suffixed))
        {
            return suffixed;
        }
        return _options.Models.Count == 1 && hourly.TryGetProperty(variable, out var plain) ? plain : null;
    }

    private static bool IsError(JsonElement root) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True;

    private static DateTimeOffset TruncateToHour(DateTimeOffset utc) =>
        new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);

    private sealed record ModelSeries(string Model, JsonElement? Speed, JsonElement? Direction, JsonElement? Gust)
    {
        public WindSample? SampleAt(int index, DateTimeOffset time)
        {
            var speed = ValueAt(Speed, index);
            var direction = ValueAt(Direction, index);
            var gust = ValueAt(Gust, index);
            return speed is null || direction is null || gust is null
                ? null
                : new WindSample(time, speed.Value, gust.Value, direction.Value, Model);
        }

        private static double? ValueAt(JsonElement? series, int index) =>
            series is { } s && index < s.GetArrayLength() && s[index].ValueKind == JsonValueKind.Number
                ? s[index].GetDouble()
                : null;
    }
}
