namespace VientoHud.Core.Routing;

/// <summary>A position on a local flat map, in km east (X) and north (Y) of a reference point.</summary>
internal readonly record struct PlanePoint(double X, double Y)
{
    public double DistanceTo(PlanePoint other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));

    public static PlanePoint Lerp(PlanePoint a, PlanePoint b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Distance to the straight segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public double DistanceToSegment(PlanePoint a, PlanePoint b) => ClosestOnSegment(a, b).DistanceKm;

    /// <summary>
    /// Distance to the straight segment from <paramref name="a"/> to <paramref name="b"/>, and where its closest point
    /// is, as a fraction of the way from a (0) to b (1).
    /// </summary>
    public (double DistanceKm, double Fraction) ClosestOnSegment(PlanePoint a, PlanePoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return (DistanceTo(a), 0);
        }
        var t = Math.Clamp(((X - a.X) * dx + (Y - a.Y) * dy) / lengthSquared, 0, 1);
        return (DistanceTo(Lerp(a, b, t)), t);
    }
}

/// <summary>
/// Equirectangular projection around a reference point. East-west distances drift with the latitude difference
/// (about 1.5 % 100 km north or south of the reference at 42°, more nearer the poles): negligible for the
/// tens-of-metres checks it is used for. Handles routes across the 180° meridian.
/// </summary>
internal sealed class LocalProjection(Coordinate origin)
{
    private const double KmPerDegree = 6371.0088 * Math.PI / 180;
    private readonly double _cosLatitude = Math.Cos(origin.Latitude * Math.PI / 180);

    public PlanePoint Project(Coordinate c)
    {
        var dLon = c.Longitude - origin.Longitude;
        dLon -= 360 * Math.Round(dLon / 360);
        return new PlanePoint(dLon * _cosLatitude * KmPerDegree, (c.Latitude - origin.Latitude) * KmPerDegree);
    }
}

/// <summary>A track projected on a local map, with the distance along it to each point.</summary>
internal sealed class ProjectedTrack
{
    public ProjectedTrack(IReadOnlyList<Coordinate> points)
    {
        Projection = new LocalProjection(points[0]);
        Points = points.Select(Projection.Project).ToArray();
        AlongKm = new double[Points.Count];
        for (var i = 1; i < Points.Count; i++)
        {
            AlongKm[i] = AlongKm[i - 1] + Points[i - 1].DistanceTo(Points[i]);
        }
    }

    public LocalProjection Projection { get; }

    public IReadOnlyList<PlanePoint> Points { get; }

    /// <summary>Distance along the track from its first point to each point, in km.</summary>
    public double[] AlongKm { get; }

    public double LengthKm => AlongKm[^1];

    /// <summary>Shortest distance from <paramref name="p"/> to any part of the track.</summary>
    public double DistanceTo(PlanePoint p)
    {
        var best = double.PositiveInfinity;
        for (var i = 1; i < Points.Count; i++)
        {
            best = Math.Min(best, p.DistanceToSegment(Points[i - 1], Points[i]));
        }
        return best;
    }

    /// <summary>The point at <paramref name="alongKm"/> from the start, clamped to the track.</summary>
    public PlanePoint At(double alongKm)
    {
        var i = Array.BinarySearch(AlongKm, alongKm);
        if (i >= 0)
        {
            return Points[i];
        }
        i = ~i;
        if (i == 0)
        {
            return Points[0];
        }
        if (i == Points.Count)
        {
            return Points[^1];
        }
        return Lerp(i - 1, alongKm);
    }

    /// <summary>
    /// The closest point to <paramref name="p"/> among the parts of the track more than <paramref name="gapKm"/> away
    /// from <paramref name="alongKm"/> when measured along the track: how far it is and where it is along the track.
    /// On a <paramref name="closed"/> track (a loop that ends where it starts) the separation is measured around the
    /// loop, so the end and the start are next to each other. Infinite distance if there are no such parts.
    /// </summary>
    public (double DistanceKm, double AlongKm) ClosestAwayFrom(PlanePoint p, double alongKm, double gapKm, bool closed)
    {
        // On a closed track, the parts just before the end are close to a point just after the start, and vice versa.
        var before = ClosestWithin(p, closed ? Math.Max(0, alongKm + gapKm - LengthKm) : 0, alongKm - gapKm);
        var after = ClosestWithin(p, alongKm + gapKm, closed ? Math.Min(LengthKm, alongKm - gapKm + LengthKm) : LengthKm);
        return before.DistanceKm <= after.DistanceKm ? before : after;
    }

    /// <summary>The closest point to <paramref name="p"/> on the track between two distances along it.</summary>
    private (double DistanceKm, double AlongKm) ClosestWithin(PlanePoint p, double fromKm, double toKm)
    {
        var best = (DistanceKm: double.PositiveInfinity, AlongKm: double.NaN);
        for (var i = 1; i < Points.Count; i++)
        {
            double start = AlongKm[i - 1], end = AlongKm[i];
            if (end <= fromKm || start >= toKm)
            {
                continue;
            }
            var a = start >= fromKm ? start : fromKm;
            var b = end <= toKm ? end : toKm;
            var (distance, t) = p.ClosestOnSegment(At(a, i - 1), At(b, i - 1));
            if (distance < best.DistanceKm)
            {
                best = (distance, a + t * (b - a));
            }
        }
        return best;
    }

    // Point at alongKm, known to be on the segment that starts at point i.
    private PlanePoint At(double alongKm, int i) =>
        alongKm == AlongKm[i] ? Points[i] : alongKm == AlongKm[i + 1] ? Points[i + 1] : Lerp(i, alongKm);

    // Point at alongKm on the segment that starts at point i.
    private PlanePoint Lerp(int i, double alongKm)
    {
        var length = AlongKm[i + 1] - AlongKm[i];
        return length == 0 ? Points[i] : PlanePoint.Lerp(Points[i], Points[i + 1], (alongKm - AlongKm[i]) / length);
    }
}
