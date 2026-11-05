using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public partial class LoopWaypointsTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;

    [Fact]
    public void Starts_and_ends_at_the_start_with_four_points_at_the_radius()
    {
        var fan = LoopWaypoints.Fan(Origin, 90, 10);

        Assert.Equal(6, fan.Count);
        Assert.Equal(Origin, fan[0]);
        Assert.Equal(Origin, fan[^1]);
        Assert.All(fan.Skip(1).SkipLast(1), p => Assert.Equal(10, Origin.DistanceKmTo(p), precision: 9));
    }

    [Theory]
    [InlineData(90, new double[] { 36, 72, 108, 144 })]
    [InlineData(0, new double[] { 306, 342, 18, 54 })]
    [InlineData(-90, new double[] { 216, 252, 288, 324 })]
    [InlineData(270, new double[] { 216, 252, 288, 324 })]
    public void Spreads_the_points_over_180_degrees_centred_on_the_bearing_clockwise(double bearing, double[] expected)
    {
        var fan = LoopWaypoints.Fan(Origin, bearing, 10);

        var bearings = fan.Skip(1).SkipLast(1).Select(p => Origin.BearingTo(p)).ToList();
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(0, Compass.AngleBetween(expected[i], bearings[i]), precision: 6);
        }
    }

    [Fact]
    public void Takes_any_number_of_points_from_three()
    {
        var fan = LoopWaypoints.Fan(Origin, 0, 5, points: 3);

        // Start, two points at −30° and +30°, start.
        Assert.Equal(4, fan.Count);
        Assert.Equal(0, Compass.AngleBetween(330, Origin.BearingTo(fan[1])), precision: 6);
        Assert.Equal(0, Compass.AngleBetween(30, Origin.BearingTo(fan[2])), precision: 6);
    }

    [Fact]
    public void Works_across_the_180_degree_meridian()
    {
        var start = new Coordinate(-16.5, 179.95);

        var fan = LoopWaypoints.Fan(start, 90, 10);

        Assert.Contains(fan, p => p.Longitude < 0);
        Assert.All(fan.Skip(1).SkipLast(1), p => Assert.Equal(10, start.DistanceKmTo(p), precision: 9));
    }

    [Fact]
    public void The_straight_polygon_of_a_five_point_fan_is_3_85_times_the_radius()
    {
        // Measured on a small fan, where the sphere is as good as flat.
        var fan = LoopWaypoints.Fan(Origin, 45, 1);
        var length = fan.Zip(fan.Skip(1), (a, b) => a.DistanceKmTo(b)).Sum();

        Assert.Equal(3.854102, LoopWaypoints.StraightLengthPerRadius(), precision: 6);
        Assert.Equal(LoopWaypoints.StraightLengthPerRadius(), length, precision: 4);
    }

    [Fact]
    public void The_first_radius_divides_the_target_by_the_straight_polygon_and_the_road_factor()
    {
        // 3.854 × 1.5 = 5.78: a 60 km loop starts with R = 10.38 km.
        Assert.Equal(10.379, LoopWaypoints.InitialRadiusKm(60), precision: 3);
        Assert.Equal(2 * LoopWaypoints.InitialRadiusKm(40), LoopWaypoints.InitialRadiusKm(80), precision: 9);
    }

    [Fact]
    public void Rejects_bad_radii_targets_and_point_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopWaypoints.Fan(Origin, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopWaypoints.Fan(Origin, double.PositiveInfinity, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopWaypoints.Fan(Origin, 0, 5, points: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopWaypoints.Fan(Origin, 0, 5, points: 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopWaypoints.InitialRadiusKm(0));
    }
}
