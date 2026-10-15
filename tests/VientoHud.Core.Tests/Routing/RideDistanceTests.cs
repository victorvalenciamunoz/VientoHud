using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Routing;

public class RideDistanceTests
{
    [Theory]
    [InlineData(2, 25, 50)]
    [InlineData(1.5, 30, 45)]
    [InlineData(0.5, 20, 10)]
    public void Is_the_time_times_the_average_speed(double hours, double speedKmh, double expectedKm) =>
        Assert.Equal(expectedKm, RideDistance.FromDuration(TimeSpan.FromHours(hours), speedKmh), precision: 9);

    [Fact]
    public void Assumes_25_km_h_by_default() =>
        Assert.Equal(75, RideDistance.FromDuration(TimeSpan.FromHours(3)), precision: 9);

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(2, 4)]
    [InlineData(2, 61)]
    [InlineData(2, double.NaN)]
    public void Rejects_non_positive_durations_and_implausible_speeds(double hours, double speedKmh) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RideDistance.FromDuration(TimeSpan.FromHours(hours), speedKmh));
}
