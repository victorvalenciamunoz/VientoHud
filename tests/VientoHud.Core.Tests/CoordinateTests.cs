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
}
