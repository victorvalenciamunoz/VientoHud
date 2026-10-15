using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Support;

/// <summary>A route engine that answers with whatever <c>route</c> builds from the waypoints, and remembers the requests.</summary>
internal sealed class FakeRouteEngine(Func<IReadOnlyList<Coordinate>, Route> route) : IRouteEngine
{
    public List<IReadOnlyList<Coordinate>> Requests { get; } = [];

    public Task<Route> RouteAsync(IReadOnlyList<Coordinate> waypoints, CancellationToken cancellationToken = default)
    {
        Requests.Add(waypoints);
        return Task.FromResult(route(waypoints));
    }

    /// <summary>
    /// A route straight through <paramref name="points"/>, whose length is the straight length times
    /// <paramref name="roadFactor"/>, all on one paved road (or on <paramref name="highway"/>).
    /// </summary>
    public static Route Straight(IReadOnlyList<Coordinate> points, double roadFactor = 1.5, string highway = "tertiary")
    {
        var km = points.Zip(points.Skip(1), (a, b) => a.DistanceKmTo(b)).Sum() * roadFactor;
        return new Route(
            points.Select(p => new RoutePoint(p)).ToList(),
            [new RouteSegment(0, km, new Dictionary<string, string> { ["highway"] = highway, ["surface"] = "asphalt" })],
            km,
            null,
            "fake");
    }

    /// <summary>The bearing a fan of waypoints is centred on: halfway between its two middle points.</summary>
    public static double FanBearing(IReadOnlyList<Coordinate> waypoints)
    {
        var start = waypoints[0];
        var a = start.BearingTo(waypoints[2]);
        var b = start.BearingTo(waypoints[3]);
        var half = ((b - a + 540) % 360 - 180) / 2;
        return (a + half + 360) % 360;
    }
}
