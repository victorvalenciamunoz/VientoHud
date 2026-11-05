using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class RainIntensityTests
{
    [Theory]
    [InlineData(0.2, RainIntensity.Light)]
    [InlineData(2, RainIntensity.Light)]
    [InlineData(2.1, RainIntensity.Moderate)]
    [InlineData(15, RainIntensity.Moderate)]
    [InlineData(15.1, RainIntensity.Heavy)]
    [InlineData(30.1, RainIntensity.VeryHeavy)]
    [InlineData(60, RainIntensity.VeryHeavy)]
    [InlineData(60.1, RainIntensity.Torrential)]
    public void Uses_aemet_thresholds(double mmPerHour, RainIntensity expected)
    {
        Assert.Equal(expected, RainIntensities.Classify(mmPerHour));
    }

    [Fact]
    public void Rejects_negative_or_missing_rates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RainIntensities.Classify(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RainIntensities.Classify(double.NaN));
    }
}
