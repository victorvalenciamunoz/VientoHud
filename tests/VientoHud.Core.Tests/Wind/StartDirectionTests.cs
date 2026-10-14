using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Wind;

public class StartDirectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private static WindSample[] Hours(params (double SpeedKmh, double FromDegrees)[] winds) =>
        [.. winds.Select((w, i) => new WindSample(Start.AddHours(i), w.SpeedKmh, w.SpeedKmh + 10, w.FromDegrees, "test"))];

    [Fact]
    public void Steady_west_wind_means_start_heading_west()
    {
        var advice = StartDirection.Recommend(Hours((15, 270), (15, 270), (15, 270)));

        Assert.Equal(StartAdviceKind.HeadIntoWind, advice.Kind);
        Assert.Equal(270, advice.StartBearingDegrees!.Value, precision: 9);
        Assert.Equal(270, advice.MeanWindFromDegrees!.Value, precision: 9);
        Assert.Equal(15, advice.MeanVectorSpeedKmh, precision: 9);
        Assert.Equal(15, advice.MeanSpeedKmh, precision: 9);
        Assert.Equal(1, advice.Steadiness, precision: 9);
        Assert.Equal(3, advice.Hours);
    }

    [Fact]
    public void Averages_across_north_without_wrapping_to_south()
    {
        var advice = StartDirection.Recommend(Hours((12, 350), (12, 10)));

        Assert.Equal(StartAdviceKind.HeadIntoWind, advice.Kind);
        Assert.Equal(0, advice.StartBearingDegrees!.Value, precision: 9);
    }

    [Fact]
    public void Stronger_hours_weigh_more()
    {
        // 20 km/h from the north and 10 km/h from the east: the mean leans north, atan2(10, 20) = 26.57°.
        var advice = StartDirection.Recommend(
            Hours((20, 0), (10, 90)),
            new StartDirectionOptions { MinSteadiness = 0 });

        Assert.Equal(Math.Atan2(10, 20) * 180 / Math.PI, advice.StartBearingDegrees!.Value, precision: 9);
        Assert.Equal(Math.Sqrt(10 * 10 + 5 * 5), advice.MeanVectorSpeedKmh, precision: 9);
        Assert.Equal(15, advice.MeanSpeedKmh, precision: 9);
    }

    [Fact]
    public void Weak_wind_is_calm_and_gives_no_bearing()
    {
        var advice = StartDirection.Recommend(Hours((4, 90), (4.9, 100), (4, 80)));

        Assert.Equal(StartAdviceKind.Calm, advice.Kind);
        Assert.Null(advice.StartBearingDegrees);
        Assert.NotNull(advice.MeanWindFromDegrees);
    }

    [Fact]
    public void Wind_that_turns_around_is_variable_even_if_strong()
    {
        var advice = StartDirection.Recommend(Hours((20, 0), (20, 180), (20, 0), (20, 180)));

        Assert.Equal(StartAdviceKind.Variable, advice.Kind);
        Assert.Null(advice.StartBearingDegrees);
        Assert.Null(advice.MeanWindFromDegrees);
        Assert.Equal(0, advice.Steadiness, precision: 9);
        Assert.Equal(20, advice.MeanSpeedKmh, precision: 9);
    }

    [Fact]
    public void Wind_turning_ninety_degrees_is_below_default_steadiness()
    {
        // From N then from E at the same speed: steadiness = cos(45°) = 0.707, just above 0.7.
        var ninety = StartDirection.Recommend(Hours((15, 0), (15, 90)));
        // From N then from ESE (100°): steadiness = cos(50°) = 0.643, below 0.7.
        var hundred = StartDirection.Recommend(Hours((15, 0), (15, 100)));

        Assert.Equal(StartAdviceKind.HeadIntoWind, ninety.Kind);
        Assert.Equal(45, ninety.StartBearingDegrees!.Value, precision: 9);
        Assert.Equal(StartAdviceKind.Variable, hundred.Kind);
    }

    [Fact]
    public void Calm_takes_precedence_over_variable()
    {
        var advice = StartDirection.Recommend(Hours((3, 0), (3, 180)));

        Assert.Equal(StartAdviceKind.Calm, advice.Kind);
    }

    [Fact]
    public void All_zero_wind_is_calm()
    {
        var advice = StartDirection.Recommend(Hours((0, 0), (0, 0)));

        Assert.Equal(StartAdviceKind.Calm, advice.Kind);
        Assert.Null(advice.MeanWindFromDegrees);
        Assert.Equal(0, advice.Steadiness);
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        var hours = Hours((4, 270), (4, 270));

        var advice = StartDirection.Recommend(hours, new StartDirectionOptions { CalmBelowKmh = 3 });

        Assert.Equal(StartAdviceKind.HeadIntoWind, advice.Kind);
        Assert.Equal(270, advice.StartBearingDegrees!.Value, precision: 9);
    }

    [Fact]
    public void Rejects_empty_forecast() =>
        Assert.Throws<ArgumentException>(() => StartDirection.Recommend([]));
}
