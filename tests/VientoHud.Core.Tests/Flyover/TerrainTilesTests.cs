using VientoHud.Core.Tests.Support;
using VientoHud.Core.Flyover;

namespace VientoHud.Core.Tests.Flyover;

public class TerrainTilesTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;

    [Fact]
    public void Finds_the_tile_of_a_position()
    {
        Assert.Equal(new TileAddress(0, 0, 0), TileAddress.Covering(Origin, 0));
        // At zoom 1 the world is 2 × 2: the Greenwich equator point is the south-east one.
        Assert.Equal(new TileAddress(1, 1, 1), TileAddress.Covering(new Coordinate(0, 0), 1));
        // A hair north-west of it, the north-west one.
        Assert.Equal(new TileAddress(1, 0, 0), TileAddress.Covering(new Coordinate(0.001, -0.001), 1));
        // The test origin (45° N, 1° E) at zoom 12: column 2059, row 1473 (worked out by hand with the slippy-map formula).
        Assert.Equal(new TileAddress(12, 2059, 1473), TileAddress.Covering(Origin, 12));
        Assert.Equal("12/2059/1473", TileAddress.Covering(Origin, 12).Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => TileAddress.Covering(Origin, 23));
    }

    [Fact]
    public void A_corridor_covers_the_track_and_its_margin_once_per_tile()
    {
        // 10 km east, a point every 100 m.
        var track = Enumerable.Range(0, 101).Select(i => Origin.Destination(90, i * 0.1)).ToList();

        var tiles = TileCorridor.Cover(track, 12, marginKm: 2);

        // At zoom 12 and 45° north a tile is about 7 km wide and 7 km tall: 10 km plus 2 km each side takes 2-3
        // columns, and 4 km of height 1-2 rows.
        Assert.InRange(tiles.Count, 2, 6);
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
        Assert.All(tiles, t => Assert.Equal(12, t.Zoom));
        Assert.Contains(TileAddress.Covering(Origin, 12), tiles);
        Assert.Contains(TileAddress.Covering(track[^1], 12), tiles);
        // The margin reaches 2 km north of the start.
        Assert.Contains(TileAddress.Covering(Origin.Destination(0, 1.9), 12), tiles);
        // The first tile is the one at the start.
        Assert.Equal(TileAddress.Covering(Origin.Destination(315, 2.8), 12), tiles[0]);
    }

    [Fact]
    public void The_terrain_corridor_spans_the_zooms_the_view_asks_for()
    {
        var track = Enumerable.Range(0, 51).Select(i => Origin.Destination(45, i * 0.5)).ToList();

        var tiles = TerrainTiles.Corridor(track);

        Assert.Equal(TerrainTiles.MinZoom, tiles.Min(t => t.Zoom));
        Assert.Equal(TerrainTiles.MaxZoom, tiles.Max(t => t.Zoom));
        // Fewer tiles at each lower zoom.
        var byZoom = tiles.GroupBy(t => t.Zoom).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
        Assert.True(byZoom.Zip(byZoom.Skip(1)).All(pair => pair.First <= pair.Second), string.Join(",", byZoom));
        Assert.InRange(tiles.Count, 10, 80);
    }

    [Fact]
    public void Knows_its_own_urls()
    {
        var tile = new TileAddress(12, 2059, 1473);

        var url = TerrainTiles.Url(tile);

        Assert.Equal("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/2059/1473.png", url.ToString());
        Assert.True(TerrainTiles.TryParse(url, out var parsed));
        Assert.Equal(tile, parsed);
        Assert.False(TerrainTiles.TryParse(new Uri("https://tile.thunderforest.com/cycle/12/2059/1473.png?apikey=x"), out _));
        Assert.False(TerrainTiles.TryParse(new Uri("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/2059.png"), out _));
    }

    [Theory]
    [InlineData("12/2059/1473.png", true, 12, 2059, 1473)]
    [InlineData("12/2059/1473", true, 12, 2059, 1473)]
    [InlineData("12/2059/1473.png?apikey=x", true, 12, 2059, 1473)]
    [InlineData("12/4096/1473.png", false, 0, 0, 0)]
    [InlineData("a/b/c.png", false, 0, 0, 0)]
    [InlineData("12/2059", false, 0, 0, 0)]
    public void Reads_tile_paths(string path, bool ok, int z, int x, int y)
    {
        Assert.Equal(ok, TileCorridor.TryParsePath(path, out var tile));
        if (ok)
        {
            Assert.Equal(new TileAddress(z, x, y), tile);
        }
    }
}
