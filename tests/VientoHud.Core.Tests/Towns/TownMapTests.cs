using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Towns;
using VientoHud.Core.Towns.Ghsl;

namespace VientoHud.Core.Tests.Towns;

public class TownMapTests
{
    private const double Cell = 0.001;

    // A grid at the equator from rows of text: '#' all built, '+' a tenth built, '.' open ground.
    private static BuiltUpGrid Grid(params string[] rows) => new(
        rows.Length * Cell, 0, Cell, rows[0].Length, rows.Length,
        rows.SelectMany(r => r.Select(c => c switch { '#' => 1f, '+' => 0.1f, _ => 0f })).ToArray());

    private static Coordinate CentreOf(BuiltUpGrid grid, int column, int row) =>
        new(grid.North - (row + 0.5) * Cell, grid.West + (column + 0.5) * Cell);

    private static readonly TownMapOptions Exact = new() { JoinCells = 0, MinAreaKm2 = 0, SimplifyKm = 0 };

    [Fact]
    public void A_block_of_built_cells_is_a_town_with_its_corners_and_area()
    {
        var grid = Grid(
            "......",
            ".###..",
            ".###..",
            "......");

        var map = TownMap.From(grid, Exact);

        var town = Assert.Single(map.Towns);
        Assert.Equal([grid.Corner(1, 1), grid.Corner(4, 1), grid.Corner(4, 3), grid.Corner(1, 3)], town.Outline);
        Assert.Equal(6 * grid.CellAreaKm2(1), town.AreaKm2, 9);
        Assert.True(map.Contains(CentreOf(grid, 2, 2)));
        Assert.False(map.Contains(CentreOf(grid, 4, 2)));
        Assert.False(map.Contains(new Coordinate(1, 1)));
    }

    [Fact]
    public void Blocks_a_street_apart_join_and_far_ones_do_not()
    {
        var grid = Grid(
            "..............",
            ".##.##....##..",
            ".##.##....##..",
            "..............");

        var map = TownMap.From(grid, Exact with { JoinCells = 1 });

        Assert.Equal(2, map.Towns.Count);
        Assert.True(map.Contains(CentreOf(grid, 3, 1)));
        Assert.False(map.Contains(CentreOf(grid, 7, 1)));
    }

    [Fact]
    public void A_park_inside_a_town_is_town()
    {
        var grid = Grid(
            ".......",
            ".#####.",
            ".#...#.",
            ".#...#.",
            ".#####.",
            ".......");

        var map = TownMap.From(grid, Exact);

        Assert.Equal(4, Assert.Single(map.Towns).Outline.Count);
        Assert.True(map.Contains(CentreOf(grid, 3, 3)));
    }

    [Fact]
    public void Small_spots_and_lightly_built_ground_are_not_towns()
    {
        var grid = Grid(
            "..........",
            ".##....++.",
            ".##....++.",
            "..........",
            "......#...");

        // A cell at the equator is ~0.0124 km²: the 2 × 2 block passes 0.04 km², the lone cell does not.
        var map = TownMap.From(grid, Exact with { MinAreaKm2 = 0.04 });

        Assert.Single(map.Towns);
        Assert.False(map.Contains(CentreOf(grid, 6, 4)));
        Assert.False(map.Contains(CentreOf(grid, 7, 1)));
    }

    [Fact]
    public void Corner_to_corner_cells_are_two_towns_largest_first()
    {
        var grid = Grid(
            "......",
            ".#....",
            "..###.",
            "..###.",
            "......");

        var map = TownMap.From(grid, Exact);

        Assert.Equal(2, map.Towns.Count);
        Assert.True(map.Towns[0].AreaKm2 > map.Towns[1].AreaKm2);
        Assert.Equal(4, map.Towns[1].Outline.Count);
    }

    [Fact]
    public void Ragged_edges_are_simplified()
    {
        // A staircase: every row one cell longer than the one above.
        var rows = Enumerable.Range(0, 20).Select(i => "." + new string('#', i + 1) + new string('.', 21 - i)).ToArray();
        var grid = Grid([new string('.', 23), .. rows, new string('.', 23)]);

        var exact = TownMap.From(grid, Exact).Towns[0];
        var simplified = TownMap.From(grid, Exact with { SimplifyKm = 0.2 }).Towns[0];

        Assert.Equal(42, exact.Outline.Count);
        Assert.InRange(simplified.Outline.Count, 3, 6);
        Assert.Equal(exact.AreaKm2, simplified.AreaKm2);
    }

    [Fact]
    public async Task Around_humanes_the_towns_are_where_people_live()
    {
        var file = "GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss_V1_0_R5_C18.zip";
        var server = new BinaryHttpMessageHandler(uri => uri.Segments[^1] == file
            ? (HttpStatusCode.OK, Fixture.ReadBytes("Ghsl/" + file))
            : (HttpStatusCode.NotFound, []));
        var grid = await new GhslBuiltUpSource(new HttpClient(server)).GetAroundAsync(new Coordinate(40.2526, -3.8299), 10);

        var map = TownMap.From(grid);

        // Humanes, Fuenlabrada and Griñón are towns; the fields around them are not.
        Assert.True(map.Contains(new Coordinate(40.2526, -3.8299)));
        Assert.True(map.Contains(new Coordinate(40.2842, -3.7942)));
        Assert.True(map.Contains(new Coordinate(40.2131, -3.8547)));
        Assert.False(map.Contains(new Coordinate(40.23, -3.87)));
        Assert.False(map.Contains(new Coordinate(40.27, -3.90)));
        Assert.InRange(map.Towns.Count, 10, 40);
        Assert.True(map.Towns[0].AreaKm2 > 40);
        Assert.All(map.Towns, t => Assert.True(t.AreaKm2 >= 0.2 && t.Outline.Count >= 3));
    }
}
