using System.IO.Compression;
using System.Net;

namespace VientoHud.Core.Towns.Ghsl;

/// <summary>Settings for <see cref="GhslBuiltUpSource"/>.</summary>
public sealed record GhslOptions
{
    /// <summary>Folder with the tiles of GHS-BUILT-S, epoch 2020, WGS84 at 3 arc-seconds.</summary>
    public Uri BaseUri { get; init; } = new(
        "https://jeodpp.jrc.ec.europa.eu/ftp/jrc-opendata/GHSL/GHS_BUILT_S_GLOBE_R2023A/GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss/V1-0/tiles/");

    /// <summary>Tile file names: <c>{prefix}_R{row}_C{column}.zip</c>.</summary>
    public string FilePrefix { get; init; } = "GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss_V1_0";

    /// <summary>
    /// Folder where tiles are kept once downloaded (each ~15-30 MB, never refreshed: the data is for 2020); null to keep
    /// nothing and download every time.
    /// </summary>
    public string? CacheDirectory { get; init; }
}

/// <summary>
/// Built-up ground from the European Commission's Global Human Settlement Layer, GHS-BUILT-S R2023A (CC BY 4.0, no key;
/// DECISIONS 045): square metres built in each cell of 3 arc-seconds (~90 m), from satellite images. The world comes in
/// 10° × 10° tiles, each a zip with a BigTIFF of 12,000 × 12,000 cells; a tile is downloaded whole once and then only
/// the blocks around the point asked for are read. Tiles of open sea do not exist (404) and count as no built-up ground.
/// </summary>
public sealed class GhslBuiltUpSource(HttpClient httpClient, GhslOptions? options = null) : IBuiltUpSource
{
    public const string AttributionText = "Built-up areas: GHSL, © European Union (CC BY 4.0)";

    // The tile grid, as read from the files (row 5 column 18 starts at 49.0996° N 10.0079° W): 10° tiles from these
    // corners, of cells of 1/1200° (DECISIONS 045).
    private const double GridNorth = 89.09958333862926;
    private const double GridWest = -180.00791660463318;
    private const double TileDegrees = 10;
    private const double CellDegrees = TileDegrees / 12000;

    private readonly GhslOptions _options = options ?? new GhslOptions();

    public string Attribution => AttributionText;

    public async Task<BuiltUpGrid> GetAroundAsync(Coordinate center, double radiusKm, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(radiusKm) || radiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), radiusKm, "Radius must be positive.");
        }
        const double kmPerDegree = 6371.0088 * Math.PI / 180;
        var dLat = radiusKm / kmPerDegree;
        var dLon = radiusKm / (kmPerDegree * Math.Max(0.01, Math.Cos(center.Latitude * Math.PI / 180)));

        // The window in cells of the world grid.
        var column0 = (int)Math.Floor((center.Longitude - dLon - GridWest) / CellDegrees);
        var column1 = (int)Math.Floor((center.Longitude + dLon - GridWest) / CellDegrees);
        var row0 = Math.Max(0, (int)Math.Floor((GridNorth - (center.Latitude + dLat)) / CellDegrees));
        var row1 = (int)Math.Floor((GridNorth - (center.Latitude - dLat)) / CellDegrees);
        var width = column1 - column0 + 1;
        var height = row1 - row0 + 1;
        var shares = new float[width * height];

        const int cellsPerTile = 12000;
        for (var tileRow = row0 / cellsPerTile; tileRow <= row1 / cellsPerTile; tileRow++)
        {
            for (var tileColumn = FloorDiv(column0, cellsPerTile); tileColumn <= FloorDiv(column1, cellsPerTile); tileColumn++)
            {
                var name = $"{_options.FilePrefix}_R{tileRow + 1}_C{tileColumn + 1}";
                await using var tif = await OpenTileAsync(name, cancellationToken);
                if (tif is null)
                {
                    continue;
                }
                Fill(GeoTiff.Open(tif), name, column0, row0, width, height, shares);
            }
        }
        return new BuiltUpGrid(GridNorth - row0 * CellDegrees, GridWest + column0 * CellDegrees, CellDegrees, width, height, shares);
    }

    // Copies the part of a tile inside the window into it, as shares of each cell's area.
    private static void Fill(GeoTiff tiff, string name, int column0, int row0, int width, int height, float[] shares)
    {
        if (Math.Abs(tiff.CellLonDegrees - CellDegrees) > 1e-9 || Math.Abs(tiff.CellLatDegrees - CellDegrees) > 1e-9)
        {
            throw new BuiltUpException($"GHSL tile {name} has cells of {tiff.CellLonDegrees}°, not {CellDegrees}°.");
        }
        var tileColumn0 = (int)Math.Round((tiff.West - GridWest) / CellDegrees);
        var tileRow0 = (int)Math.Round((GridNorth - tiff.North) / CellDegrees);
        var values = tiff.ReadWindow(column0 - tileColumn0, row0 - tileRow0, width, height);

        const double kmPerDegree = 6371.0088 * Math.PI / 180;
        for (var y = 0; y < height; y++)
        {
            var latitude = GridNorth - (row0 + y + 0.5) * CellDegrees;
            var cellM2 = Math.Pow(CellDegrees * kmPerDegree * 1000, 2) * Math.Cos(latitude * Math.PI / 180);
            for (var x = 0; x < width; x++)
            {
                var value = values[y * width + x];
                if (value != GeoTiff.NoData)
                {
                    shares[y * width + x] = (float)Math.Min(1, value / cellM2);
                }
            }
        }
    }

    // The tile's GeoTIFF from the cache or downloaded (and cached); null if the tile does not exist.
    private async Task<Stream?> OpenTileAsync(string name, CancellationToken cancellationToken)
    {
        var cached = _options.CacheDirectory is { } directory ? Path.Combine(directory, name + ".tif") : null;
        var missing = cached is null ? null : Path.ChangeExtension(cached, ".none");
        if (cached is not null && File.Exists(cached))
        {
            return File.OpenRead(cached);
        }
        if (missing is not null && File.Exists(missing))
        {
            return null;
        }

        var zip = await DownloadAsync(new Uri(_options.BaseUri, name + ".zip"), cancellationToken);
        if (zip is null)
        {
            if (missing is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(missing)!);
                await File.WriteAllTextAsync(missing, "", cancellationToken);
            }
            return null;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
                        ?? throw new BuiltUpException($"GHSL tile {name} has no GeoTIFF inside.");
            if (cached is null)
            {
                var memory = new MemoryStream();
                await using (var source = entry.Open())
                {
                    await source.CopyToAsync(memory, cancellationToken);
                }
                memory.Position = 0;
                return memory;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            // Written aside and moved, so an interrupted copy never leaves a broken file in the cache.
            var partial = cached + ".part";
            await using (var source = entry.Open())
            await using (var target = File.Create(partial))
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            File.Move(partial, cached, overwrite: true);
            return File.OpenRead(cached);
        }
        catch (InvalidDataException ex)
        {
            throw new BuiltUpException($"GHSL tile {name} is not a readable zip.", ex);
        }
    }

    /// <summary>Body of a successful response; null for 404 (no such tile).</summary>
    private async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new BuiltUpException($"GHSL returned HTTP {(int)response.StatusCode} for {uri.AbsolutePath}.");
            }
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new BuiltUpException($"GHSL request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BuiltUpException("GHSL request timed out.", ex);
        }
    }

    private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
}
