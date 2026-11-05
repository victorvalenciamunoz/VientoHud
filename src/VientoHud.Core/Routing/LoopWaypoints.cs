namespace VientoHud.Core.Routing;

/// <summary>
/// Where to put the waypoints of a loop before routing it (DECISIONS 013): a fan of points at the same distance from
/// the start, centred on the bearing the loop should head to.
/// </summary>
public static class LoopWaypoints
{
    /// <summary>The start plus four fan points, as BRouter's own round trips use.</summary>
    public const int DefaultPoints = 5;

    /// <summary>
    /// How much longer the route along roads is than the straight polygon through the waypoints. With 1.5, a five-point
    /// fan gives R = target / 5.8; valid loops in the live test of 2026-10-15 came out at 4.5-6 times R. The generator
    /// still has to correct R, because the length jumps a lot with it.
    /// </summary>
    public const double RoadFactor = 1.5;

    /// <summary>
    /// The loop: the start, <paramref name="points"/> − 1 waypoints <paramref name="radiusKm"/> from it and the start
    /// again. The waypoints are spread over 180° centred on <paramref name="bearingDegrees"/> (degrees TOWARDS,
    /// clockwise from north): the i-th at <c>bearing − (90 − 180·i/points)</c>. The loop sets out to the left of the
    /// bearing and comes back from the right, clockwise on a map; riding it the other way round is reversing the list.
    /// </summary>
    public static IReadOnlyList<Coordinate> Fan(
        Coordinate start, double bearingDegrees, double radiusKm, int points = DefaultPoints)
    {
        if (!double.IsFinite(bearingDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(bearingDegrees), bearingDegrees, "Bearing must be finite.");
        }
        if (!double.IsFinite(radiusKm) || radiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), radiusKm, "Radius must be positive.");
        }
        ValidatePoints(points);

        var waypoints = new List<Coordinate>(points + 1) { start };
        for (var i = 1; i < points; i++)
        {
            waypoints.Add(start.Destination(bearingDegrees - (90 - 180.0 * i / points), radiusKm));
        }
        waypoints.Add(start);
        return waypoints;
    }

    /// <summary>A first radius for a loop of <paramref name="targetKm"/>: its straight polygon times <see cref="RoadFactor"/>.</summary>
    public static double InitialRadiusKm(double targetKm, int points = DefaultPoints)
    {
        if (!double.IsFinite(targetKm) || targetKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetKm), targetKm, "Target distance must be positive.");
        }
        ValidatePoints(points);
        return targetKm / (StraightLengthPerRadius(points) * RoadFactor);
    }

    /// <summary>
    /// Length of the fan's straight polygon per km of radius: out and back (2) plus the chords between consecutive
    /// waypoints, 180/points degrees apart (3.85 for five points).
    /// </summary>
    public static double StraightLengthPerRadius(int points = DefaultPoints)
    {
        ValidatePoints(points);
        return 2 + (points - 2) * 2 * Math.Sin(Math.PI / 2 / points);
    }

    private static void ValidatePoints(int points)
    {
        if (points is < 3 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(points), points, "A fan needs 3 to 12 points (start included).");
        }
    }
}
