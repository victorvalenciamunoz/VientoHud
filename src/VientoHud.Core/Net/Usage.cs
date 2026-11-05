using System.Globalization;
using System.Text.Json;

namespace VientoHud.Core.Net;

/// <summary>The outside services the app leans on, as their limits count them.</summary>
public enum UsageService
{
    /// <summary>OpenCycleMap tiles (the map, and the 3D view's base map).</summary>
    Thunderforest,

    /// <summary>Standard OpenStreetMap tiles, when the app was built without the Thunderforest key.</summary>
    OpenStreetMap,

    /// <summary>Wind and rain forecasts.</summary>
    OpenMeteo,

    /// <summary>The routes.</summary>
    BRouter,

    /// <summary>The radar frames (OPERA, EUMETNET).</summary>
    OperaRadar,

    /// <summary>The relief of the 3D view (Terrarium tiles, AWS Open Data).</summary>
    Terrain,

    /// <summary>Anything else (observations for the judges, licences…).</summary>
    Other,
}

/// <summary>
/// A service's published limits, in its own units (tiles, calls, requests); null where it publishes none. Checked at
/// the source on 2026-10-26 (DECISIONS 059).
/// </summary>
/// <param name="Note">What the limit is and where it comes from, in Spanish, for the CONSUMO panel.</param>
public sealed record UsageLimit(double? PerMinute, double? PerHour, double? PerDay, double? PerMonth, string Note)
{
    public static UsageLimit Of(UsageService service) => service switch
    {
        UsageService.Thunderforest => new(null, null, null, 150_000, "Plan gratuito: 150.000 teselas al mes."),
        UsageService.OpenStreetMap => new(null, null, null, null, "Sin cupo; sus normas piden un uso moderado."),
        UsageService.OpenMeteo => new(600, 5_000, 10_000, 300_000, "Gratis: 600 al minuto, 5.000 a la hora, 10.000 al día y 300.000 al mes."),
        UsageService.BRouter => new(60, null, null, null, "brouter.de no publica cupo; la app se pone una petición por segundo."),
        UsageService.OperaRadar => new(null, null, null, null, "Datos abiertos, sin cupo publicado."),
        UsageService.Terrain => new(null, null, null, null, "AWS Open Data, sin cupo."),
        _ => new(null, null, null, null, ""),
    };
}

/// <summary>Which service a request goes to and how much it weighs against that service's limits.</summary>
public static class UsageClassifier
{
    /// <summary>
    /// The service of <paramref name="url"/> and its weight. Open-Meteo counts every location of a request (observed
    /// when the daily quota ran out, 2026-10-19: ~25 calls per new point; not stated on its site) and, as its site
    /// says, more for more than 10 variables or more than two weeks, fractionally. Everything else, one per request.
    /// </summary>
    public static (UsageService Service, double Weight) Classify(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var host = url.Host.ToLowerInvariant();
        var path = url.AbsolutePath;
        if (host.EndsWith("thunderforest.com", StringComparison.Ordinal))
        {
            return (UsageService.Thunderforest, 1);
        }
        if (host.EndsWith("tile.openstreetmap.org", StringComparison.Ordinal))
        {
            return (UsageService.OpenStreetMap, 1);
        }
        if (host.EndsWith("open-meteo.com", StringComparison.Ordinal))
        {
            return (UsageService.OpenMeteo, OpenMeteoWeight(url));
        }
        if (host.Contains("brouter", StringComparison.Ordinal) || path.StartsWith("/brouter", StringComparison.Ordinal))
        {
            return (UsageService.BRouter, 1);
        }
        if (host == "s3.waw3-1.cloudferro.com")
        {
            return (UsageService.OperaRadar, 1);
        }
        if (host == "s3.amazonaws.com" && path.StartsWith("/elevation-tiles-prod/", StringComparison.Ordinal))
        {
            return (UsageService.Terrain, 1);
        }
        return (UsageService.Other, 1);
    }

    private static double OpenMeteoWeight(Uri url)
    {
        var query = url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .GroupBy(p => Uri.UnescapeDataString(p[0]))
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First().ElementAtOrDefault(1) ?? ""));
        int Items(string key) => query.TryGetValue(key, out var value) && value.Length > 0 ? value.Split(',').Length : 0;

        var locations = Math.Max(1, Items("latitude"));
        var variables = Items("hourly") + Items("minutely_15") + Items("daily") + Items("current");
        var days = SpanDays(query);
        return locations * Math.Max(1, variables / 10.0) * Math.Max(1, days / 14.0);
    }

    // Days the request covers: its dates or hours when given, otherwise the forecast days (7 by default) and past days.
    private static double SpanDays(Dictionary<string, string> query)
    {
        static DateTime? Parse(Dictionary<string, string> q, string key, string format) =>
            q.TryGetValue(key, out var v) && DateTime.TryParseExact(v, format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

        if (Parse(query, "start_date", "yyyy-MM-dd") is { } startDate && Parse(query, "end_date", "yyyy-MM-dd") is { } endDate)
        {
            return (endDate - startDate).TotalDays + 1;
        }
        if (Parse(query, "start_hour", "yyyy-MM-dd'T'HH:mm") is { } startHour && Parse(query, "end_hour", "yyyy-MM-dd'T'HH:mm") is { } endHour)
        {
            return (endHour - startHour).TotalDays;
        }
        var forecast = query.TryGetValue("forecast_days", out var f) && double.TryParse(f, CultureInfo.InvariantCulture, out var fd) ? fd : 7;
        var past = query.TryGetValue("past_days", out var p) && double.TryParse(p, CultureInfo.InvariantCulture, out var pd) ? pd : 0;
        return forecast + past;
    }
}

/// <summary>What a service took in one span of time: calls in its own units (see <see cref="UsageClassifier"/>) and bytes received.</summary>
public sealed record UsageCount(double Calls, long Bytes)
{
    public static readonly UsageCount Zero = new(0, 0);

    public UsageCount Add(double calls, long bytes) => new(Calls + calls, Bytes + bytes);
}

/// <summary>One service's use: the last minute and hour, today and this month (UTC), and its limits.</summary>
/// <param name="Busiest">
/// The highest share of a limit reached in any of its spans, from 0; null when the service publishes no limit.
/// </param>
public sealed record UsageRow(
    UsageService Service,
    UsageCount LastMinute,
    UsageCount LastHour,
    UsageCount Today,
    UsageCount Month,
    UsageLimit Limit,
    double? Busiest);

/// <summary>
/// Counts what the app asks of each outside service (phase 9), by UTC day and month, kept in a small JSON file on the
/// device (no server); the last minute and hour only in memory, for the per-minute and per-hour limits. Thread-safe.
/// Days older than <see cref="KeepDays"/> are dropped.
/// </summary>
public sealed class UsageMeter
{
    public const int KeepDays = 62;

    /// <summary>The file is written at most this often while counting; <see cref="Save"/> writes it at once.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly string? _path;
    private readonly TimeProvider _time;
    private readonly Dictionary<DateOnly, Dictionary<UsageService, UsageCount>> _days = [];
    private readonly Queue<(DateTimeOffset At, UsageService Service, double Calls, long Bytes)> _recent = new();
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;
    private bool _dirty;

    /// <param name="path">The JSON file; null keeps the counts in memory only.</param>
    public UsageMeter(string? path = null, TimeProvider? timeProvider = null)
    {
        _path = path;
        _time = timeProvider ?? TimeProvider.System;
        Load();
    }

    /// <summary>Counts one request to <paramref name="url"/> that brought <paramref name="bytes"/> back.</summary>
    public void Record(Uri url, long bytes)
    {
        var (service, calls) = UsageClassifier.Classify(url);
        Record(service, calls, bytes);
    }

    public void Record(UsageService service, double calls, long bytes)
    {
        var now = _time.GetUtcNow();
        bool save;
        lock (_lock)
        {
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            if (!_days.TryGetValue(day, out var services))
            {
                _days[day] = services = [];
            }
            services[service] = services.GetValueOrDefault(service, UsageCount.Zero).Add(calls, bytes);
            _recent.Enqueue((now, service, calls, bytes));
            Forget(now);
            _dirty = true;
            save = now - _lastSave >= SaveEvery;
        }
        if (save)
        {
            Save();
        }
    }

    /// <summary>Every service's use now, in the order of <see cref="UsageService"/>.</summary>
    public IReadOnlyList<UsageRow> Report()
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            Forget(now);
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            return Enum.GetValues<UsageService>().Select(service =>
            {
                UsageCount Since(TimeSpan span) => _recent
                    .Where(r => r.Service == service && r.At > now - span)
                    .Aggregate(UsageCount.Zero, (sum, r) => sum.Add(r.Calls, r.Bytes));
                var minute = Since(TimeSpan.FromMinutes(1));
                var hour = Since(TimeSpan.FromHours(1));
                var day = _days.TryGetValue(today, out var d) ? d.GetValueOrDefault(service, UsageCount.Zero) : UsageCount.Zero;
                var month = _days
                    .Where(kv => kv.Key.Year == today.Year && kv.Key.Month == today.Month)
                    .Select(kv => kv.Value.GetValueOrDefault(service, UsageCount.Zero))
                    .Aggregate(UsageCount.Zero, (sum, c) => sum.Add(c.Calls, c.Bytes));
                var limit = UsageLimit.Of(service);
                var shares = new[]
                {
                    limit.PerMinute is { } pm ? minute.Calls / pm : (double?)null,
                    limit.PerHour is { } ph ? hour.Calls / ph : null,
                    limit.PerDay is { } pd ? day.Calls / pd : null,
                    limit.PerMonth is { } pmo ? month.Calls / pmo : null,
                }.Where(s => s is not null).ToList();
                return new UsageRow(service, minute, hour, day, month, limit, shares.Count > 0 ? shares.Max() : null);
            }).ToList();
        }
    }

    /// <summary>Writes the counts to the file now (best effort: a full disk only loses counting).</summary>
    public void Save()
    {
        if (_path is null)
        {
            return;
        }
        string json;
        lock (_lock)
        {
            if (!_dirty)
            {
                return;
            }
            var oldest = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddDays(-KeepDays);
            foreach (var old in _days.Keys.Where(d => d < oldest).ToList())
            {
                _days.Remove(old);
            }
            json = JsonSerializer.Serialize(_days.OrderBy(kv => kv.Key).ToDictionary(
                kv => kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                kv => kv.Value.ToDictionary(s => s.Key.ToString(), s => new[] { s.Value.Calls, s.Value.Bytes })));
            _dirty = false;
            _lastSave = _time.GetUtcNow();
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var partial = _path + ".part";
            File.WriteAllText(partial, json);
            File.Move(partial, _path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }
        try
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double[]>>>(File.ReadAllText(_path)) ?? [];
            foreach (var (dayText, services) in stored)
            {
                if (!DateOnly.TryParseExact(dayText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    continue;
                }
                var counts = new Dictionary<UsageService, UsageCount>();
                foreach (var (name, values) in services)
                {
                    if (Enum.TryParse<UsageService>(name, out var service) && values.Length == 2)
                    {
                        counts[service] = new UsageCount(values[0], (long)values[1]);
                    }
                }
                _days[day] = counts;
            }
        }
        catch (JsonException)
        {
            // A broken file starts the counts again rather than stopping the app.
        }
    }

    private void Forget(DateTimeOffset now)
    {
        while (_recent.Count > 0 && _recent.Peek().At <= now - TimeSpan.FromHours(1))
        {
            _recent.Dequeue();
        }
    }
}

/// <summary>Counts every request that goes through it in a <see cref="UsageMeter"/>, with the bytes that came back.</summary>
public sealed class MeteredHandler(UsageMeter meter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch
        {
            // Sent and failed: it still counts against the service's limits.
            meter.Record(request.RequestUri!, 0);
            throw;
        }
        var bytes = response.Content.Headers.ContentLength;
        if (bytes is null)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            bytes = response.Content.Headers.ContentLength ?? 0;
        }
        meter.Record(request.RequestUri!, bytes.Value);
        return response;
    }
}
