using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class RainGridTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    // 3 × 3 cells of 1 km with Madrid in the middle one; rain only in the north-east corner, no data in the south-west.
    private static RainGrid Create()
    {
        var (x, y) = Opera.Forward(Madrid);
        var left = Math.Floor(x / 1000) * 1000 - 1000;
        var top = Math.Ceiling(y / 1000) * 1000 + 1000;
        float[] rates = [0, 0, 4.5f, 0, 0, 0, float.NaN, 0, 0];
        return new RainGrid(Time, Opera, left, top, 1000, 3, 3, rates);
    }

    [Fact]
    public void Finds_the_cell_of_a_position()
    {
        var grid = Create();

        Assert.Equal((1, 1), grid.CellOf(Madrid));
        Assert.Equal(0f, grid.RateAt(Madrid));
    }

    [Fact]
    public void Row_zero_is_north_and_column_zero_is_west()
    {
        var grid = Create();
        var northEast = grid.CenterOf(0, 2);
        var southWest = grid.CenterOf(2, 0);

        Assert.True(northEast.Latitude > Madrid.Latitude && northEast.Longitude > Madrid.Longitude);
        Assert.True(southWest.Latitude < Madrid.Latitude && southWest.Longitude < Madrid.Longitude);
        Assert.Equal(4.5f, grid.RateAt(northEast));
        Assert.True(float.IsNaN(grid.RateAt(southWest)!.Value));
    }

    [Fact]
    public void Cell_centres_are_a_cell_apart_and_inside_their_cell()
    {
        var grid = Create();

        Assert.Equal(1.0, grid.CenterOf(1, 0).DistanceKmTo(grid.CenterOf(1, 1)), tolerance: 0.01);
        Assert.Equal(1.0, grid.CenterOf(0, 1).DistanceKmTo(grid.CenterOf(1, 1)), tolerance: 0.01);
        Assert.Equal((2, 1), grid.CellOf(grid.CenterOf(2, 1)));
    }

    [Fact]
    public void Positions_outside_have_no_cell()
    {
        var grid = Create();

        Assert.Null(grid.CellOf(new Coordinate(42.5987, -5.5671)));
        Assert.Null(grid.RateAt(new Coordinate(42.5987, -5.5671)));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.CenterOf(0, -1));
    }

    [Fact]
    public void Keeps_its_own_copy_of_the_rates()
    {
        float[] rates = [1, 2, 3, 4];
        var grid = new RainGrid(Time, Opera, 0, 0, 1000, 2, 2, rates);

        rates[0] = 99;

        Assert.Equal(1f, grid[0, 0]);
    }

    [Fact]
    public void Validates_its_input()
    {
        Assert.Throws<ArgumentException>(() =>
            new RainGrid(Time.ToOffset(TimeSpan.FromHours(2)), Opera, 0, 0, 1000, 1, 1, [0]));
        Assert.Throws<ArgumentException>(() => new RainGrid(Time, Opera, 0, 0, 1000, 2, 2, [0, 0, 0]));
        Assert.Throws<ArgumentException>(() => new RainGrid(Time, Opera, 0, 0, 1000, 1, 1, [-1]));
        Assert.Throws<ArgumentException>(() => new RainGrid(Time, Opera, 0, 0, 1000, 1, 1, [float.PositiveInfinity]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RainGrid(Time, Opera, 0, 0, 0, 1, 1, [0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RainGrid(Time, Opera, 0, 0, 1000, 0, 1, []));
    }
}
