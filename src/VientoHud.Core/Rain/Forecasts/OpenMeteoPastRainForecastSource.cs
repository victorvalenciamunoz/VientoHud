using System.Globalization;
using System.Net;
using System.Text.Json;

namespace VientoHud.Core.Rain.Forecasts;

/// <summary>Settings for <see cref="OpenMeteoPastRainForecastSource"/>.</summary>
public sealed record OpenMeteoRainArchiveOptions
{
    /// <summary>Historical Forecast API: first hours of each run stitched together.</summary>
    public Uri Endpoint { get; init; } = new("https://historical-forecast-api.open-meteo.com/v1/forecast");

    /// <summary>
    /// AROME HD (Météo-France, 1.5 km) with real 15-minute output; covers France and the north and centre of Spain
    /// (Madrid and León), not the south. The plain AROME HD model only splits each hour in four equal parts.
    /// </summary>
    public string Model { get; init; } = "meteofrance_arome_france_hd_15min";
}

/// <summary>
/// Past 15-minute rain forecasts from the Open-Meteo archive (CC BY 4.0, no key; DECISIONS 025). Several locations in
/// one request. Values are the rain of the preceding 15 minutes, turned into mm/h.
/// </summary>
/// <remarks>
/// The archive keeps the first hours of each run, so for a time T it holds what the model said shortly before T: a
/// generous rival for the nowcast (DECISIONS 031).
/// </remarks>
public sealed class OpenMeteoPastRainForecastSource(HttpClient httpClient, OpenMeteoRainArchiveOptions? options = null)
    : IPastRainForecastSource
{
    public const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0)";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly OpenMeteoRainArchiveOptions _options = options ?? new OpenMeteoRainArchiveOptions();

    public async Task<IReadOnlyList<RainForecastSeries>> GetAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (locations.Count == 0)
        {
            return [];
        }
        if (to <= from)
        {
            throw new ArgumentException("The end must be after the start.", nameof(to));
        }

        var query = string.Join('&',
            $"latitude={string.Join(',', locations.Select(l => Degrees(l.Latitude)))}",
            $"longitude={string.Join(',', locations.Select(l => Degrees(l.Longitude)))}",
            "minutely_15=precipitation",
            $"models={_options.Model}",
            $"start_date={from.UtcDateTime:yyyy-MM-dd}",
            $"end_date={to.UtcDateTime:yyyy-MM-dd}",
            "timezone=GMT",
            "timeformat=unixtime");
        var uri = new UriBuilder(_options.Endpoint) { Query = query }.Uri;

        string body;
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var reason = Reason(body);
                var hint = response.StatusCode == HttpStatusCode.TooManyRequests ? " (rate limit)" : "";
                throw new RainForecastException(
                    $"Open-Meteo returned HTTP {(int)response.StatusCode}{hint} for {_options.Model}: {reason}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new RainForecastException($"Open-Meteo request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RainForecastException("Open-Meteo request timed out.", ex);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            // One location gives an object; several give an array, in the same order.
            var roots = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : [document.RootElement];
            if (roots.Count != locations.Count)
            {
                throw new RainForecastException($"Open-Meteo returned {roots.Count} locations for {locations.Count}.");
            }
            return roots.Select((root, i) => ParseSeries(root, locations[i], from, to)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new RainForecastException("Open-Meteo returned an unexpected response.", ex);
        }
    }

    private RainForecastSeries ParseSeries(JsonElement root, Coordinate location, DateTimeOffset from, DateTimeOffset to)
    {
        var block = root.GetProperty("minutely_15");
        var times = block.GetProperty("time").EnumerateArray().Select(t => t.GetInt64()).ToList();
        var values = block.GetProperty("precipitation").EnumerateArray().ToList();
        var samples = new List<RainForecastSample>();
        for (var i = 0; i < times.Count; i++)
        {
            var end = DateTimeOffset.FromUnixTimeSeconds(times[i]);
            if (end > from && end <= to && values[i].ValueKind == JsonValueKind.Number)
            {
                samples.Add(new RainForecastSample(end, values[i].GetDouble() * (60 / Interval.TotalMinutes)));
            }
        }
        var gridPoint = new Coordinate(root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble());
        return new RainForecastSeries(location, gridPoint, _options.Model, Interval, samples);
    }

    private static string Reason(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() ?? body : body;
        }
        catch (JsonException)
        {
            return body.Length <= 200 ? body : body[..200] + "…";
        }
    }

    private static string Degrees(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
