using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideBearingsTests
{
    private static StartDirectionAdvice Wind(StartAdviceKind kind, double? intoWind) =>
        new(kind, intoWind, intoWind, 15, 15, 1, 3);

    [Fact]
    public void Looks_straight_into_the_wind_and_45_degrees_to_each_side() =>
        Assert.Equal([194.0, 239, 149], RideBearings.For(Wind(StartAdviceKind.HeadIntoWind, 194), 3));

    [Fact]
    public void Goes_on_widening_for_more_loops() =>
        Assert.Equal([10.0, 55, 325, 100, 280, 145], RideBearings.For(Wind(StartAdviceKind.HeadIntoWind, 10), 6));

    [Fact]
    public void Gives_a_single_loop_straight_into_the_wind() =>
        Assert.Equal([300.0], RideBearings.For(Wind(StartAdviceKind.HeadIntoWind, 300), 1));

    [Theory]
    [InlineData(StartAdviceKind.Calm)]
    [InlineData(StartAdviceKind.Variable)]
    public void Spreads_them_evenly_when_the_wind_does_not_matter(StartAdviceKind kind) =>
        Assert.Equal([0.0, 120, 240], RideBearings.For(Wind(kind, null), 3));

    [Fact]
    public void Takes_the_spread_from_the_caller() =>
        Assert.Equal([90.0, 120, 60], RideBearings.For(Wind(StartAdviceKind.HeadIntoWind, 90), 3, spreadDegrees: 30));
}
