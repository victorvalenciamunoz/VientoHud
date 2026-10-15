namespace VientoHud.Core.Routing;

/// <summary>Km ridden on one kind of way.</summary>
/// <param name="Highway">OSM "highway" tag, or null if not reported.</param>
/// <param name="Surface">OSM "surface" tag, or null if not reported.</param>
public sealed record RoadUsage(string? Highway, string? Surface, RoadCategory Category, double Km);

/// <summary>How the repetition of a route is measured (DECISIONS 014).</summary>
public sealed record RouteAnalysisOptions
{
    /// <summary>The track is checked at points this far apart.</summary>
    public double SampleSpacingKm { get; init; } = 0.1;

    /// <summary>A checked point is repeated if another part of the track passes this close.</summary>
    public double RepeatToleranceKm { get; init; } = 0.03;

    /// <summary>
    /// The other part must be at least this far away along the track, so that the stretch the point is on does not
    /// count. Also tolerates short out-and-back detours, like turning at a roundabout.
    /// </summary>
    public double MinRepeatSeparationKm { get; init; } = 1;
}

/// <summary>What a routed loop is like: kinds of road, how close it passes to each waypoint and how much it repeats.</summary>
/// <param name="DistanceKm">Length of the route, as the engine measured it.</param>
/// <param name="Roads">Km per road type and surface, longest first.</param>
/// <param name="WaypointOffsetsKm">
/// For each requested waypoint, in order, how close the route passes to it. Engines move waypoints to the nearest
/// allowed road, which may be far away (a point in the sea gave a route to a coast 446 km away).
/// </param>
/// <param name="RepeatedFraction">
/// Share of the route, from 0 to 1, that runs where another part of the route also runs (out and back on the same
/// road). Both passes count.
/// </param>
public sealed record RouteAnalysis(
    double DistanceKm,
    IReadOnlyList<RoadUsage> Roads,
    IReadOnlyList<double> WaypointOffsetsKm,
    double RepeatedFraction)
{
    public double ForbiddenKm => KmOf(RoadCategory.Forbidden);

    public double PavedTrackKm => KmOf(RoadCategory.PavedTrack);

    public double UnknownKm => KmOf(RoadCategory.Unknown);

    public double RepeatedKm => RepeatedFraction * DistanceKm;

    /// <summary>The largest of <see cref="WaypointOffsetsKm"/>.</summary>
    public double MaxWaypointOffsetKm => WaypointOffsetsKm.Count > 0 ? WaypointOffsetsKm.Max() : 0;

    private double KmOf(RoadCategory category) => Roads.Where(r => r.Category == category).Sum(r => r.Km);
}

/// <summary>Measures a routed loop without calling any service.</summary>
public static class RouteAnalyzer
{
    public static RouteAnalysis Analyze(
        Route route,
        IReadOnlyList<Coordinate> waypoints,
        RouteAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(waypoints);
        options ??= new RouteAnalysisOptions();
        if (options.SampleSpacingKm <= 0 || options.RepeatToleranceKm <= 0 || options.MinRepeatSeparationKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Repetition settings must be positive.");
        }

        var track = new ProjectedTrack(route.Points.Select(p => p.Position).ToList());
        return new RouteAnalysis(
            route.DistanceKm,
            Roads(route.Segments),
            waypoints.Select(w => track.DistanceTo(track.Projection.Project(w))).ToList(),
            RepeatedFraction(track, options));
    }

    private static List<RoadUsage> Roads(IReadOnlyList<RouteSegment> segments) =>
        segments
            .GroupBy(s => (s.Highway, s.Surface, Category: RoadRules.Classify(s)))
            .Select(g => new RoadUsage(g.Key.Highway, g.Key.Surface, g.Key.Category, g.Sum(s => s.LengthKm)))
            .OrderByDescending(r => r.Km)
            .ToList();

    /// <summary>
    /// Checks a point every <see cref="RouteAnalysisOptions.SampleSpacingKm"/> along the track. A point is repeated when
    /// the track passes within <see cref="RouteAnalysisOptions.RepeatToleranceKm"/> of it somewhere at least
    /// <see cref="RouteAnalysisOptions.MinRepeatSeparationKm"/> away along the track. Distances are to the track's
    /// line, not to its points or other checked points, which may be hundreds of metres apart. On a loop that ends at
    /// its start, the separation is measured around the loop: arriving home is not repeating the way out.
    /// </summary>
    private static double RepeatedFraction(ProjectedTrack track, RouteAnalysisOptions options)
    {
        var closed = track.Points[0].DistanceTo(track.Points[^1]) < options.RepeatToleranceKm;
        // The margin keeps a length that is a whole number of spacings (22 / 0.1 = 219.999…) from losing its last point.
        var checkedPoints = (int)Math.Floor(track.LengthKm / options.SampleSpacingKm + 1e-9) + 1;
        var repeated = 0;
        for (var i = 0; i < checkedPoints; i++)
        {
            var along = i * options.SampleSpacingKm;
            var distance = track.DistanceAwayFrom(
                track.At(along), along, options.MinRepeatSeparationKm, closed, stopBelowKm: options.RepeatToleranceKm);
            if (distance < options.RepeatToleranceKm)
            {
                repeated++;
            }
        }
        return repeated / (double)checkedPoints;
    }
}
