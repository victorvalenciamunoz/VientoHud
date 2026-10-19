using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class OutAndBackTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    // A paved road straight between the waypoints, with a point every 100 m, `roadFactor` times longer than the straight
    // line (as if it wound), and the elevation given at each end.
    private static Route Road(IReadOnlyList<Coordinate> waypoints, double roadFactor = 1.3, double? fromElevation = null,
        double? toElevation = null, double? ascent = null)
    {
        var points = new List<RoutePoint>();
        foreach (var (from, to) in waypoints.Zip(waypoints.Skip(1)))
        {
            var km = from.DistanceKmTo(to);
            var steps = Math.Max(1, (int)Math.Round(km / 0.1));
            for (var s = points.Count == 0 ? 0 : 1; s <= steps; s++)
            {
                var share = (double)s / steps;
                double? elevation = fromElevation is { } a && toElevation is { } b ? a + (b - a) * share : null;
                points.Add(new RoutePoint(from.Destination(from.BearingTo(to), km * share), elevation));
            }
        }
        var length = waypoints.Zip(waypoints.Skip(1), (a, b) => a.DistanceKmTo(b)).Sum() * roadFactor;
        return new Route(points,
            [new RouteSegment(0, length, new Dictionary<string, string> { ["highway"] = "tertiary", ["surface"] = "asphalt" })],
            length, ascent, "fake");
    }

    [Fact]
    public void There_and_back_rides_the_same_road_both_ways()
    {
        var there = Road([Leon, Leon.Destination(30, 10)], fromElevation: 800, toElevation: 830, ascent: 50);

        var route = there.ThereAndBack();

        Assert.Equal(2 * there.DistanceKm, route.DistanceKm, 9);
        Assert.Equal(2 * there.Points.Count - 1, route.Points.Count);
        Assert.Equal(there.Points[0], route.Points[^1]);
        Assert.Equal(there.Points[^1], route.Points[there.Points.Count - 1]);
        Assert.Equal([0, there.DistanceKm], route.Segments.Select(s => s.StartKm));
        Assert.Equal(route.DistanceKm, route.Segments.Sum(s => s.LengthKm), 9);
        // 50 m up on the way there with a rise of 30 m: 20 m up on the way back.
        Assert.Equal(70, route.AscentMeters);
        Assert.Null(Road([Leon, Leon.Destination(30, 10)], ascent: 50).ThereAndBack().AscentMeters);
    }

    [Fact]
    public async Task Generates_an_out_and_back_on_the_bearing_from_one_way_route()
    {
        var engine = new FakeRouteEngine(w => Road(w));
        var generator = new LoopGenerator(engine);

        var result = await generator.GenerateAsync(Leon, 60, 60, RouteShape.OutAndBack);

        Assert.True(result.Found);
        Assert.Equal(RouteShape.OutAndBack, result.Shape);
        var best = result.Best!;
        Assert.Equal(RouteShape.OutAndBack, best.Shape);
        // One way, from the start to a point 60 / 2 / 1.3 ≈ 23 km away towards 60°.
        var request = Assert.Single(engine.Requests);
        Assert.Equal(2, request.Count);
        Assert.Equal(23.1, Leon.DistanceKmTo(request[1]), 1);
        Assert.Equal(60, Leon.BearingTo(request[1]), 1);
        Assert.Equal(60, best.Route.DistanceKm, 1);
        Assert.True(best.Route.Points[^1].Position.DistanceKmTo(Leon) < 0.01);
        Assert.Equal([Leon, request[1]], best.Waypoints);
        // All ridden twice, but from home: not a spur, and the turn at the far point is the destination.
        Assert.True(best.Analysis.RepeatedFraction > 0.9);
        Assert.Equal(LoopProblems.None, best.Problems);
    }

    [Fact]
    public async Task Moves_the_far_point_until_the_distance_fits()
    {
        // Roads twice as long as the straight line: the first guess (for 1.3) gives 92 km for 60.
        var engine = new FakeRouteEngine(w => Road(w, roadFactor: 2));

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 200, 60, RouteShape.OutAndBack);

        Assert.True(result.Found);
        Assert.True(engine.Requests.Count >= 2);
        Assert.InRange(result.Best!.Route.DistanceKm, 54, 66);
        Assert.True(Leon.DistanceKmTo(engine.Requests[^1][1]) < Leon.DistanceKmTo(engine.Requests[0][1]));
    }

    [Fact]
    public async Task Turns_the_bearing_when_the_far_point_cannot_be_reached()
    {
        // Nothing to the east (60°-80°): the sea, say.
        var engine = new FakeRouteEngine(w =>
        {
            var bearing = Leon.BearingTo(w[1]);
            return bearing is > 55 and < 85
                ? throw new RouteException(RouteFailure.Unreachable, "Target island")
                : Road(w);
        });

        var result = await new LoopGenerator(engine).GenerateAsync(Leon, 70, 60, RouteShape.OutAndBack);

        Assert.True(result.Found);
        Assert.Equal(70, result.BearingDegrees);
        // Turns are tried 0, +10, -10, +20…: 70, 80 and 60 are blocked, 90 is the first free one.
        Assert.Equal(90, result.Best!.FanBearingDegrees, 6);
    }

    [Fact]
    public async Task One_out_and_back_per_bearing()
    {
        var engine = new FakeRouteEngine(w => Road(w));

        var results = await new LoopGenerator(engine).GenerateManyAsync(Leon, [0, 120, 240], 40, RouteShape.OutAndBack);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(RouteShape.OutAndBack, r.Shape));
        Assert.All(results, r => Assert.True(r.Found));
        Assert.All(engine.Requests, r => Assert.Equal(2, r.Count));
    }
}
