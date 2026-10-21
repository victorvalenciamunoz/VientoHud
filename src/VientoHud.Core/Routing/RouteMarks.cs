namespace VientoHud.Core.Routing;

/// <summary>A mark on the track that shows which way the ride goes.</summary>
/// <param name="Position">Where it stands, on the track.</param>
/// <param name="BearingDegrees">Degrees TOWARDS where the rider goes there, clockwise from north.</param>
/// <param name="AtKm">Distance along the route.</param>
public readonly record struct RouteMark(Coordinate Position, double BearingDegrees, double AtKm);

/// <summary>
/// Marks along a route that say which way it is ridden (the wind arrows say where the wind goes, not the rider: the
/// author read them as the way round, 2026-10-21). One every few km, halfway between the wind arrows' km.
/// </summary>
public static class RouteMarks
{
    public const double DefaultEveryKm = 2.5;

    /// <summary>One mark every <paramref name="everyKm"/> (at km 1.25, 3.75…), heading along the track there.</summary>
    public static IReadOnlyList<RouteMark> Along(Route route, double everyKm = DefaultEveryKm)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (everyKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(everyKm), everyKm, "The spacing must be positive.");
        }
        var marks = new List<RouteMark>();
        var points = route.Points;
        var next = everyKm / 2;
        var soFar = 0.0;
        for (var i = 0; i < points.Count - 1; i++)
        {
            var from = points[i].Position;
            var to = points[i + 1].Position;
            var length = from.DistanceKmTo(to);
            while (length > 0 && next <= soFar + length)
            {
                var bearing = from.BearingTo(to);
                marks.Add(new RouteMark(from.Destination(bearing, next - soFar), bearing, next));
                next += everyKm;
            }
            soFar += length;
        }
        return marks;
    }
}
