namespace VientoHud.Core.Flyover;

/// <summary>A web map tile (slippy map, Web Mercator): zoom level and column and row from the north-west.</summary>
public readonly record struct TileAddress(int Zoom, int X, int Y)
{
    /// <summary>The tile that contains <paramref name="position"/> at <paramref name="zoom"/>.</summary>
    public static TileAddress Covering(Coordinate position, int zoom)
    {
        if (zoom is < 0 or > 22)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be 0-22.");
        }
        var n = 1 << zoom;
        var x = (int)Math.Floor((position.Longitude + 180) / 360 * n);
        var latitude = Math.Clamp(position.Latitude, -85.0511, 85.0511) * Math.PI / 180;
        var y = (int)Math.Floor((1 - Math.Log(Math.Tan(latitude) + 1 / Math.Cos(latitude)) / Math.PI) / 2 * n);
        return new TileAddress(zoom, Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }

    /// <summary>The path "{z}/{x}/{y}" the tile servers use.</summary>
    public string Path => $"{Zoom}/{X}/{Y}";
}

/// <summary>
/// The terrain tiles of the 3D view (DECISIONS 056): Mapzen's Terrarium PNGs in AWS Open Data, and which of them
/// cover a route. MapLibre asks for them by zoom as the camera moves; a corridor from <see cref="MinZoom"/> to
/// <see cref="MaxZoom"/> around the route is all it ever needs, so once it is in the cache the view works offline.
/// </summary>
public static class TerrainTiles
{
    /// <summary>Name of the tile set in the cache.</summary>
    public const string Source = "terrarium";

    /// <summary>Lowest zoom to keep: at 8 one or two tiles cover a whole ride's region.</summary>
    public const int MinZoom = 8;

    /// <summary>
    /// Highest zoom to keep and the most MapLibre is told to ask for: at 12 a pixel is about 30 m at 40° north, the
    /// data's own resolution (SRTM 30 m); higher zooms would only oversample.
    /// </summary>
    public const int MaxZoom = 12;

    /// <summary>How far around the track the corridor reaches, in km.</summary>
    public const double DefaultMarginKm = 2;

    /// <summary>The URL of a tile, with the {z}/{x}/{y} template the view uses too.</summary>
    public const string UrlTemplate = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";

    public static Uri Url(TileAddress tile) => new(UrlTemplate.Replace("{z}/{x}/{y}", tile.Path));

    /// <summary>Whether <paramref name="url"/> is one of these tiles, and which.</summary>
    public static bool TryParse(Uri url, out TileAddress tile)
    {
        tile = default;
        if (!url.Host.Equals("s3.amazonaws.com", StringComparison.OrdinalIgnoreCase)
            || !url.AbsolutePath.StartsWith("/elevation-tiles-prod/terrarium/", StringComparison.Ordinal))
        {
            return false;
        }
        return TileCorridor.TryParsePath(url.AbsolutePath["/elevation-tiles-prod/terrarium/".Length..], out tile);
    }

    /// <summary>
    /// Every tile from <see cref="MinZoom"/> to <see cref="MaxZoom"/> within <paramref name="marginKm"/> of the
    /// track, zoom by zoom, in riding order within each zoom.
    /// </summary>
    public static IReadOnlyList<TileAddress> Corridor(IReadOnlyList<Coordinate> track, double marginKm = DefaultMarginKm) =>
        Enumerable.Range(MinZoom, MaxZoom - MinZoom + 1).SelectMany(zoom => TileCorridor.Cover(track, zoom, marginKm)).ToList();
}

/// <summary>Which tiles of a zoom level a track passes through, with a margin around it.</summary>
public static class TileCorridor
{
    /// <summary>
    /// The distinct tiles at <paramref name="zoom"/> whose area lies within <paramref name="marginKm"/> of some track
    /// point (each point's margin as a box: a little generous at the corners, never short), in the order the track
    /// first reaches them.
    /// </summary>
    public static IReadOnlyList<TileAddress> Cover(IReadOnlyList<Coordinate> track, int zoom, double marginKm)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.Count == 0)
        {
            throw new ArgumentException("The track has no points.", nameof(track));
        }
        if (!double.IsFinite(marginKm) || marginKm < 0 || marginKm > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(marginKm), marginKm, "The margin must be 0-50 km.");
        }
        var seen = new HashSet<TileAddress>();
        var tiles = new List<TileAddress>();
        foreach (var point in track)
        {
            var north = point.Destination(0, marginKm);
            var south = point.Destination(180, marginKm);
            var east = point.Destination(90, marginKm);
            var west = point.Destination(270, marginKm);
            var northWest = TileAddress.Covering(new Coordinate(north.Latitude, west.Longitude), zoom);
            var southEast = TileAddress.Covering(new Coordinate(south.Latitude, east.Longitude), zoom);
            for (var x = northWest.X; x <= southEast.X; x++)
            {
                for (var y = northWest.Y; y <= southEast.Y; y++)
                {
                    var tile = new TileAddress(zoom, x, y);
                    if (seen.Add(tile))
                    {
                        tiles.Add(tile);
                    }
                }
            }
        }
        return tiles;
    }

    /// <summary>Reads "{z}/{x}/{y}" (with or without an extension) into a tile.</summary>
    public static bool TryParsePath(string path, out TileAddress tile)
    {
        tile = default;
        var parts = path.Split('/');
        if (parts.Length != 3)
        {
            return false;
        }
        var last = parts[2];
        var dot = last.IndexOf('.');
        if (dot >= 0)
        {
            last = last[..dot];
        }
        if (!int.TryParse(parts[0], out var z) || !int.TryParse(parts[1], out var x) || !int.TryParse(last, out var y)
            || z is < 0 or > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z)
        {
            return false;
        }
        tile = new TileAddress(z, x, y);
        return true;
    }
}
