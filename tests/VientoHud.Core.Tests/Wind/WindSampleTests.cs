using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Wind;

public class WindSampleTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(359.9, 359.9)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    public void Normalizes_direction_to_0_360(double input, double expected)
    {
        var sample = new WindSample(Noon, 10, 15, input, "m");

        Assert.Equal(expected, sample.DirectionFromDegrees, precision: 9);
    }

    [Fact]
    public void Rejects_time_not_in_utc() =>
        Assert.Throws<ArgumentException>(() =>
            new WindSample(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.FromHours(2)), 10, 15, 0, "m"));

    [Fact]
    public void Rejects_negative_speed() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindSample(Noon, -1, 15, 0, "m"));

    [Fact]
    public void Rejects_negative_gust() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindSample(Noon, 10, -1, 0, "m"));

    [Fact]
    public void Rejects_non_finite_direction() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindSample(Noon, 10, 15, double.NaN, "m"));
}
