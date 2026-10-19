using System.Globalization;
using System.Net;
using System.Xml.Linq;

namespace VientoHud.Core.Rain.Opera;

/// <summary>OPERA composite product.</summary>
public enum OperaProduct
{
    /// <summary>Maximum reflectivity (CIRRUS, <c>DBZH</c>): 1 km, every 5 min, published ~4 min later.</summary>
    Reflectivity,

    /// <summary>Rain rate in mm/h (NIMBUS, <c>RATE</c>): 2 km, every 15 min, published ~12 min later.</summary>
    RainRate,
}

/// <summary>Settings for <see cref="OperaRadarSource"/>.</summary>
public sealed record OperaOptions
{
    /// <summary>Open Radar Data object storage (anonymous, S3-compatible).</summary>
    public Uri BaseUri { get; init; } = new("https://s3.waw3-1.cloudferro.com/");

    /// <summary>Bucket with the last 24 hours.</summary>
    public string RecentBucket { get; init; } = "openradar-24h";

    /// <summary>Bucket with the archive (since 2012).</summary>
    public string ArchiveBucket { get; init; } = "openradar-archive";

    public OperaProduct Product { get; init; } = OperaProduct.Reflectivity;

    /// <summary>Folder where downloaded files are kept and reused; null to keep nothing.</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// Files in the cache older than this (by when they were written) are deleted whenever a new one is stored; null
    /// keeps them all (the console and the judges look at past times; the phone only needs the last hours).
    /// </summary>
    public TimeSpan? CacheMaxAge { get; init; }
}

/// <summary>
/// EUMETNET OPERA radar composites from Open Radar Data (CC BY 4.0, no key; DECISIONS 025). Files are ODIM HDF5 of
/// all of Europe (~2 MB) under <c>yyyy/MM/dd/OPERA/COMP/OPERA@yyyyMMddTHHmm@0@{DBZH|RATE}.h5</c>, first in the
/// 24-hour bucket and later in the archive.
/// </summary>
public sealed class OperaRadarSource(HttpClient httpClient, OperaOptions? options = null, TimeProvider? timeProvider = null)
    : IRadarSource
{
    public const string AttributionText = "Radar: EUMETNET OPERA (CC BY 4.0)";

    // A frame is looked for first in the bucket it most likely is in, then in the other.
    private static readonly TimeSpan RecentBucketAge = TimeSpan.FromHours(23);

    private readonly OperaOptions _options = options ?? new OperaOptions();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public TimeSpan FrameInterval => _options.Product == OperaProduct.Reflectivity
        ? TimeSpan.FromMinutes(5)
        : TimeSpan.FromMinutes(15);

    public string Attribution => AttributionText;

    private string Quantity => _options.Product == OperaProduct.Reflectivity ? "DBZH" : "RATE";

    public async Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        // Today's folder from two hours back (keys sort by time); yesterday's too just after midnight.
        var since = now - TimeSpan.FromHours(2);
        DateTimeOffset? latest = null;
        foreach (var day in new[] { since.UtcDateTime.Date, now.UtcDateTime.Date }.Distinct())
        {
            var prefix = $"{day:yyyy'/'MM'/'dd}/OPERA/COMP/";
            var startAfter = day == since.UtcDateTime.Date ? $"{prefix}OPERA@{since:yyyyMMdd'T'HHmm}" : null;
            foreach (var time in await ListFrameTimesAsync(prefix, startAfter, cancellationToken))
            {
                latest = latest is null || time > latest ? time : latest;
            }
        }

        return latest ?? throw new RadarException($"OPERA has no {Quantity} frames in the last two hours.");
    }

    public async Task<RainGrid?> GetFrameAsync(
        DateTimeOffset time,
        Coordinate center,
        double radiusKm,
        CancellationToken cancellationToken = default)
    {
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        if (time.Ticks % FrameInterval.Ticks != 0)
        {
            throw new ArgumentException($"Time must be a multiple of {FrameInterval.TotalMinutes} min.", nameof(time));
        }
        if (!double.IsFinite(radiusKm) || radiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), radiusKm, "Radius must be positive.");
        }

        var key = $"{time:yyyy'/'MM'/'dd}/OPERA/COMP/OPERA@{time:yyyyMMdd'T'HHmm}@0@{Quantity}.h5";
        var bytes = await ReadCachedAsync(key, cancellationToken) ?? await DownloadAsync(time, key, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var grid = OdimComposite.ReadAround(stream, center, radiusKm);
        if (grid.Time != time)
        {
            throw new RadarException($"OPERA file {key} is for {grid.Time:u}, not {time:u}.");
        }
        return grid;
    }

    private async Task<byte[]?> DownloadAsync(DateTimeOffset time, string key, CancellationToken cancellationToken)
    {
        var recent = _time.GetUtcNow() - time < RecentBucketAge;
        string[] buckets = recent
            ? [_options.RecentBucket, _options.ArchiveBucket]
            : [_options.ArchiveBucket, _options.RecentBucket];

        foreach (var bucket in buckets)
        {
            var bytes = await GetBytesAsync(new Uri(_options.BaseUri, $"{bucket}/{EscapeKey(key)}"), cancellationToken);
            if (bytes is not null)
            {
                await WriteCacheAsync(key, bytes, cancellationToken);
                return bytes;
            }
        }
        return null;
    }

    private async Task<IReadOnlyList<DateTimeOffset>> ListFrameTimesAsync(
        string prefix, string? startAfter, CancellationToken cancellationToken)
    {
        var times = new List<DateTimeOffset>();
        string? continuation = null;
        do
        {
            var query = $"list-type=2&prefix={Uri.EscapeDataString(prefix)}"
                        + (startAfter is null ? "" : $"&start-after={Uri.EscapeDataString(startAfter)}")
                        + (continuation is null ? "" : $"&continuation-token={Uri.EscapeDataString(continuation)}");
            var uri = new Uri(_options.BaseUri, $"{_options.RecentBucket}?{query}");
            var body = await GetBytesAsync(uri, cancellationToken)
                       ?? throw new RadarException($"OPERA bucket {_options.RecentBucket} not found.");

            XDocument document;
            try
            {
                using var stream = new MemoryStream(body);
                document = XDocument.Load(stream);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new RadarException("OPERA returned an unreadable bucket listing.", ex);
            }

            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var suffix = $"@0@{Quantity}.h5";
            foreach (var key in document.Descendants(ns + "Key").Select(k => k.Value))
            {
                if (key.EndsWith(suffix, StringComparison.Ordinal) && ParseKeyTime(key) is { } time)
                {
                    times.Add(time);
                }
            }

            var truncated = document.Root?.Element(ns + "IsTruncated")?.Value == "true";
            continuation = truncated ? document.Root?.Element(ns + "NextContinuationToken")?.Value : null;
        }
        while (continuation is not null);

        return times;
    }

    // ".../OPERA@20261016T1045@0@DBZH.h5" → 2026-10-16 10:45 UTC.
    private static DateTimeOffset? ParseKeyTime(string key)
    {
        var parts = key[(key.LastIndexOf('/') + 1)..].Split('@');
        return parts.Length >= 2 && DateTimeOffset.TryParseExact(
            parts[1], "yyyyMMdd'T'HHmm", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;
    }

    /// <summary>Body of a successful response; null for 404 (no such frame or bucket).</summary>
    private async Task<byte[]?> GetBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new RadarException(
                    $"OPERA returned HTTP {(int)response.StatusCode} for {uri.AbsolutePath}: {Truncate(body)}");
            }
            return body;
        }
        catch (HttpRequestException ex)
        {
            throw new RadarException($"OPERA request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RadarException("OPERA request timed out.", ex);
        }
    }

    private async Task<byte[]?> ReadCachedAsync(string key, CancellationToken cancellationToken)
    {
        var path = CachePath(key);
        return path is not null && File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    private async Task WriteCacheAsync(string key, byte[] bytes, CancellationToken cancellationToken)
    {
        if (CachePath(key) is not { } path)
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Written aside and moved, so an interrupted download never leaves a broken file in the cache.
        var partial = path + ".part";
        await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
        File.Move(partial, path, overwrite: true);
        if (_options.CacheMaxAge is { } maxAge)
        {
            Prune(_options.CacheDirectory!, _time.GetUtcNow() - maxAge);
        }
    }

    /// <summary>Deletes the cached files written before <paramref name="before"/>, and the folders left empty. Best effort.</summary>
    private static void Prune(string directory, DateTimeOffset before)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList())
            {
                if (File.GetLastWriteTimeUtc(file) < before.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            foreach (var folder in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length).ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file in use or a permission: the cache is only a cache.
        }
    }

    private string? CachePath(string key) => _options.CacheDirectory is { } directory
        ? Path.Combine([directory, .. key.Split('/')])
        : null;

    private static string EscapeKey(string key) => string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private static string Truncate(byte[] body)
    {
        var text = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 200)).Trim();
        return body.Length <= 200 ? text : text + "…";
    }
}
