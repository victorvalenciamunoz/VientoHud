namespace VientoHud.Core.Routing;

/// <summary>A stretch of about 1 km of a route, with where it goes and when the rider rides it.</summary>
/// <param name="Index">Position in riding order, from 0.</param>
/// <param name="StartKm">Distance along the track from the start of the route to the start of the leg.</param>
/// <param name="LengthKm">Length of the leg along the track.</param>
/// <param name="Start">Where the leg begins.</param>
/// <param name="End">Where the leg ends.</param>
/// <param name="Middle">The point halfway along the leg, on the track.</param>
/// <param name="BearingDegrees">
/// Route bearing: degrees TOWARDS where the rider goes, clockwise from north, in [0, 360), of the straight line from
/// <paramref name="Start"/> to <paramref name="End"/>. Not to be confused with wind directions, which are FROM.
/// The along-wind effect of a steady wind on the bends inside the leg adds up to its effect on that straight line.
/// </param>
/// <param name="Straightness">
/// Straight-line distance from <paramref name="Start"/> to <paramref name="End"/> divided by the length, in [0, 1]:
/// 1 for a straight leg, near 0 for one that turns back on itself (then the bearing means little).
/// </param>
/// <param name="StartTime">When the rider reaches the start of the leg, in UTC.</param>
/// <param name="EndTime">When the rider reaches the end of the leg, in UTC.</param>
public sealed record RouteLeg(
    int Index,
    double StartKm,
    double LengthKm,
    Coordinate Start,
    Coordinate End,
    Coordinate Middle,
    double BearingDegrees,
    double Straightness,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime)
{
    public double EndKm => StartKm + LengthKm;

    /// <summary>When the rider is halfway along the leg.</summary>
    public DateTimeOffset MiddleTime => StartTime + (EndTime - StartTime) / 2;
}

/// <summary>Cuts a route into legs of about the same length and times them from the departure.</summary>
public static class RouteLegs
{
    /// <summary>Length of a leg when none is given.</summary>
    public const double DefaultLegKm = 1;

    /// <summary>
    /// The route cut into equal legs of about <paramref name="legKm"/> (as many as fit, rounded; at least one), timed
    /// from <paramref name="departure"/> at a steady <paramref name="averageSpeedKmh"/>. Distances are measured along
    /// the track, so they can differ slightly from the engine's <see cref="Route.DistanceKm"/>.
    /// </summary>
    public static IReadOnlyList<RouteLeg> Split(
        Route route,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double legKm = DefaultLegKm)
    {
        if (departure.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Departure must be in UTC.", nameof(departure));
        }
        if (!double.IsFinite(averageSpeedKmh) || averageSpeedKmh is < 5 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(averageSpeedKmh), averageSpeedKmh, "Average speed must be 5-60 km/h.");
        }
        if (!double.IsFinite(legKm) || legKm is < 0.1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(legKm), legKm, "Leg length must be 0.1-10 km.");
        }

        var points = route.Points.Select(p => p.Position).ToArray();
        var alongKm = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            alongKm[i] = alongKm[i - 1] + points[i - 1].DistanceKmTo(points[i]);
        }
        var totalKm = alongKm[^1];
        if (totalKm == 0)
        {
            throw new ArgumentException("The route has no length.", nameof(route));
        }

        var count = Math.Max(1, (int)Math.Round(totalKm / legKm));
        var length = totalKm / count;
        var legs = new RouteLeg[count];
        var start = points[0];
        for (var k = 0; k < count; k++)
        {
            var startKm = k * length;
            var end = k == count - 1 ? points[^1] : PointAt(points, alongKm, (k + 1) * length);
            legs[k] = new RouteLeg(
                k,
                startKm,
                length,
                start,
                end,
                PointAt(points, alongKm, startKm + length / 2),
                start.BearingTo(end),
                Math.Min(1, start.DistanceKmTo(end) / length),
                departure + TimeSpan.FromHours(startKm / averageSpeedKmh),
                departure + TimeSpan.FromHours((startKm + length) / averageSpeedKmh));
            start = end;
        }
        return legs;
    }

    // The point at km along the track, on the great circle between the two track points around it.
    private static Coordinate PointAt(Coordinate[] points, double[] alongKm, double km)
    {
        var i = Array.BinarySearch(alongKm, km);
        if (i >= 0)
        {
            return points[i];
        }
        i = ~i;
        var from = points[i - 1];
        return from.Destination(from.BearingTo(points[i]), km - alongKm[i - 1]);
    }
}
