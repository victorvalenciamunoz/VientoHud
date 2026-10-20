using VientoHud.Core.Rain;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class TracedRideAdvisorTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    // The same wind for the next 12 hours.
    private static FakeWindForecastProvider Wind(double speed, double from) =>
        new(Enumerable.Range(0, 12).Select(h => new WindSample(Now.AddHours(h), speed, speed + 10, from, "fake")).ToList());

    // A radar frame of 201 × 201 km around León at `time`, dry.
    private static FakeRadarSource DryRadar()
    {
        var (x, y) = Opera.Forward(Leon);
        return new(TimeSpan.FromMinutes(5),
            new RainGrid(Now.AddMinutes(-5), Opera, x - 100_500, y + 100_500, 1000, 201, 201, new float[201 * 201]));
    }

    private static TracedRideAdvisor Advisor(IRouteEngine roads, IRadarSource? radar = null, FakeRainForecastSource? model = null)
    {
        model ??= new FakeRainForecastSource((_, _) => 0);
        return new(roads, Wind(15, 194), new RainOutlookSource(radar ?? DryRadar(), model, model));
    }

    // Roads 1.5 times as long as the straight line between the points.
    private static FakeRouteEngine Roads(string highway = "tertiary") =>
        new(w => FakeRouteEngine.Straight(w, roadFactor: 1.5, highway: highway));

    [Fact]
    public async Task One_point_makes_an_out_and_back_in_one_request()
    {
        var roads = Roads();
        var radar = DryRadar();
        var model = new FakeRainForecastSource((_, _) => 0);
        var far = Leon.Destination(90, 20);

        var ride = await Advisor(roads, radar, model).AdviseAsync(new TracedRequest(Leon, [far], Now), Now);

        Assert.Equal(RouteShape.OutAndBack, ride.Shape);
        // Only the way there is asked for; the route is there and back: 20 km × 1.5 × 2.
        Assert.Single(roads.Requests);
        Assert.Equal([Leon, far], roads.Requests[0]);
        Assert.Equal(60, ride.Route.DistanceKm, 6);
        Assert.Equal(Leon, ride.Route.Points[^1].Position);
        Assert.Equal(0, ride.Analysis.ForbiddenKm);
        // The ride in its better way round at the chosen departure, with the wind along it.
        Assert.Single(ride.Departure.Chosen.Ranked);
        Assert.Equal(60, ride.Ride.Route.DistanceKm, 6);
        Assert.NotEmpty(ride.Ride.Winds);
        // Leave now or every quarter of an hour up to 2 h later; dry and steady wind: now.
        Assert.Equal(9, ride.Departure.Options.Count);
        Assert.False(ride.Departure.Wait);
        // The rain, asked once from now; the ride goes beyond 2 h (up to 2 h later), so AROME too.
        Assert.NotNull(ride.Rain);
        Assert.Null(ride.RainFailure);
        Assert.Single(model.ModelRequests);
        Assert.Equal(1, model.ProbabilityRequests);
    }

    [Fact]
    public async Task Two_or_more_points_make_a_loop_back_to_the_start()
    {
        var roads = Roads();
        var south = Leon.Destination(180, 10);
        var east = Leon.Destination(90, 10);

        var ride = await Advisor(roads).AdviseAsync(new TracedRequest(Leon, [south, east], Now), Now);

        Assert.Equal(RouteShape.Loop, ride.Shape);
        Assert.Single(roads.Requests);
        Assert.Equal([Leon, south, east, Leon], roads.Requests[0]);
        Assert.Equal(1.5 * (10 + south.DistanceKmTo(east) + 10), ride.Route.DistanceKm, 6);
        // Ridden the way traced or the other way round, whichever the wind prefers.
        var second = ride.Ride.Route.Points[1].Position;
        Assert.True(second == ride.Route.Points[1].Position || second == ride.Route.Points[^2].Position);
        Assert.Equal(ride.Route.DistanceKm, ride.Ride.Route.DistanceKm, 6);
    }

    [Fact]
    public async Task Forbidden_roads_are_measured_not_refused()
    {
        var ride = await Advisor(Roads("motorway")).AdviseAsync(new TracedRequest(Leon, [Leon.Destination(0, 20)], Now), Now);

        Assert.Equal(ride.Route.DistanceKm, ride.Analysis.ForbiddenKm, 6);
        Assert.NotNull(ride.Departure.Chosen.Best);
    }

    [Fact]
    public async Task Without_the_rain_still_advises_and_says_why()
    {
        var ride = await Advisor(Roads(), new FailingRadarSource()).AdviseAsync(new TracedRequest(Leon, [Leon.Destination(0, 20)], Now), Now);

        Assert.Null(ride.Rain);
        Assert.Equal("Radar down", ride.RainFailure);
        Assert.NotNull(ride.Departure.Chosen.Best);
    }

    [Fact]
    public async Task A_point_the_engine_cannot_reach_is_reported_as_such()
    {
        var roads = new FakeRouteEngine(_ => throw new RouteException(RouteFailure.Unreachable, "target island"));

        var ex = await Assert.ThrowsAsync<RouteException>(() =>
            Advisor(roads).AdviseAsync(new TracedRequest(Leon, [Leon.Destination(0, 20)], Now), Now));

        Assert.Equal(RouteFailure.Unreachable, ex.Failure);
    }

    [Fact]
    public async Task The_route_alone_comes_first_without_asking_for_any_weather()
    {
        var roads = Roads();
        var model = new FakeRainForecastSource((_, _) => 0);
        var advisor = Advisor(roads, model: model);

        var traced = await advisor.RouteAsync(new TracedRequest(Leon, [Leon.Destination(90, 20)], Now));

        Assert.Single(roads.Requests);
        Assert.Equal(60, traced.Route.DistanceKm, 6);
        Assert.Equal(RouteShape.OutAndBack, traced.Shape);
        Assert.Empty(model.ModelRequests);
        // The weather on top of it: the same result as asking for everything at once.
        var ride = await advisor.AdviseAsync(traced, Now);
        Assert.Same(traced.Route, ride.Route);
        Assert.Equal(9, ride.Departure.Options.Count);
    }

    [Fact]
    public async Task The_wind_is_judged_first_and_the_rain_on_top_without_asking_for_the_wind_again()
    {
        var wind = Wind(15, 194);
        var model = new FakeRainForecastSource((_, _) => 0);
        var advisor = new TracedRideAdvisor(Roads(), wind, new RainOutlookSource(DryRadar(), model, model));
        var traced = await advisor.RouteAsync(new TracedRequest(Leon, [Leon.Destination(90, 20)], Now));

        var windOnly = await advisor.JudgeWindAsync(traced);

        Assert.True(windOnly.RainPending);
        Assert.Null(windOnly.Rain);
        Assert.NotNull(windOnly.Field);
        Assert.Equal(9, windOnly.Departure.Options.Count);
        Assert.NotEmpty(windOnly.Ride.Winds);
        Assert.Empty(model.ModelRequests);
        Assert.Single(wind.Requests);

        var ride = await advisor.JudgeRainAsync(windOnly, Now);

        Assert.False(ride.RainPending);
        Assert.NotNull(ride.Rain);
        Assert.NotNull(ride.Ride.Rain);
        Assert.Single(model.ModelRequests);
        Assert.Single(wind.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => advisor.JudgeRainAsync(ride with { Field = null }, Now));
    }

    [Fact]
    public async Task Says_where_the_route_really_passes_for_each_point_touched()
    {
        var far = Leon.Destination(90, 20);
        var traced = await Advisor(Roads()).RouteAsync(new TracedRequest(Leon, [far], Now));
        // Straight roads pass through the point itself.
        Assert.Equal(far, traced.Reached[0]);

        // A point the engine could not reach: the route turns 2 km short of it.
        var turn = Leon.Destination(90, 18);
        var route = new Route([new RoutePoint(Leon), new RoutePoint(turn), new RoutePoint(Leon)], [], 36, null, "fake");
        var moved = new TracedRoute(new TracedRequest(Leon, [far], Now), route, RouteAnalyzer.Analyze(route, [Leon, far, Leon]));
        Assert.Equal(turn, moved.Reached[0]);
        Assert.Equal(2, moved.Analysis.MaxWaypointOffsetKm, 0);
    }

    [Fact]
    public async Task A_loop_asked_for_through_one_point_lets_the_engine_choose_the_way_back()
    {
        var roads = Roads();
        var far = Leon.Destination(90, 20);

        var traced = await Advisor(roads).RouteAsync(new TracedRequest(Leon, [far], Now, RequestedShape: RouteShape.Loop));

        Assert.Equal(RouteShape.Loop, traced.Shape);
        // Home, the point and home again in one request: not the way there ridden back.
        Assert.Equal([Leon, far, Leon], roads.Requests[0]);
        Assert.Equal(60, traced.Route.DistanceKm, 6);
    }

    [Fact]
    public async Task Proposes_a_far_point_for_an_out_and_back_in_the_direction_asked()
    {
        var roads = Roads();

        var proposal = await Advisor(roads).ProposeFarPointAsync(Leon, 270, 60);

        // 60 km there and back on roads 1,5 times the straight line: the far point at 20 km, towards the west.
        Assert.NotNull(proposal);
        Assert.True(proposal!.Value.Fits);
        Assert.Equal(20, Leon.DistanceKmTo(proposal.Value.FarPoint), 0);
        Assert.Equal(270, Leon.BearingTo(proposal.Value.FarPoint), 0);
        Assert.True(roads.Requests.Count <= 3);

        // No road at all that way: nothing to propose.
        var island = new FakeRouteEngine(_ => throw new RouteException(RouteFailure.Unreachable, "target island"));
        Assert.Null(await Advisor(island).ProposeFarPointAsync(Leon, 270, 60));
    }

    [Fact]
    public async Task Needs_a_point_and_times_in_UTC()
    {
        Assert.Throws<ArgumentException>(() => new TracedRequest(Leon, [], Now));
        Assert.Throws<ArgumentException>(() => new TracedRequest(Leon, [Leon.Destination(0, 20)], Now.ToOffset(TimeSpan.FromHours(2))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Advisor(Roads()).AdviseAsync(new TracedRequest(Leon, [Leon.Destination(0, 20)], Now), Now.ToOffset(TimeSpan.FromHours(2))));
    }
}
