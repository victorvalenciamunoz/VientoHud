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

    /// <summary>
    /// The stem may start this far from either end of the track: leaving and entering a town by different streets
    /// (one-way streets, a ring road), or a recording that stops short of home.
    /// </summary>
    public double MaxStemStartKm { get; init; } = 3;
}

/// <summary>What a routed loop is like: kinds of road, how close it passes to each waypoint and how much it repeats.</summary>
/// <param name="DistanceKm">Length of the route, as the engine measured it.</param>
/// <param name="Roads">Km per road type and surface, longest first.</param>
/// <param name="WaypointOffsetsKm">
/// For each requested waypoint, in order, how close the route passes to it. Engines move waypoints to the nearest
/// allowed road, which may be far away (a point in the sea gave a route to a coast 446 km away).
/// </param>
/// <param name="RepeatedFraction">
/// Share of the route, from 0 to 1, that runs where another part of the route also runs. Both passes count.
/// </param>
/// <param name="StemFraction">
/// Share of the route, from 0 to 1, that is its stem: leaving and coming home by the same road, like the string of a
/// balloon. Both passes count. 0 for a loop without a stem, almost 1 for a pure out-and-back ride.
/// </param>
/// <param name="RepeatedOffStemFraction">
/// Share of the route, from 0 to 1, repeated anywhere but on the stem: out-and-back spurs in the middle of the ride,
/// as in a star. Loops, balloons and out-and-back rides are near 0 (DECISIONS 015).
/// </param>
public sealed record RouteAnalysis(
    double DistanceKm,
    IReadOnlyList<RoadUsage> Roads,
    IReadOnlyList<double> WaypointOffsetsKm,
    double RepeatedFraction,
    double StemFraction,
    double RepeatedOffStemFraction)
{
    public double ForbiddenKm => KmOf(RoadCategory.Forbidden);

    public double PavedTrackKm => KmOf(RoadCategory.PavedTrack);

    public double UnknownKm => KmOf(RoadCategory.Unknown);

    public double RepeatedKm => RepeatedFraction * DistanceKm;

    public double StemKm => StemFraction * DistanceKm;

    public double RepeatedOffStemKm => RepeatedOffStemFraction * DistanceKm;

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
        if (options.SampleSpacingKm <= 0 || options.RepeatToleranceKm <= 0 || options.MinRepeatSeparationKm <= 0
            || options.MaxStemStartKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Repetition settings must be positive.");
        }

        var track = new ProjectedTrack(route.Points.Select(p => p.Position).ToList());
        var repetition = Repetition(track, options);
        return new RouteAnalysis(
            route.DistanceKm,
            Roads(route.Segments),
            waypoints.Select(w => track.DistanceTo(track.Projection.Project(w))).ToList(),
            repetition.Repeated,
            repetition.Stem,
            repetition.RepeatedOffStem);
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
    /// <remarks>
    /// The stem is found from each end of the track: the run of repeated points whose other pass is in the other half
    /// of the track (the way out ridden again on the way home). It may start up to
    /// <see cref="RouteAnalysisOptions.MaxStemStartKm"/> from the end, and once started may have gaps shorter than the
    /// separation. A spur near home is not a stem: both its passes are in the same half.
    /// </remarks>
    private static (double Repeated, double Stem, double RepeatedOffStem) Repetition(
        ProjectedTrack track, RouteAnalysisOptions options)
    {
        var closed = track.Points[0].DistanceTo(track.Points[^1]) < options.RepeatToleranceKm;
        // The margin keeps a length that is a whole number of spacings (22 / 0.1 = 219.999…) from losing its last point.
        var count = (int)Math.Floor(track.LengthKm / options.SampleSpacingKm + 1e-9) + 1;
        var half = track.LengthKm / 2;

        // Where along the track the other pass of each checked point is; null if the point is not repeated.
        var otherPass = new double?[count];
        for (var i = 0; i < count; i++)
        {
            var along = i * options.SampleSpacingKm;
            var closest = track.ClosestAwayFrom(track.At(along), along, options.MinRepeatSeparationKm, closed);
            otherPass[i] = closest.DistanceKm < options.RepeatToleranceKm ? closest.AlongKm : null;
        }

        var stemOutEnd = StemEnd(Enumerable.Range(0, count).Where(i => i * options.SampleSpacingKm < half), other => other > half);
        var stemBackStart = StemEnd(Enumerable.Range(0, count).Reverse().Where(i => i * options.SampleSpacingKm > half), other => other < half);
        var repeated = otherPass.Count(o => o is not null);
        var offStem = Enumerable.Range(0, count)
            .Count(i => otherPass[i] is not null && (stemOutEnd is null || i > stemOutEnd) && (stemBackStart is null || i < stemBackStart));
        var stemKm = (stemOutEnd ?? 0) * options.SampleSpacingKm
            + (stemBackStart is { } back ? track.LengthKm - back * options.SampleSpacingKm : 0);

        return (repeated / (double)count, Math.Min(1, stemKm / track.LengthKm), offStem / (double)count);

        // Last checked point, walking from one end, of the run whose other pass is in the other half; null if none.
        int? StemEnd(IEnumerable<int> walk, Func<double, bool> otherHalf)
        {
            int? end = null;
            var gapKm = 0.0;
            foreach (var i in walk)
            {
                if (otherPass[i] is { } other && otherHalf(other))
                {
                    end = i;
                    gapKm = 0;
                }
                else if ((gapKm += options.SampleSpacingKm) > (end is null
                             ? Math.Max(options.MaxStemStartKm, options.MinRepeatSeparationKm)
                             : options.MinRepeatSeparationKm))
                {
                    break;
                }
            }
            return end;
        }
    }
}
