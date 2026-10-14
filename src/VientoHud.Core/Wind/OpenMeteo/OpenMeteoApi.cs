using System.Globalization;
using System.Text.Json;

namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Request and response details shared by the Open-Meteo forecast and archive endpoints.</summary>
internal static class OpenMeteoApi
{
    public const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0)";

    public const string SpeedVariable = "wind_speed_10m";
    public const string DirectionVariable = "wind_direction_10m";
    public const string GustVariable = "wind_gusts_10m";

    // Reason Open-Meteo gives (HTTP 400) when a model's domain does not include the location.
    private const string NoDataForLocation = "No data is available for this location";

    /// <summary>
    /// Query for hourly wind of one model, from <paramref name="firstHour"/> to <paramref name="lastHour"/> inclusive,
    /// in UTC and km/h. <paramref name="variableSuffix"/> selects archived leads, e.g. "_previous_day1".
    /// </summary>
    public static Uri BuildUri(
        Uri endpoint,
        IReadOnlyList<Coordinate> locations,
        string model,
        DateTimeOffset firstHour,
        DateTimeOffset lastHour,
        string variableSuffix = "")
    {
        var query = string.Join('&',
            $"latitude={string.Join(',', locations.Select(l => FormatDegrees(l.Latitude)))}",
            $"longitude={string.Join(',', locations.Select(l => FormatDegrees(l.Longitude)))}",
            $"hourly={SpeedVariable}{variableSuffix},{DirectionVariable}{variableSuffix},{GustVariable}{variableSuffix}",
            $"models={model}",
            $"start_hour={FormatHour(firstHour)}",
            $"end_hour={FormatHour(lastHour)}",
            "wind_speed_unit=kmh",
            "timezone=GMT",
            "timeformat=unixtime");

        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    /// <summary>
    /// Sends the request and returns the parsed body, or null when the model does not cover the location(s).
    /// The caller disposes the document.
    /// </summary>
    /// <exception cref="WindForecastException">Network failure, timeout, error response or invalid JSON.</exception>
    public static async Task<JsonDocument?> GetAsync(
        HttpClient httpClient,
        Uri uri,
        string model,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        string body;
        try
        {
            response = await httpClient.GetAsync(uri, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new WindForecastException($"Open-Meteo request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WindForecastException("Open-Meteo request timed out.", ex);
        }

        using (response)
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

            var root = document.RootElement;
            var isError = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.True;
            if (response.IsSuccessStatusCode && !isError)
            {
                return document;
            }

            var reason = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("reason", out var r)
                ? r.GetString()
                : null;
            document.Dispose();
            return reason == NoDataForLocation
                ? null
                : throw new WindForecastException(
                    $"Open-Meteo returned HTTP {(int)response.StatusCode} for model {model}: {reason ?? "no reason given"}.");
        }
    }

    /// <summary>
    /// One element per requested location: Open-Meteo answers a single location with an object
    /// and several with an array, in request order.
    /// </summary>
    public static IReadOnlyList<JsonElement> Locations(JsonElement root) =>
        root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray()] : [root];

    public static Coordinate GridPoint(JsonElement location) =>
        new(location.GetProperty("latitude").GetDouble(), location.GetProperty("longitude").GetDouble());

    public static double Elevation(JsonElement location) => location.GetProperty("elevation").GetDouble();

    /// <summary>
    /// Hours with speed, direction and gust all present. With one model per request Open-Meteo names the
    /// variables without a model suffix, only with <paramref name="variableSuffix"/> for archived leads.
    /// </summary>
    public static Dictionary<DateTimeOffset, WindSample> HourlySamples(
        JsonElement location,
        string model,
        string variableSuffix = "")
    {
        var hourly = location.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var speeds = hourly.GetProperty(SpeedVariable + variableSuffix);
        var directions = hourly.GetProperty(DirectionVariable + variableSuffix);
        var gusts = hourly.GetProperty(GustVariable + variableSuffix);

        var samples = new Dictionary<DateTimeOffset, WindSample>();
        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            if (ValueAt(speeds, i) is { } speed && ValueAt(directions, i) is { } direction && ValueAt(gusts, i) is { } gust)
            {
                var time = DateTimeOffset.FromUnixTimeSeconds(times[i].GetInt64());
                samples[time] = new WindSample(time, speed, gust, direction, model);
            }
        }
        return samples;
    }

    public static DateTimeOffset TruncateToHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }

    private static double? ValueAt(JsonElement series, int index) =>
        index < series.GetArrayLength() && series[index].ValueKind == JsonValueKind.Number
            ? series[index].GetDouble()
            : null;

    private static string FormatDegrees(double degrees) => degrees.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string FormatHour(DateTimeOffset hour) =>
        hour.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
}
