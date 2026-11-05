namespace VientoHud.Core.Tests;

public class CoordinateTests
{
    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(45.0, 1.0)]
    public void Accepts_valid_coordinates(double latitude, double longitude)
    {
        var coordinate = new Coordinate(latitude, longitude);

        Assert.Equal(latitude, coordinate.Latitude);
        Assert.Equal(longitude, coordinate.Longitude);
    }

    [Theory]
    [InlineData(90.1, 0)]
    [InlineData(-90.1, 0)]
    [InlineData(0, 180.1)]
    [InlineData(0, -180.1)]
    [InlineData(double.NaN, 0)]
    public void Rejects_out_of_range_coordinates(double latitude, double longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Coordinate(latitude, longitude));

    [Fact]
    public void One_degree_of_longitude_on_the_equator_is_about_111_km() =>
        Assert.Equal(111.195, new Coordinate(0, 0).DistanceKmTo(new Coordinate(0, 1)), precision: 3);

    [Fact]
    public void Distance_to_itself_is_zero() =>
        Assert.Equal(0, new Coordinate(45.0, 1.0).DistanceKmTo(new Coordinate(45.0, 1.0)));

    [Fact]
    public void Antipodes_are_half_the_circumference_apart() =>
        // Within a metre: floating-point rounding near antipodes leaves ~0.1 m.
        Assert.Equal(Math.PI * 6371.0088, new Coordinate(10, 20).DistanceKmTo(new Coordinate(-10, -160)), tolerance: 0.001);

    [Fact]
    public void Two_degrees_north_and_one_west_is_about_235_km()
    {
        // 2° of latitude are 222.4 km and 1° of longitude at 46° N is 77.3 km: √(222.4² + 77.3²) ≈ 235.4 km on the flat,
        // and the haversine formula worked out by hand gives 235.42 km.
        var distance = new Coordinate(45.0, 1.0).DistanceKmTo(new Coordinate(47.0, 0.0));

        Assert.Equal(235.42, distance, tolerance: 0.01);
    }

    [Theory]
    [InlineData(0, 1, 90)]
    [InlineData(1, 0, 0)]
    [InlineData(0, -1, 270)]
    [InlineData(-1, 0, 180)]
    public void Bearing_is_towards_the_other_point_clockwise_from_north(double latitude, double longitude, double expected) =>
        Assert.Equal(expected, new Coordinate(0, 0).BearingTo(new Coordinate(latitude, longitude)), precision: 9);

    [Fact]
    public void Bearing_two_degrees_north_and_one_west_is_north_north_west()
    {
        // 222.4 km north and 77.3 km west: atan2(−77.3, 222.4) ≈ −19.2°, so ~341° (341.2° by the great-circle formula).
        var bearing = new Coordinate(45.0, 1.0).BearingTo(new Coordinate(47.0, 0.0));

        Assert.InRange(bearing, 340, 342);
        Assert.Equal(CompassPoint.NNW, Compass.ToCompassPoint(bearing));
    }

    [Fact]
    public void Destination_100_km_north_is_100_km_of_latitude_away()
    {
        var destination = new Coordinate(45, 1).Destination(0, 100);

        Assert.Equal(45 + 100 / (6371.0088 * Math.PI / 180), destination.Latitude, precision: 9);
        Assert.Equal(1, destination.Longitude, precision: 9);
    }

    [Fact]
    public void Destination_wraps_longitude_across_the_180_degree_meridian()
    {
        var destination = new Coordinate(0, 179.5).Destination(90, 6371.0088 * Math.PI / 180);

        Assert.Equal(0, destination.Latitude, precision: 9);
        Assert.Equal(-179.5, destination.Longitude, precision: 9);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(37, 3)]
    [InlineData(200, 80)]
    [InlineData(-90, 12)]
    public void Destination_is_at_that_distance_and_initial_bearing(double bearing, double km)
    {
        var start = new Coordinate(45.0, 1.0);

        var destination = start.Destination(bearing, km);

        Assert.Equal(km, start.DistanceKmTo(destination), precision: 9);
        Assert.Equal(0, Compass.AngleBetween((bearing + 360) % 360, start.BearingTo(destination)), precision: 9);
    }

    [Fact]
    public void Destination_rejects_negative_distances_and_non_finite_bearings()
    {
        var start = new Coordinate(0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => start.Destination(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => start.Destination(double.NaN, 1));
    }
}
