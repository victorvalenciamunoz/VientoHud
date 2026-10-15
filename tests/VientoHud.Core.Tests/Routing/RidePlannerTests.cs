using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RidePlannerTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    private static FakeWindForecastProvider Wind(double speed, double from) =>
        new(Enumerable.Range(0, 8).Select(h => new WindSample(Eight.AddHours(h), speed, speed + 10, from, "fake")).ToList());

    // Every fan of waypoints makes a valid loop of exactly the target length.
    private static FakeRouteEngine Roads() => new(w => FakeRouteEngine.Straight(w, roadFactor: 1.5));

    [Fact]
    public async Task Looks_for_loops_into_the_wind_and_ranks_the_one_straight_into_it_first()
    {
        var wind = Wind(15, 194);
        var planner = new RidePlanner(new LoopGenerator(Roads()), wind);

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.Equal(StartAdviceKind.HeadIntoWind, plan.Wind.Kind);
        Assert.Equal([194.0, 239, 149], plan.Bearings);
        Assert.Equal(3, plan.Ranked.Count);
        Assert.Equal(3, plan.Ranked.Select(o => o.RouteIndex).Distinct().Count());
        Assert.Equal(194, plan.Loops[plan.Ranked[0].RouteIndex].BearingDegrees);
        Assert.True(plan.Ranked[0].Score.WindAdvantageKmh > plan.Ranked[2].Score.WindAdvantageKmh);
        // One forecast for the start (60 km at 25 km/h: 3 hours) and one for all the loops.
        Assert.Equal(2, wind.Requests.Count);
        Assert.Equal([Leon], wind.Requests[0]);
    }

    [Fact]
    public async Task Spreads_the_loops_evenly_when_the_wind_is_calm()
    {
        var planner = new RidePlanner(new LoopGenerator(Roads()), Wind(2, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.Equal(StartAdviceKind.Calm, plan.Wind.Kind);
        Assert.Equal([0.0, 120, 240], plan.Bearings);
        Assert.Equal(3, plan.Ranked.Count);
    }

    [Fact]
    public async Task Lets_the_rider_choose_the_first_bearing()
    {
        var planner = new RidePlanner(new LoopGenerator(Roads()), Wind(15, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight, firstBearing: 90);

        Assert.Equal([90.0, 210, 330], plan.Bearings);
    }

    [Fact]
    public async Task Ranks_nothing_and_asks_no_more_wind_when_no_loop_is_valid()
    {
        // Every loop runs on a motorway: forbidden.
        var wind = Wind(15, 194);
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5, highway: "motorway"));
        var planner = new RidePlanner(new LoopGenerator(engine), wind);

        var plan = await planner.PlanAsync(Leon, 60, Eight, count: 1);

        Assert.Empty(plan.Ranked);
        // The one bearing into the wind and the three of the wider fan.
        Assert.Equal(4, plan.Loops.Count);
        Assert.Single(wind.Requests);
    }

    [Fact]
    public async Task Points_each_ranked_loop_back_to_the_bearing_it_came_from()
    {
        // Around 239° there are only motorways: no valid loop that way.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(
            w, roadFactor: 1.5, highway: Compass.AngleBetween(FakeRouteEngine.FanBearing(w), 239) < 50 ? "motorway" : "tertiary"));
        var planner = new RidePlanner(new LoopGenerator(engine), Wind(15, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.False(plan.Loops.Single(l => l.BearingDegrees == 239).Found);
        Assert.Equal(2, plan.Ranked.Count);
        Assert.All(plan.Ranked, o => Assert.True(plan.Loops[o.RouteIndex].Found));
        Assert.All(plan.Ranked, o => Assert.Equal(
            plan.Loops[o.RouteIndex].Best!.Route.Points[1].Position,
            (o.Reversed ? o.Route.Reversed() : o.Route).Points[1].Position));
    }

    [Fact]
    public async Task Widens_the_fan_until_a_loop_is_valid_when_none_into_the_wind_is()
    {
        // Within 89° of the wind (194°) there are only motorways, so not even turning a bearing 40° helps; 90° to the
        // right (284°) there are good roads.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(
            w, roadFactor: 1.5, highway: Compass.AngleBetween(FakeRouteEngine.FanBearing(w), 194) < 89 ? "motorway" : "tertiary"));
        var planner = new RidePlanner(new LoopGenerator(engine), Wind(15, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.Equal([194.0, 239, 149, 284], plan.Bearings);
        Assert.Equal(4, plan.Loops.Count);
        Assert.Equal(284, plan.Loops[Assert.Single(plan.Ranked).RouteIndex].BearingDegrees);
        Assert.True(plan.Loops[0].Found);
    }

    [Fact]
    public async Task Tries_every_wider_bearing_and_stops_there_when_nothing_is_valid()
    {
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5, highway: "motorway"));
        var planner = new RidePlanner(new LoopGenerator(engine), Wind(15, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.Equal([194.0, 239, 149, 284, 104, 14], plan.Bearings);
        Assert.Empty(plan.Ranked);
    }

    [Fact]
    public async Task Does_not_widen_the_bearings_the_rider_chose()
    {
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5, highway: "motorway"));
        var planner = new RidePlanner(new LoopGenerator(engine), Wind(15, 194));

        var plan = await planner.PlanAsync(Leon, 60, Eight, firstBearing: 90);

        Assert.Equal([90.0, 210, 330], plan.Bearings);
    }

    [Fact]
    public async Task Rejects_a_departure_not_in_utc()
    {
        var planner = new RidePlanner(new LoopGenerator(Roads()), Wind(15, 194));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            planner.PlanAsync(Leon, 60, new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(2))));
    }

    [Theory]
    // 09:00 in Madrid (07:00 UTC in summer time), asking for 10:30: today at 08:30 UTC.
    [InlineData("2026-09-30T07:00:00Z", "10:30", "2026-09-30T08:30:00Z")]
    // 11:00 in Madrid, asking for 10:30: it has gone, so tomorrow.
    [InlineData("2026-09-30T09:00:00Z", "10:30", "2026-10-01T08:30:00Z")]
    // The very minute: still today.
    [InlineData("2026-09-30T08:30:20Z", "10:30", "2026-09-30T08:30:00Z")]
    // Winter time (UTC+1) after the clocks go back on 25 October 2026.
    [InlineData("2026-10-25T20:00:00Z", "10:30", "2026-10-26T09:30:00Z")]
    // 02:30 does not exist on 28 March 2027 (02:00 jumps to 03:00): 03:30 summer time.
    [InlineData("2027-03-27T22:00:00Z", "02:30", "2027-03-28T01:30:00Z")]
    public void Takes_the_next_time_the_clock_shows_that_time(string now, string at, string expected)
    {
        var departure = DepartureTime.Next(TimeOnly.Parse(at), DateTimeOffset.Parse(now), Madrid);

        Assert.Equal(DateTimeOffset.Parse(expected), departure);
        Assert.Equal(TimeSpan.Zero, departure.Offset);
    }
}
