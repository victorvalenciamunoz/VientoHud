using VientoHud.Core.Net;

namespace VientoHud.Core.Flyover;

/// <summary>
/// Map tiles kept on disk for the 3D view (phase 8, step 4): terrain and base map alike, one folder per source, as
/// "{source}/{z}/{x}/{y}.png". A tile asked for is served from the folder when it is there and fetched once
/// otherwise; a whole corridor can be fetched ahead. What is in the folder works without a connection.
/// </summary>
public sealed class TileStore(HttpClient httpClient, string directory, TimeProvider? timeProvider = null)
{
    /// <summary>Tiles older than this are refetched when asked for again (the relief does not change; map tiles do, slowly).</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(90);

    private readonly HttpClient _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _directory = !string.IsNullOrWhiteSpace(directory)
        ? directory
        : throw new ArgumentException("A directory is needed.", nameof(directory));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public TimeSpan MaxAge { get; init; } = DefaultMaxAge;

    /// <summary>How many tiles are fetched at once when a corridor is fetched ahead.</summary>
    public int Parallelism { get; init; } = 4;

    public string Directory => _directory;

    /// <summary>Whether the tile is in the store and not too old.</summary>
    public bool Has(string source, TileAddress tile)
    {
        var path = PathOf(source, tile);
        return File.Exists(path) && _time.GetUtcNow() - File.GetLastWriteTimeUtc(path) <= MaxAge;
    }

    /// <summary>
    /// The tile's bytes: from the store, or fetched from <paramref name="url"/> and kept. Null when it cannot be had
    /// (not found, server or network failure): the view shows a hole there.
    /// </summary>
    public async Task<byte[]?> GetOrFetchAsync(string source, TileAddress tile, Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(url);
        var path = PathOf(source, tile);
        if (Has(source, tile))
        {
            var cached = await File.ReadAllBytesAsync(path, cancellationToken);
            // Marked as used (the access time; the write time says how old it is): the cache cap deletes what was
            // used longest ago first (CacheLimit).
            try
            {
                File.SetLastAccessTimeUtc(path, _time.GetUtcNow().UtcDateTime);
            }
            catch (IOException)
            {
            }
            return cached;
        }
        byte[] bytes;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", ResilientHttp.UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        if (bytes.Length == 0)
        {
            return null;
        }
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        // Written aside and moved, so an interrupted download never leaves a broken tile in the store.
        var partial = path + ".part";
        await File.WriteAllBytesAsync(partial, bytes, cancellationToken);
        File.Move(partial, path, overwrite: true);
        return bytes;
    }

    /// <summary>
    /// Fetches ahead the tiles of <paramref name="tiles"/> that are not in the store, a few at a time. Returns how
    /// many were fetched and how many could not be; <paramref name="progress"/> gets the count done so far.
    /// </summary>
    public async Task<(int Fetched, int Failed)> PrefetchAsync(
        string source,
        IReadOnlyList<TileAddress> tiles,
        Func<TileAddress, Uri> urlOf,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(urlOf);
        var missing = tiles.Where(t => !Has(source, t)).ToList();
        var fetched = 0;
        var failed = 0;
        var done = 0;
        await Parallel.ForEachAsync(
            missing,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Parallelism), CancellationToken = cancellationToken },
            async (tile, ct) =>
            {
                var bytes = await GetOrFetchAsync(source, tile, urlOf(tile), ct);
                if (bytes is null)
                {
                    Interlocked.Increment(ref failed);
                }
                else
                {
                    Interlocked.Increment(ref fetched);
                }
                progress?.Report(Interlocked.Increment(ref done));
            });
        return (fetched, failed);
    }

    /// <summary>Bytes on disk for one source, or for all when <paramref name="source"/> is null.</summary>
    public long SizeBytes(string? source = null)
    {
        var root = source is null ? _directory : System.IO.Path.Combine(_directory, source);
        return System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : 0;
    }

    /// <summary>Where the tile lives on disk.</summary>
    public string PathOf(string source, TileAddress tile) =>
        System.IO.Path.Combine(_directory, source, tile.Zoom.ToString(), tile.X.ToString(), tile.Y + ".png");
}
