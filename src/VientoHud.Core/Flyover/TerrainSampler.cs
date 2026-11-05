namespace VientoHud.Core.Flyover;

/// <summary>
/// The elevation of the relief the 3D view draws (Terrarium tiles at <see cref="TerrainTiles.MaxZoom"/>, through the
/// <see cref="TileStore"/>): for the judge of phase 8, the profile of the flyover against the GPX's (DECISIONS 056).
/// </summary>
public sealed class TerrainSampler(TileStore store, int zoom = TerrainTiles.MaxZoom)
{
    private readonly Dictionary<TileAddress, TerrariumTile?> _tiles = [];

    /// <summary>The relief's elevation at <paramref name="position"/>, interpolated; null when its tile cannot be had.</summary>
    public async Task<double?> ElevationAsync(Coordinate position, CancellationToken cancellationToken = default)
    {
        var n = 1 << zoom;
        var latitude = position.Latitude * Math.PI / 180;
        var worldX = (position.Longitude + 180) / 360 * n;
        var worldY = (1 - Math.Log(Math.Tan(latitude) + 1 / Math.Cos(latitude)) / Math.PI) / 2 * n;
        var address = new TileAddress(zoom, (int)Math.Floor(worldX), (int)Math.Floor(worldY));
        if (!_tiles.TryGetValue(address, out var tile))
        {
            var bytes = await store.GetOrFetchAsync(TerrainTiles.Source, address, TerrainTiles.Url(address), cancellationToken);
            tile = bytes is null ? null : TerrariumTile.Read(bytes);
            _tiles[address] = tile;
        }
        return tile?.AtPixel((worldX - address.X) * tile.Size, (worldY - address.Y) * tile.Size);
    }

    /// <summary>The relief's elevation at each point of a track; null when some tile cannot be had.</summary>
    public async Task<IReadOnlyList<double>?> AlongAsync(IReadOnlyList<Coordinate> track, CancellationToken cancellationToken = default)
    {
        var elevations = new List<double>(track.Count);
        foreach (var point in track)
        {
            if (await ElevationAsync(point, cancellationToken) is not { } metres)
            {
                return null;
            }
            elevations.Add(metres);
        }
        return elevations;
    }
}
