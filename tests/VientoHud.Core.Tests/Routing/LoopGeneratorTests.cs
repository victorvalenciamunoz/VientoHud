using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class LoopGeneratorTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    // With a road factor of 1.5, a fan's straight polygon times 1.5 is exactly the target: the first radius is right.
    private static Route Exact(IReadOnlyList<Coordinate> waypoints) => FakeRouteEngine.Straight(waypoints, roadFactor: 1.5);

    private static double Radius(IReadOnlyList<Coordinate> waypoints) => waypoints[0].DistanceKmTo(waypoints[1]);

    [Fact]
    public async Task Returns_the_first_loop_when_it_is_valid()
    {
        var engine = new FakeRouteEngine(Exact);

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 90, 60);

        Assert.Equal(1, result.Requests);
        var best = Assert.IsType<LoopCandidate>(result.Best);
        Assert.True(best.IsValid);
        Assert.Equal(0, best.DistanceError, precision: 6);
        Assert.Equal(90, best.FanBearingDegrees, precision: 6);
        Assert.True(best.BearingDeviationDegrees < 1, $"{best.BearingDeviationDegrees}°");
        Assert.Equal(LoopWaypoints.InitialRadiusKm(60), Radius(engine.Requests[0]), precision: 6);
    }

    [Fact]
    public async Task Scales_the_radius_when_the_route_is_too_long()
    {
        // Roads twice as long as the straight polygon: the first loop is a third too long.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 2));

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 90, 60);

        Assert.Equal(2, result.Requests);
        Assert.Equal(LoopProblems.Distance, result.Attempts[0].Problems);
        Assert.Equal(0.75, Radius(engine.Requests[1]) / Radius(engine.Requests[0]), precision: 6);
        Assert.True(result.Best!.IsValid);
        // On a sphere the length is not quite proportional to the radius: within a metre.
        Assert.Equal(60, result.Best.Route.DistanceKm, precision: 3);
    }

    [Fact]
    public async Task Tries_between_a_radius_that_fell_short_and_one_that_went_too_far()
    {
        // Roads 1.2 times the polygon below a 9 km radius and 2 times above: the length jumps with the radius.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: Radius(w) < 9 ? 1.2 : 2));

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 90, 60);

        var radii = engine.Requests.Select(Radius).ToList();
        var lengths = result.Attempts.Select(a => a.Route.DistanceKm).ToList();
        // First 10.38 km → 80 km; then 7.78 km → 36 km; then in between, 7.78 + (60 − 36) × (10.38 − 7.78) / (80 − 36).
        Assert.Equal(radii[1] + (60 - lengths[1]) * (radii[0] - radii[1]) / (lengths[0] - lengths[1]), radii[2], precision: 6);
    }

    [Fact]
    public async Task Moves_a_waypoint_reached_through_a_dead_end_to_where_the_dead_end_leaves_the_route()
    {
        // The third waypoint is off the roads: the route reaches it along a dead end from 30 % of the radius to 90 %,
        // ridden in and out. Once moved to the dead end's start, it is on the roads.
        Coordinate? deadEndStart = null;
        var engine = new FakeRouteEngine(w =>
        {
            var radius = Radius(w);
            if (w[0].DistanceKmTo(w[2]) < 0.6 * radius)
            {
                return Exact(w);
            }
            var bearing = w[0].BearingTo(w[2]);
            deadEndStart = w[0].Destination(bearing, 0.3 * radius);
            var deadEndEnd = w[0].Destination(bearing, 0.9 * radius);
            return Exact([w[0], w[1], deadEndStart.Value, deadEndEnd, deadEndStart.Value, w[3], w[4], w[5]]);
        });

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 90, 60);

        Assert.Equal(2, result.Requests);
        Assert.True(result.Attempts[0].Problems.HasFlag(LoopProblems.Spurs), result.Attempts[0].Problems.ToString());
        var second = engine.Requests[1];
        Assert.True(second[2].DistanceKmTo(deadEndStart!.Value) < 0.001, $"moved to {second[2]}, dead end starts at {deadEndStart}");
        Assert.Equal(engine.Requests[0].Where((_, i) => i != 2), second.Where((_, i) => i != 2));
        Assert.True(result.Best!.IsValid, result.Best.Problems.ToString());
    }

    [Fact]
    public async Task Moves_the_waypoints_of_a_real_star_onto_the_route_where_their_spurs_leave_it()
    {
        // The recorded star from León towards the east (radius 6 km) answers the first request, whatever it is.
        var recorded = RecordedRoutes.LeonEastRepeats;
        var star = await recorded.LoadAsync();
        // Recorded waypoints have 6 decimals: the same fan within a metre.
        var engine = new FakeRouteEngine(w =>
            w.Count == recorded.Waypoints.Count && w.Zip(recorded.Waypoints).All(p => p.First.DistanceKmTo(p.Second) < 0.001)
                ? star
                : Exact(w));
        var target = 6 * LoopWaypoints.StraightLengthPerRadius() * LoopWaypoints.RoadFactor; // first radius 6 km

        var result = await new LoopGenerator(engine).GenerateAsync(recorded.Waypoints[0], 90, target);

        Assert.True(result.Attempts[0].Problems.HasFlag(LoopProblems.Spurs));
        var moved = engine.Requests[1].Where((p, i) => p != engine.Requests[0][i]).ToList();
        Assert.NotEmpty(moved);
        // Moved onto points of the recorded route, and closer to the start than the fan put them.
        Assert.All(moved, p => Assert.Contains(star.Points, q => q.Position == p));
        Assert.All(moved, p => Assert.True(recorded.Waypoints[0].DistanceKmTo(p) < 6));
    }

    [Fact]
    public async Task Turns_the_bearing_when_the_waypoints_cannot_be_joined()
    {
        var engine = new FakeRouteEngine(w => Compass.AngleBetween(FakeRouteEngine.FanBearing(w), 0) < 1
            ? throw new RouteException(RouteFailure.Unreachable, "target island")
            : Exact(w));

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 0, 60);

        Assert.Equal(2, result.Requests);
        Assert.Equal(20, result.Best!.FanBearingDegrees, precision: 6);
        Assert.True(result.Best.IsValid);
        Assert.InRange(result.Best.BearingDeviationDegrees, 19, 21);
    }

    [Fact]
    public async Task Gives_no_loop_when_no_bearing_can_be_joined()
    {
        var engine = new FakeRouteEngine(_ => throw new RouteException(RouteFailure.Unreachable, "target island"));

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 0, 60);

        Assert.Null(result.Best);
        Assert.Empty(result.Attempts);
        // One per bearing: 0, +20, −20, +40, −40.
        Assert.Equal(
            [0.0, 20, 340, 40, 320],
            engine.Requests.Select(w => Math.Round(FakeRouteEngine.FanBearing(w), 6)));
    }

    [Fact]
    public async Task Stops_at_the_request_limit_and_returns_the_least_bad_loop()
    {
        // Every route is on a motorway, and twice as long as the polygon.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 2, highway: "motorway"));

        var result = await new LoopGenerator(engine, new LoopGeneratorOptions { MaxRequests = 3 }).GenerateAsync(Leon, 90, 60);

        Assert.Equal(3, result.Requests);
        Assert.Equal(3, engine.Requests.Count);
        Assert.False(result.Best!.IsValid);
        Assert.Equal(LoopProblems.Forbidden, result.Best.Problems);
    }

    [Fact]
    public async Task Lets_engine_failures_other_than_unreachable_waypoints_through()
    {
        var engine = new FakeRouteEngine(_ => throw new RouteException(RouteFailure.Service, "HTTP 503"));

        var ex = await Assert.ThrowsAsync<RouteException>(() => new LoopGenerator(engine).GenerateAsync(Leon, 0, 60));

        Assert.Equal(RouteFailure.Service, ex.Failure);
    }

    [Theory]
    [InlineData(3, 0, new double[] { 0, 120, 240 })]
    [InlineData(3, 300, new double[] { 300, 60, 180 })]
    [InlineData(4, -45, new double[] { 315, 45, 135, 225 })]
    [InlineData(1, 90, new double[] { 90 })]
    public void Spreads_bearings_evenly_around_the_compass(int count, double first, double[] expected) =>
        Assert.Equal(expected, LoopGenerator.SpreadBearings(count, first).Select(b => Math.Round(b, 9)));

    [Fact]
    public void Rejects_bad_bearing_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopGenerator.SpreadBearings(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LoopGenerator.SpreadBearings(37));
    }

    [Fact]
    public async Task Ranks_several_bearings_valid_first_and_closest_to_the_target_first()
    {
        // North: no loop in any direction near it. South-east: 5 % long. South-west: exact.
        var engine = new FakeRouteEngine(w =>
        {
            var fan = FakeRouteEngine.FanBearing(w);
            if (Compass.AngleBetween(fan, 0) < 45)
            {
                throw new RouteException(RouteFailure.Unreachable, "target island");
            }
            return FakeRouteEngine.Straight(w, roadFactor: Compass.AngleBetween(fan, 120) < 1 ? 1.5 * 1.05 : 1.5);
        });

        var results = await new LoopGenerator(engine).GenerateManyAsync(Leon, LoopGenerator.SpreadBearings(3), 60);

        Assert.Equal([240.0, 120, 0], results.Select(r => r.BearingDegrees));
        Assert.True(results[0].Found);
        Assert.Equal(0, results[0].Best!.DistanceError, precision: 3);
        Assert.True(results[1].Found);
        Assert.Equal(0.05, results[1].Best!.DistanceError, precision: 3);
        // Towards the north there is no loop, not even turning ±40°.
        Assert.False(results[2].Found);
        Assert.Null(results[2].Best);
        Assert.Equal(5, results[2].Requests);
    }

    [Fact]
    public async Task Among_valid_loops_prefers_fewer_trunk_km()
    {
        // Both loops exact; the one towards 120° runs all on a trunk road.
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(
            w, highway: Compass.AngleBetween(FakeRouteEngine.FanBearing(w), 120) < 1 ? "trunk" : "tertiary"));

        var results = await new LoopGenerator(engine).GenerateManyAsync(Leon, [120, 240], 60);

        Assert.Equal([240.0, 120], results.Select(r => r.BearingDegrees));
        Assert.All(results, r => Assert.True(r.Found));
        Assert.Equal(60, results[1].Best!.Analysis.TrunkKm, precision: 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(60, double.NaN)]
    public async Task Rejects_bad_targets_and_bearings(double targetKm, double bearing)
    {
        var engine = new FakeRouteEngine(Exact);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new LoopGenerator(engine).GenerateAsync(Leon, bearing, targetKm));
    }
}
