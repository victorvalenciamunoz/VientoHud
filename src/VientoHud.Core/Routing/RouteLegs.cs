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
/// <param name="ClimbMeters">
/// Elevation at the end minus elevation at the start (negative downhill); null when the route has no elevations.
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
    double? ClimbMeters,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime)
{
    public double EndKm => StartKm + LengthKm;

    /// <summary>Average slope in percent (positive uphill); 0 when the elevation is unknown.</summary>
    public double GradePercent => ClimbMeters is { } climb && LengthKm > 0 ? climb / (LengthKm * 10) : 0;

    /// <summary>When the rider is halfway along the leg.</summary>
    public DateTimeOffset MiddleTime => StartTime + (EndTime - StartTime) / 2;
}

/// <summary>Cuts a route into legs of about the same length and times them from the departure.</summary>
public static class RouteLegs
{
    /// <summary>Length of a leg when none is given.</summary>
    public const double DefaultLegKm = 1;

    /// <summary>
    /// How much slower the rider goes per 1 % of slope, as a fraction of the speed on the flat (faster downhill).
    /// Measured on the author's rides: 3.9 km/h per 1 % at about 30 km/h (DECISIONS 021).
    /// </summary>
    public const double SlowdownPerGradePercent = 0.13;

    /// <summary>Slopes beyond this (in percent, up or down) count as this much: the measurement went no further.</summary>
    public const double MaxGradePercent = 4;

    /// <summary>
    /// The route cut into equal legs of about <paramref name="legKm"/> (as many as fit, rounded; at least one), timed
    /// from <paramref name="departure"/>. The whole route takes its length divided by <paramref name="averageSpeedKmh"/>;
    /// when the route has elevations, the time is shared out by slope: slower uphill, faster downhill
    /// (<see cref="SlowdownPerGradePercent"/>). Distances are measured along the track, so they can differ slightly
    /// from the engine's <see cref="Route.DistanceKm"/>.
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
        var elevations = route.Points.All(p => p.ElevationMeters is not null)
            ? route.Points.Select(p => p.ElevationMeters!.Value).ToArray()
            : null;
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
        double? ElevationAt(double km) => elevations is null ? null : ValueAt(elevations, alongKm, km);
        var climbs = Enumerable.Range(0, count)
            .Select(k => ElevationAt((k + 1) * length) - ElevationAt(k * length))
            .ToArray();

        // Time of each leg relative to the flat: the slowdown factors are scaled so that the total stays at
        // totalKm / averageSpeedKmh.
        var relative = climbs
            .Select(climb => 1 / (1 - SlowdownPerGradePercent * Math.Clamp((climb ?? 0) / (length * 10), -MaxGradePercent, MaxGradePercent)))
            .ToArray();
        var hoursPerUnit = totalKm / averageSpeedKmh / relative.Sum();

        var legs = new RouteLeg[count];
        var start = points[0];
        var startTime = departure;
        for (var k = 0; k < count; k++)
        {
            var startKm = k * length;
            var end = k == count - 1 ? points[^1] : PointAt(points, alongKm, (k + 1) * length);
            var endTime = k == count - 1
                ? departure + TimeSpan.FromHours(totalKm / averageSpeedKmh)
                : startTime + TimeSpan.FromHours(relative[k] * hoursPerUnit);
            legs[k] = new RouteLeg(
                k,
                startKm,
                length,
                start,
                end,
                PointAt(points, alongKm, startKm + length / 2),
                start.BearingTo(end),
                Math.Min(1, start.DistanceKmTo(end) / length),
                climbs[k],
                startTime,
                endTime);
            start = end;
            startTime = endTime;
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

    // A value given at each track point (e.g. elevation), interpolated at km along the track.
    private static double ValueAt(double[] values, double[] alongKm, double km)
    {
        var i = Array.BinarySearch(alongKm, km);
        if (i >= 0)
        {
            return values[i];
        }
        i = ~i;
        if (i >= values.Length)
        {
            return values[^1];
        }
        var span = alongKm[i] - alongKm[i - 1];
        return span == 0 ? values[i] : values[i - 1] + (km - alongKm[i - 1]) / span * (values[i] - values[i - 1]);
    }
}
