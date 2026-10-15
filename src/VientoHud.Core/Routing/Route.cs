namespace VientoHud.Core.Routing;

/// <summary>One point of a routed track.</summary>
/// <param name="Position">WGS84 position.</param>
/// <param name="ElevationMeters">Elevation above sea level in metres, if the engine knows it.</param>
public readonly record struct RoutePoint(Coordinate Position, double? ElevationMeters = null);

/// <summary>
/// A stretch of the route along ways with the same OpenStreetMap tags, e.g. "highway=tertiary surface=asphalt".
/// </summary>
/// <param name="StartKm">Distance from the start of the route where the stretch begins.</param>
/// <param name="LengthKm">Length of the stretch.</param>
/// <param name="WayTags">
/// OSM tags of the way(s). Engines only report the tags they use, so a missing tag (e.g. no "surface") means
/// "not reported", not "the way has none".
/// </param>
public sealed record RouteSegment(double StartKm, double LengthKm, IReadOnlyDictionary<string, string> WayTags)
{
    public double StartKm { get; } = StartKm >= 0
        ? StartKm
        : throw new ArgumentOutOfRangeException(nameof(StartKm), StartKm, "Start cannot be negative.");

    public double LengthKm { get; } = LengthKm >= 0
        ? LengthKm
        : throw new ArgumentOutOfRangeException(nameof(LengthKm), LengthKm, "Length cannot be negative.");

    /// <summary>OSM road type ("highway" tag), e.g. "tertiary", "track"; null if not reported.</summary>
    public string? Highway => WayTags.GetValueOrDefault("highway");

    /// <summary>OSM surface ("surface" tag), e.g. "asphalt", "gravel"; null if not reported.</summary>
    public string? Surface => WayTags.GetValueOrDefault("surface");
}

/// <summary>A route through a list of waypoints along real roads.</summary>
/// <param name="Points">The track, in riding order.</param>
/// <param name="Segments">Consecutive stretches covering the whole route, in riding order.</param>
/// <param name="DistanceKm">Length of the route.</param>
/// <param name="AscentMeters">Total climb, as the engine estimates it (smoothed), if known.</param>
/// <param name="Attribution">Credit line the routing service and map data require.</param>
public sealed record Route(
    IReadOnlyList<RoutePoint> Points,
    IReadOnlyList<RouteSegment> Segments,
    double DistanceKm,
    double? AscentMeters,
    string Attribution)
{
    public IReadOnlyList<RoutePoint> Points { get; } = Points is { Count: >= 2 }
        ? Points
        : throw new ArgumentException("A route needs at least two points.", nameof(Points));

    /// <summary>
    /// The same route ridden the other way round: points and stretches in reverse order. The climb stays for a loop
    /// (it climbs as much as it drops); for a route that ends elsewhere it is unknown.
    /// </summary>
    public Route Reversed() => new(
        Points.Reverse().ToList(),
        Segments
            .Reverse()
            .Select(s => new RouteSegment(Math.Max(0, DistanceKm - s.StartKm - s.LengthKm), s.LengthKm, s.WayTags))
            .ToList(),
        DistanceKm,
        Points[0].Position.DistanceKmTo(Points[^1].Position) < 0.5 ? AscentMeters : null,
        Attribution);
}
