using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class DirectionAdvisorTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private static FakeWindForecastProvider Wind(double speed, double from) =>
        new(Enumerable.Range(0, 8).Select(h => new WindSample(Eight.AddHours(h), speed, speed + 10, from, "fake")).ToList());

    [Fact]
    public async Task Asks_the_hours_of_the_ride_at_the_start_and_says_where_to_head_with_a_fan()
    {
        var wind = Wind(15, 194);

        var advice = await new DirectionAdvisor(wind).AdviseAsync(Leon, 60, Eight);

        // 60 km at 25 km/h from 08:00: 3 hours, one place.
        Assert.Equal(3, advice.Hours);
        Assert.Single(wind.Requests);
        Assert.Equal([Leon], wind.Requests[0]);
        Assert.Equal(StartAdviceKind.HeadIntoWind, advice.Wind.Kind);
        Assert.Equal(194, advice.Wind.StartBearingDegrees!.Value, 0);
        Assert.NotNull(advice.Fan);
        Assert.Equal(Leon, advice.Fan![0]);
    }

    [Fact]
    public async Task Calm_wind_gives_no_fan()
    {
        var advice = await new DirectionAdvisor(Wind(2, 194)).AdviseAsync(Leon, 60, Eight.AddMinutes(50));

        Assert.Equal(StartAdviceKind.Calm, advice.Wind.Kind);
        Assert.Null(advice.Fan);
        // 2,4 h from 08:50 ends in the 11:00 hour: 4 hours.
        Assert.Equal(4, advice.Hours);
    }

    [Fact]
    public async Task Departure_must_be_in_UTC()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DirectionAdvisor(Wind(15, 194)).AdviseAsync(Leon, 60, Eight.ToOffset(TimeSpan.FromHours(2))));
    }
}
