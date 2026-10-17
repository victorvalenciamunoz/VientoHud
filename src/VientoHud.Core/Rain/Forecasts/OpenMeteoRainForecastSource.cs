using System.Globalization;
using System.Net;
using System.Text.Json;

namespace VientoHud.Core.Rain.Forecasts;

/// <summary>Settings for <see cref="OpenMeteoRainForecastSource"/>: which forecast (now, archive or one past run).</summary>
public sealed record OpenMeteoRainOptions
{
    public static readonly Uri ForecastEndpoint = new("https://api.open-meteo.com/v1/forecast");

    /// <summary>Historical Forecast API: first hours of each run stitched together.</summary>
    public static readonly Uri ArchiveEndpoint = new("https://historical-forecast-api.open-meteo.com/v1/forecast");

    /// <summary>Single Runs API: one past run as it came out (archived since April 2026).</summary>
    public static readonly Uri SingleRunsEndpoint = new("https://single-runs-api.open-meteo.com/v1/forecast");

    /// <summary>The latest forecast, for planning a ride now.</summary>
    public static OpenMeteoRainOptions Latest { get; } = new() { Endpoint = ForecastEndpoint };

    /// <summary>The archive of past forecasts (what the model said shortly before each time).</summary>
    public static OpenMeteoRainOptions Archive { get; } = new() { Endpoint = ArchiveEndpoint };

    /// <summary>One past run, starting at <paramref name="run"/> (UTC, a whole hour), for judging at a true lead time.</summary>
    public static OpenMeteoRainOptions SingleRun(DateTimeOffset run) => new() { Endpoint = SingleRunsEndpoint, Run = run };

    public Uri Endpoint { get; init; } = ArchiveEndpoint;

    /// <summary>Start of the run to read, with <see cref="SingleRunsEndpoint"/>; null for the others.</summary>
    public DateTimeOffset? Run { get; init; }

    /// <summary>
    /// AROME HD (Météo-France, 1.5 km) with real 15-minute output; covers France and the north and centre of Spain
    /// (Madrid and León), not the south. The plain AROME HD model only splits each hour in four equal parts.
    /// </summary>
    public string Model { get; init; } = "meteofrance_arome_france_hd_15min";

    /// <summary>
    /// Model for the hourly chance of rain, the fallback where AROME does not reach (DECISIONS 033): `best_match` takes
    /// it from the ICON ensembles.
    /// </summary>
    public string ProbabilityModel { get; init; } = "best_match";
}

/// <summary>
/// 15-minute rain forecasts from Open-Meteo (CC BY 4.0, no key; DECISIONS 025 and 033): the latest, the archive or one
/// past run. Several locations in one request (each counts as a call against the limits). Values are the rain of the
/// preceding 15 minutes, turned into mm/h.
/// </summary>
/// <remarks>
/// The archive keeps the first hours of each run, so for a time T it holds what the model said shortly before T: a
/// generous rival for the nowcast (DECISIONS 031). A single run gives what it said hours before.
/// </remarks>
public sealed class OpenMeteoRainForecastSource(HttpClient httpClient, OpenMeteoRainOptions? options = null)
    : IRainForecastSource, IRainProbabilitySource
{
    public const string Attribution = "Weather data by Open-Meteo.com (CC BY 4.0)";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly OpenMeteoRainOptions _options = options ?? new OpenMeteoRainOptions();

    public Task<IReadOnlyList<RainForecastSeries>> GetAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        FetchAsync(locations, from, to, "minutely_15", "precipitation", _options.Model, (root, location) =>
        {
            var gridPoint = GridPoint(root);
            var samples = Values(root, "minutely_15", "precipitation", from, to)
                .Select(v => new RainForecastSample(v.End, v.Value * (60 / Interval.TotalMinutes)))
                .ToList();
            return new RainForecastSeries(location, gridPoint, _options.Model, Interval, samples);
        }, cancellationToken);

    public Task<IReadOnlyList<RainProbabilitySeries>> GetProbabilityAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default) =>
        FetchAsync(locations, from, to, "hourly", "precipitation_probability", _options.ProbabilityModel, (root, location) =>
        {
            var samples = Values(root, "hourly", "precipitation_probability", from, to)
                .Select(v => new RainProbabilitySample(v.End, Math.Clamp(v.Value / 100, 0, 1)))
                .ToList();
            return new RainProbabilitySeries(location, GridPoint(root), _options.ProbabilityModel, TimeSpan.FromHours(1), samples);
        }, cancellationToken);

    private async Task<IReadOnlyList<T>> FetchAsync<T>(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        string block,
        string variable,
        string model,
        Func<JsonElement, Coordinate, T> parse,
        CancellationToken cancellationToken)
    {
        if (locations.Count == 0)
        {
            return [];
        }
        if (to <= from)
        {
            throw new ArgumentException("The end must be after the start.", nameof(to));
        }

        // A single run has no dates (it gives the whole run); the range is cut afterwards.
        string[] when = _options.Run is { } run
            ? [$"run={run.UtcDateTime:yyyy-MM-dd'T'HH:mm}"]
            : [$"start_date={from.UtcDateTime:yyyy-MM-dd}", $"end_date={to.UtcDateTime:yyyy-MM-dd}"];
        var query = string.Join('&',
            new[]
            {
                $"latitude={string.Join(',', locations.Select(l => Degrees(l.Latitude)))}",
                $"longitude={string.Join(',', locations.Select(l => Degrees(l.Longitude)))}",
                $"{block}={variable}",
                $"models={model}",
            }
            .Concat(when)
            .Concat(["timezone=GMT", "timeformat=unixtime"]));
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
                    $"Open-Meteo returned HTTP {(int)response.StatusCode}{hint} for {model}: {reason}");
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
            return roots.Select((root, i) => parse(root, locations[i])).ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new RainForecastException("Open-Meteo returned an unexpected response.", ex);
        }
    }

    // Values of a block whose interval ends in (from, to]; Open-Meteo stamps each value at the end of its interval.
    private static IEnumerable<(DateTimeOffset End, double Value)> Values(
        JsonElement root, string block, string variable, DateTimeOffset from, DateTimeOffset to)
    {
        var data = root.GetProperty(block);
        var times = data.GetProperty("time").EnumerateArray().Select(t => t.GetInt64()).ToList();
        var values = data.GetProperty(variable).EnumerateArray().ToList();
        for (var i = 0; i < times.Count; i++)
        {
            var end = DateTimeOffset.FromUnixTimeSeconds(times[i]);
            if (end > from && end <= to && values[i].ValueKind == JsonValueKind.Number)
            {
                yield return (end, values[i].GetDouble());
            }
        }
    }

    private static Coordinate GridPoint(JsonElement root) =>
        new(root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble());

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
