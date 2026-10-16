using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class ReflectivityTests
{
    [Theory]
    [InlineData(20, 0.648)]
    [InlineData(30, 2.73)]
    [InlineData(40, 11.53)]
    [InlineData(50, 48.6)]
    public void Uses_marshall_palmer(double dbz, double mmPerHour)
    {
        Assert.Equal(mmPerHour, Reflectivity.ToRainRate(dbz), tolerance: mmPerHour * 0.005);
    }

    [Fact]
    public void Weak_echoes_are_next_to_nothing()
    {
        Assert.Equal(0.075, Reflectivity.ToRainRate(5), tolerance: 0.001);
        Assert.True(Reflectivity.ToRainRate(-32) < 1e-3);
    }

    [Fact]
    public void Caps_hail_echoes_at_about_100_mm_per_hour()
    {
        Assert.Equal(99.9, Reflectivity.ToRainRate(55), tolerance: 0.5);
        Assert.Equal(Reflectivity.ToRainRate(55), Reflectivity.ToRainRate(67));
    }

    [Fact]
    public void Rejects_non_finite_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reflectivity.ToRainRate(double.NaN));
    }
}
