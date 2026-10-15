namespace VientoHud.Core.Routing;

/// <summary>Km ridden on one kind of way.</summary>
/// <param name="Highway">OSM "highway" tag, or null if not reported.</param>
/// <param name="Surface">OSM "surface" tag, or null if not reported.</param>
public sealed record RoadUsage(string? Highway, string? Surface, RoadCategory Category, double Km);

/// <summary>How the repetition of a route is measured (DECISIONS 014 and 015).</summary>
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
    /// A road ridden there and back is a connector, not a spur, when between its two passes there are at least this many
    /// km ridden only once: a loop at the far end (a balloon's string, a lasso's neck), not just a turn, a lap of a
    /// village or more spurs. See also <see cref="MinLoopFraction"/>.
    /// </summary>
    public double MinLoopKm { get; init; } = 5;

    /// <summary>As <see cref="MinLoopKm"/>, as a share of the route: the larger of the two applies.</summary>
    public double MinLoopFraction { get; init; } = 0.15;

    /// <summary>
    /// A spur that starts this close to both ends of the track is the ride itself (out and back from home), not a
    /// detour. Leaving and entering a town by different streets, or a recording that stops short of home, fit in it.
    /// </summary>
    public double HomeStretchKm { get; init; } = 3;
}

/// <summary>
/// A stretch of the route that runs where another part of it also runs. Each such stretch appears twice, once per pass.
/// </summary>
/// <param name="StartKm">Where the stretch begins, along the route.</param>
/// <param name="EndKm">Where it ends, along the route.</param>
/// <param name="OtherPassStartKm">Where the other pass is, along the route, nearest the start of the route.</param>
/// <param name="OtherPassEndKm">Where the other pass is, along the route, farthest from the start of the route.</param>
/// <param name="IsSpur">
/// True for a dead-end detour: in and out by the same road with almost nothing in between, away from home. That is
/// what makes a route a star. False for a balloon's string, a lasso's neck or an out-and-back ride from home.
/// </param>
public sealed record RepeatedStretch(double StartKm, double EndKm, double OtherPassStartKm, double OtherPassEndKm, bool IsSpur);

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
/// <param name="SpurFraction">
/// Share of the route, from 0 to 1, repeated on spurs (see <see cref="RepeatedStretch.IsSpur"/>). Both passes count.
/// Loops, balloons, lassos and out-and-back rides are near 0; stars are not (DECISIONS 015).
/// </param>
/// <param name="Repeats">The repeated stretches, in riding order.</param>
public sealed record RouteAnalysis(
    double DistanceKm,
    IReadOnlyList<RoadUsage> Roads,
    IReadOnlyList<double> WaypointOffsetsKm,
    double RepeatedFraction,
    double SpurFraction,
    IReadOnlyList<RepeatedStretch> Repeats)
{
    public double ForbiddenKm => KmOf(RoadCategory.Forbidden);

    public double PavedTrackKm => KmOf(RoadCategory.PavedTrack);

    public double UnknownKm => KmOf(RoadCategory.Unknown);

    public double RepeatedKm => RepeatedFraction * DistanceKm;

    public double SpurKm => SpurFraction * DistanceKm;

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
            || options.MinLoopKm < 0 || options.MinLoopFraction < 0 || options.HomeStretchKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Repetition settings must be positive.");
        }

        var track = new ProjectedTrack(route.Points.Select(p => p.Position).ToList());
        var (repeated, spur, repeats) = Repetition(track, options);
        return new RouteAnalysis(
            route.DistanceKm,
            Roads(route.Segments),
            waypoints.Select(w => track.DistanceTo(track.Projection.Project(w))).ToList(),
            repeated,
            spur,
            repeats);
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
    /// Repeated points closer than the separation make up one stretch. Between the two passes of a stretch lies the
    /// part of the route ridden from one to the other: a whole loop for a string or a neck, only the turn for a spur.
    /// So a stretch is a spur when, at its innermost point, the km ridden only once between the passes are fewer than
    /// <see cref="RouteAnalysisOptions.MinLoopKm"/> (or <see cref="RouteAnalysisOptions.MinLoopFraction"/> of the
    /// route), unless the stretch starts within <see cref="RouteAnalysisOptions.HomeStretchKm"/> of both ends of the
    /// track (an out-and-back ride from home). Counting only km ridden once keeps a spur at the end of a longer spur
    /// from passing as a loop.
    /// </remarks>
    private static (double Repeated, double Spur, List<RepeatedStretch> Repeats) Repetition(
        ProjectedTrack track, RouteAnalysisOptions options)
    {
        var closed = track.Points[0].DistanceTo(track.Points[^1]) < options.RepeatToleranceKm;
        // The margin keeps a length that is a whole number of spacings (22 / 0.1 = 219.999…) from losing its last point.
        var count = (int)Math.Floor(track.LengthKm / options.SampleSpacingKm + 1e-9) + 1;

        // Where along the track the other pass of each checked point is; null if the point is not repeated.
        var otherPass = new double?[count];
        for (var i = 0; i < count; i++)
        {
            var along = i * options.SampleSpacingKm;
            var closest = track.ClosestAwayFrom(track.At(along), along, options.MinRepeatSeparationKm, closed);
            otherPass[i] = closest.DistanceKm < options.RepeatToleranceKm ? closest.AlongKm : null;
        }

        // Checked points ridden only once before each point, to count them between two positions quickly.
        var onceBefore = new int[count + 1];
        for (var i = 0; i < count; i++)
        {
            onceBefore[i + 1] = onceBefore[i] + (otherPass[i] is null ? 1 : 0);
        }
        double OnceKmBetween(double lowKm, double highKm)
        {
            var from = Math.Clamp((int)Math.Floor(lowKm / options.SampleSpacingKm) + 1, 0, count);
            var to = Math.Clamp((int)Math.Ceiling(highKm / options.SampleSpacingKm), 0, count);
            return to > from ? (onceBefore[to] - onceBefore[from]) * options.SampleSpacingKm : 0;
        }
        var minLoopKm = Math.Max(options.MinLoopKm, options.MinLoopFraction * track.LengthKm);

        var repeats = new List<RepeatedStretch>();
        var repeated = 0;
        var onSpurs = 0;
        var maxGap = (int)Math.Round(options.MinRepeatSeparationKm / options.SampleSpacingKm);
        var first = 0;
        while (first < count)
        {
            if (otherPass[first] is null)
            {
                first++;
                continue;
            }

            // A stretch: repeated points with gaps shorter than the separation, whose other pass moves along with them.
            // Where the other pass jumps elsewhere, a different stretch begins (e.g. the string home followed by a spur).
            var last = first;
            for (var i = first + 1; i < count && i - last < maxGap; i++)
            {
                if (otherPass[i] is not { } other)
                {
                    continue;
                }
                if (Math.Abs(other - otherPass[last]!.Value) > (i - last) * options.SampleSpacingKm + options.MinRepeatSeparationKm)
                {
                    break;
                }
                last = i;
            }

            var points = Enumerable.Range(first, last - first + 1).Where(i => otherPass[i] is not null).ToList();
            var pairs = points
                .Select(i => (Here: i * options.SampleSpacingKm, Other: otherPass[i]!.Value))
                .Select(p => (Low: Math.Min(p.Here, p.Other), High: Math.Max(p.Here, p.Other)))
                .ToList();
            var loopKm = pairs.Min(p => OnceKmBetween(p.Low, p.High));
            var fromHome = pairs.Min(p => p.Low) <= options.HomeStretchKm
                && track.LengthKm - pairs.Max(p => p.High) <= options.HomeStretchKm;
            var isSpur = loopKm < minLoopKm && !fromHome;

            var others = points.Select(i => otherPass[i]!.Value).ToList();
            repeats.Add(new RepeatedStretch(
                first * options.SampleSpacingKm, last * options.SampleSpacingKm, others.Min(), others.Max(), isSpur));
            repeated += points.Count;
            onSpurs += isSpur ? points.Count : 0;
            first = last + 1;
        }

        return (repeated / (double)count, onSpurs / (double)count, repeats);
    }
}
