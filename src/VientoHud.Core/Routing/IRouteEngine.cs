namespace VientoHud.Core.Routing;

/// <summary>Finds a road-bike route through waypoints along real roads (see DECISIONS 003 and 013).</summary>
public interface IRouteEngine
{
    /// <summary>
    /// Routes through <paramref name="waypoints"/> in order. For a loop, repeat the start as the last waypoint.
    /// Engines move each waypoint to the nearest road they are allowed to use, possibly far away: callers that care
    /// must check how close the route passes to each waypoint.
    /// </summary>
    /// <exception cref="RouteException">No route could be found, or the engine failed.</exception>
    Task<Route> RouteAsync(IReadOnlyList<Coordinate> waypoints, CancellationToken cancellationToken = default);
}

/// <summary>Why a route could not be obtained.</summary>
public enum RouteFailure
{
    /// <summary>
    /// Some waypoint cannot be connected to the rest with allowed roads (e.g. it lands on an isolated stretch).
    /// Moving the waypoints may help.
    /// </summary>
    Unreachable,

    /// <summary>The engine or the network failed; retrying the same request later may help.</summary>
    Service,
}

/// <summary>A route could not be obtained.</summary>
public sealed class RouteException(RouteFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public RouteFailure Failure { get; } = failure;
}
