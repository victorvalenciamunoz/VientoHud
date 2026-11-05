using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>
/// Where the ride should head, drawn on the map (phase 7d): a sector from the start around the recommended bearing,
/// as wide as the bearings the planner looks in (±<see cref="RideBearings.DefaultSpreadDegrees"/>) and as far as the far
/// point of an out and back of the target distance (DECISIONS 040: half the target over the road factor).
/// </summary>
public static class DirectionFan
{
    /// <summary>Points along the arc: enough for a smooth edge at any zoom.</summary>
    public const int ArcPoints = 24;

    /// <summary>How far from the start a ride of <paramref name="targetKm"/> gets, as the crow flies.</summary>
    public static double ReachKm(double targetKm) => targetKm / 2 / new LoopGeneratorOptions().OutAndBackRoadFactor;

    /// <summary>
    /// The sector as a closed ring (first and last point the start), or null when the wind gives no direction
    /// (calm or variable).
    /// </summary>
    public static IReadOnlyList<Coordinate>? For(
        StartDirectionAdvice wind, Coordinate start, double targetKm, double spreadDegrees = RideBearings.DefaultSpreadDegrees)
    {
        ArgumentNullException.ThrowIfNull(wind);
        if (wind.StartBearingDegrees is not { } bearing)
        {
            return null;
        }
        var reach = ReachKm(targetKm);
        var ring = new List<Coordinate>(ArcPoints + 3) { start };
        for (var i = 0; i <= ArcPoints; i++)
        {
            var towards = (bearing - spreadDegrees + 2 * spreadDegrees * i / ArcPoints + 360) % 360;
            ring.Add(start.Destination(towards, reach));
        }
        ring.Add(start);
        return ring;
    }
}
