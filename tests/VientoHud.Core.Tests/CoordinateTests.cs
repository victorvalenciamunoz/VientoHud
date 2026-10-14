namespace VientoHud.Core.Tests;

public class CoordinateTests
{
    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(40.4168, -3.7038)]
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
        Assert.Equal(0, new Coordinate(40.4168, -3.7038).DistanceKmTo(new Coordinate(40.4168, -3.7038)));

    [Fact]
    public void Antipodes_are_half_the_circumference_apart() =>
        // Within a metre: floating-point rounding near antipodes leaves ~0.1 m.
        Assert.Equal(Math.PI * 6371.0088, new Coordinate(10, 20).DistanceKmTo(new Coordinate(-10, -160)), tolerance: 0.001);

    [Fact]
    public void Madrid_to_Leon_is_about_290_km()
    {
        // Puerta del Sol to León cathedral; any online great-circle calculator gives ~290 km.
        var distance = new Coordinate(40.4169, -3.7035).DistanceKmTo(new Coordinate(42.5995, -5.5669));

        Assert.InRange(distance, 285, 295);
    }
}
