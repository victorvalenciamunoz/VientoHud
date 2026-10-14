namespace VientoHud.Core.Tests;

public class CompassTests
{
    [Theory]
    [InlineData(0, CompassPoint.N)]
    [InlineData(11.24, CompassPoint.N)]
    [InlineData(11.25, CompassPoint.NNE)]
    [InlineData(22.5, CompassPoint.NNE)]
    [InlineData(45, CompassPoint.NE)]
    [InlineData(90, CompassPoint.E)]
    [InlineData(180, CompassPoint.S)]
    [InlineData(202.5, CompassPoint.SSW)]
    [InlineData(270, CompassPoint.W)]
    [InlineData(337.5, CompassPoint.NNW)]
    [InlineData(348.74, CompassPoint.NNW)]
    [InlineData(348.75, CompassPoint.N)]
    [InlineData(359.9, CompassPoint.N)]
    [InlineData(360, CompassPoint.N)]
    [InlineData(-90, CompassPoint.W)]
    [InlineData(720 + 45, CompassPoint.NE)]
    public void Maps_angle_to_nearest_of_16_points(double degrees, CompassPoint expected) =>
        Assert.Equal(expected, Compass.ToCompassPoint(degrees));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_non_finite_angles(double degrees) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Compass.ToCompassPoint(degrees));

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(0, 180, 180)]
    [InlineData(0, 0, 0)]
    [InlineData(-90, 90, 180)]
    [InlineData(720, 45, 45)]
    [InlineData(200, 10, 170)]
    public void Angle_between_is_the_smallest_one(double a, double b, double expected)
    {
        Assert.Equal(expected, Compass.AngleBetween(a, b), precision: 9);
        Assert.Equal(expected, Compass.AngleBetween(b, a), precision: 9);
    }
}
