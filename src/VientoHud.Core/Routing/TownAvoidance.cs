using VientoHud.Core.Towns;

namespace VientoHud.Core.Routing;

/// <summary>Settings for <see cref="TownAvoidance"/>.</summary>
public sealed record TownAvoidanceOptions
{
    /// <summary>A waypoint is moved at most this far; one deeper in a town stays where it is.</summary>
    public double MaxMoveKm { get; init; } = 4;

    /// <summary>Distance between the rings of places tried around a waypoint.</summary>
    public double StepKm { get; init; } = 0.25;

    /// <summary>
    /// A new place must have no town within this distance, so the route engine, which snaps waypoints to the nearest
    /// road, does not snap it to a street at the edge of the town.
    /// </summary>
    public double ClearanceKm { get; init; } = 0.3;
}

/// <summary>
/// Moves waypoints out of towns before routing (phase 6, step 6e-2; DECISIONS 045): a waypoint in a town makes the route
/// go into it. The start (home) never moves.
/// </summary>
public static class TownAvoidance
{
    // Directions tried on each ring.
    private const int Directions = 16;

    /// <summary>
    /// The waypoints with each one in a town, other than the first and last (the start), moved to the nearest place
    /// clear of towns: on the first ring around it that has one, the place whose distance from the start is closest to
    /// the waypoint's, so the route keeps its size.
    /// </summary>
    public static IReadOnlyList<Coordinate> MoveOut(
        IReadOnlyList<Coordinate> waypoints, TownMap towns, TownAvoidanceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(towns);
        options ??= new TownAvoidanceOptions();
        if (waypoints.Count < 2)
        {
            return waypoints;
        }

        var start = waypoints[0];
        var last = waypoints[^1] == start ? waypoints.Count - 1 : waypoints.Count;
        var moved = waypoints.ToList();
        for (var i = 1; i < last; i++)
        {
            if (towns.Contains(waypoints[i]))
            {
                moved[i] = NearestClear(towns, start, waypoints[i], options) ?? waypoints[i];
            }
        }
        return moved;
    }

    private static Coordinate? NearestClear(TownMap towns, Coordinate start, Coordinate waypoint, TownAvoidanceOptions options)
    {
        var wanted = start.DistanceKmTo(waypoint);
        for (var ring = options.StepKm; ring <= options.MaxMoveKm + 1e-9; ring += options.StepKm)
        {
            var clear = Enumerable.Range(0, Directions)
                .Select(d => waypoint.Destination(360.0 * d / Directions, ring))
                .Where(p => !Near(towns, p, options.ClearanceKm))
                .ToList();
            if (clear.Count > 0)
            {
                return clear.MinBy(p => Math.Abs(start.DistanceKmTo(p) - wanted));
            }
        }
        return null;
    }

    // Whether there is town at the place or at 8 places the clearance away around it.
    private static bool Near(TownMap towns, Coordinate place, double clearanceKm) =>
        towns.Contains(place)
        || Enumerable.Range(0, 8).Any(d => towns.Contains(place.Destination(45.0 * d, clearanceKm)));
}
