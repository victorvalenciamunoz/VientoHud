namespace VientoHud.Core.Routing;

/// <summary>What keeps a loop from being acceptable (DECISIONS 015, 016 and phase 2 of the plan).</summary>
[Flags]
public enum LoopProblems
{
    None = 0,

    /// <summary>Too much of the route is on spurs: a star.</summary>
    Spurs = 1,

    /// <summary>Too far from the target distance.</summary>
    Distance = 2,

    /// <summary>Some km on forbidden or unknown ways (motorway, unpaved…).</summary>
    Forbidden = 4,

    /// <summary>The route lies too far from the requested bearing.</summary>
    Bearing = 8,

    /// <summary>
    /// The route goes into a dead end and comes out the same way (at least <see cref="RouteAnalysisOptions.MinTurnBackKm"/>
    /// deep) other than at its destination: usually the way to a waypoint in the middle of a road.
    /// </summary>
    TurnBacks = 16,
}

/// <summary>Settings for <see cref="LoopGenerator"/>.</summary>
public sealed record LoopGeneratorOptions
{
    /// <summary>Most requests to the route engine for one loop (DECISIONS 017).</summary>
    public int MaxRequests { get; init; } = 12;

    /// <summary>Accepted distance error, as a share of the target (±10 % in the plan).</summary>
    public double DistanceTolerance { get; init; } = 0.1;

    /// <summary>Largest accepted share of the route on spurs (DECISIONS 015, provisional).</summary>
    public double MaxSpurFraction { get; init; } = 0.1;

    /// <summary>
    /// Largest accepted angle between the requested bearing and the direction from the start to the middle of the route.
    /// </summary>
    public double MaxBearingDeviationDegrees { get; init; } = 45;

    /// <summary>
    /// Bearings to try, as turns from the requested one, in order. The next one is tried when the engine cannot join
    /// the waypoints ("target island") or a bearing gives no valid loop.
    /// </summary>
    public IReadOnlyList<double> BearingTurns { get; init; } = [0, 20, -20, 40, -40];

    /// <summary>Rounds of waypoints (each with its own radius) on one bearing before turning.</summary>
    public int MaxRoundsPerBearing { get; init; } = 3;

    /// <summary>Points of the fan, start included (see <see cref="LoopWaypoints.Fan"/>).</summary>
    public int FanPoints { get; init; } = LoopWaypoints.DefaultPoints;

    public RouteAnalysisOptions Analysis { get; init; } = new();
}

/// <summary>A routed loop and how it measures against the request.</summary>
/// <param name="Route">The route, starting and ending at the start.</param>
/// <param name="Waypoints">The waypoints it was routed through.</param>
/// <param name="Analysis">Kinds of road, repetition and spurs.</param>
/// <param name="FanBearingDegrees">Bearing the fan was centred on (degrees TOWARDS), maybe turned from the requested one.</param>
/// <param name="BearingDeviationDegrees">Angle between the requested bearing and the direction to the middle of the route.</param>
/// <param name="DistanceError">Distance over target minus 1: +0.05 is 5 % too long.</param>
/// <param name="Problems">What is wrong with it, if anything.</param>
public sealed record LoopCandidate(
    Route Route,
    IReadOnlyList<Coordinate> Waypoints,
    RouteAnalysis Analysis,
    double FanBearingDegrees,
    double BearingDeviationDegrees,
    double DistanceError,
    LoopProblems Problems)
{
    public bool IsValid => Problems == LoopProblems.None;

    /// <summary>
    /// For ranking, lower is better: valid loops first (spurs, forbidden roads and a wrong bearing weigh most), then the
    /// one closest to the target distance, then the one with fewest trunk km (DECISIONS 016).
    /// </summary>
    public double Badness =>
        (Problems.HasFlag(LoopProblems.Spurs) ? 10 + Analysis.SpurFraction : 0)
        + (Problems.HasFlag(LoopProblems.Forbidden) ? 10 : 0)
        + (Problems.HasFlag(LoopProblems.Bearing) ? 5 : 0)
        + (Problems.HasFlag(LoopProblems.TurnBacks) ? 3 : 0)
        + Math.Abs(DistanceError)
        + Analysis.TrunkKm / 1000;
}

/// <summary>The outcome of generating one loop.</summary>
/// <param name="BearingDegrees">The bearing asked for (degrees TOWARDS).</param>
/// <param name="Best">The valid loop found, or else the least bad one; null if the engine never found a route.</param>
/// <param name="Requests">Requests made to the route engine.</param>
/// <param name="Attempts">Every loop routed, in order.</param>
public sealed record LoopResult(double BearingDegrees, LoopCandidate? Best, int Requests, IReadOnlyList<LoopCandidate> Attempts)
{
    /// <summary>False when there is no acceptable loop towards that bearing (not even turning it ±40°).</summary>
    public bool Found => Best?.IsValid == true;
}

/// <summary>
/// Generates a road-bike loop from a start, towards a bearing, of about a target distance (phase 2, step 4c; DECISIONS
/// 013 and 015-017). The route engine joins the waypoints; this class places them and judges the result:
/// <list type="number">
/// <item>Waypoints in a fan (<see cref="LoopWaypoints"/>) with a first radius for the target distance.</item>
/// <item>If the route has spurs (in and out of a dead end to reach a waypoint), each waypoint causing one moves to where
/// its spur leaves the route, and the route is asked again.</item>
/// <item>The radius is corrected from the distances obtained: between a radius that fell short and one that went too
/// far once both are known, otherwise in proportion. The length jumps with the radius (the road network is not smooth),
/// so a few rounds may be needed.</item>
/// <item>If the engine cannot join the waypoints, or a bearing gives no valid loop, the bearing turns (±20°, ±40°).</item>
/// </list>
/// It stops at the first valid loop or after <see cref="LoopGeneratorOptions.MaxRequests"/> requests.
/// </summary>
public sealed class LoopGenerator(IRouteEngine engine, LoopGeneratorOptions? options = null)
{
    private readonly LoopGeneratorOptions _options = options ?? new LoopGeneratorOptions();

    /// <summary>Generates a loop from <paramref name="start"/> towards <paramref name="bearingDegrees"/> (degrees TOWARDS).</summary>
    /// <exception cref="RouteException">The engine failed for a reason other than unreachable waypoints.</exception>
    public async Task<LoopResult> GenerateAsync(
        Coordinate start, double bearingDegrees, double targetKm, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(bearingDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(bearingDegrees), bearingDegrees, "Bearing must be finite.");
        }
        if (!double.IsFinite(targetKm) || targetKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetKm), targetKm, "Target distance must be positive.");
        }

        var attempts = new List<LoopCandidate>();
        var requests = 0;

        foreach (var turn in _options.BearingTurns)
        {
            var fanBearing = Normalize(bearingDegrees + turn);
            var history = new List<(double RadiusKm, double DistanceKm)>();
            double? radius = LoopWaypoints.InitialRadiusKm(targetKm, _options.FanPoints);

            for (var round = 0; round < _options.MaxRoundsPerBearing && radius is { } r; round++)
            {
                if (requests >= _options.MaxRequests)
                {
                    return Result();
                }
                var waypoints = LoopWaypoints.Fan(start, fanBearing, r, _options.FanPoints);
                requests++;
                var candidate = await TryRouteAsync(waypoints, fanBearing);
                if (candidate is null)
                {
                    break; // Unreachable: try the next bearing.
                }
                if (candidate.IsValid)
                {
                    return Result();
                }

                var reached = candidate.Route.DistanceKm;
                if ((candidate.Problems & (LoopProblems.Spurs | LoopProblems.TurnBacks)) != 0
                    && MoveOffDeadEnds(candidate) is { } moved
                    && requests < _options.MaxRequests)
                {
                    requests++;
                    if (await TryRouteAsync(moved, fanBearing) is { } repaired)
                    {
                        if (repaired.IsValid)
                        {
                            return Result();
                        }
                        reached = repaired.Route.DistanceKm;
                    }
                }

                history.Add((r, reached));
                radius = NextRadius(history, targetKm);
            }
        }
        return Result();

        async Task<LoopCandidate?> TryRouteAsync(IReadOnlyList<Coordinate> waypoints, double fanBearing)
        {
            try
            {
                var route = await engine.RouteAsync(waypoints, cancellationToken);
                var candidate = Judge(route, waypoints, fanBearing, bearingDegrees, targetKm);
                attempts.Add(candidate);
                return candidate;
            }
            catch (RouteException ex) when (ex.Failure == RouteFailure.Unreachable)
            {
                return null;
            }
        }

        LoopResult Result() => new(Normalize(bearingDegrees), attempts.MinBy(c => c.Badness), requests, attempts);
    }

    /// <summary>
    /// One loop towards each of <paramref name="bearingsDegrees"/>, one after another (the engine takes one request at a
    /// time), ranked: those with a valid loop first, best first; then the bearings without one.
    /// </summary>
    /// <exception cref="RouteException">The engine failed for a reason other than unreachable waypoints.</exception>
    public async Task<IReadOnlyList<LoopResult>> GenerateManyAsync(
        Coordinate start, IReadOnlyList<double> bearingsDegrees, double targetKm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bearingsDegrees);
        var results = new List<LoopResult>();
        foreach (var bearing in bearingsDegrees)
        {
            results.Add(await GenerateAsync(start, bearing, targetKm, cancellationToken));
        }
        return results
            .OrderBy(r => r.Found ? 0 : 1)
            .ThenBy(r => r.Best?.Badness ?? double.MaxValue)
            .ToList();
    }

    /// <summary>
    /// <paramref name="count"/> bearings evenly spread around the compass from <paramref name="firstDegrees"/>: three
    /// from 0° are 0°, 120° and 240°. Used while there is no wind to choose from (phase 2).
    /// </summary>
    public static IReadOnlyList<double> SpreadBearings(int count, double firstDegrees = 0)
    {
        if (count is < 1 or > 36)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Between 1 and 36 bearings.");
        }
        if (!double.IsFinite(firstDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(firstDegrees), firstDegrees, "Bearing must be finite.");
        }
        return Enumerable.Range(0, count).Select(i => Normalize(firstDegrees + 360.0 * i / count)).ToList();
    }

    private LoopCandidate Judge(
        Route route, IReadOnlyList<Coordinate> waypoints, double fanBearing, double bearingDegrees, double targetKm)
    {
        var analysis = RouteAnalyzer.Analyze(route, waypoints, _options.Analysis);
        var track = new ProjectedTrack(route.Points.Select(p => p.Position).ToList());
        var centre = track.Centre;
        // The track is projected around its first point, the start: X east, Y north.
        var towardsCentre = Normalize(Math.Atan2(centre.X, centre.Y) * 180 / Math.PI);
        var deviation = Compass.AngleBetween(towardsCentre, Normalize(bearingDegrees));
        var distanceError = route.DistanceKm / targetKm - 1;

        var problems = LoopProblems.None;
        if (analysis.SpurFraction > _options.MaxSpurFraction)
        {
            problems |= LoopProblems.Spurs;
        }
        if (Math.Abs(distanceError) > _options.DistanceTolerance)
        {
            problems |= LoopProblems.Distance;
        }
        if (analysis.ForbiddenKm > 0 || analysis.UnknownKm > 0)
        {
            problems |= LoopProblems.Forbidden;
        }
        if (deviation > _options.MaxBearingDeviationDegrees)
        {
            problems |= LoopProblems.Bearing;
        }
        if (analysis.TurnBacks.Any(t => !t.IsDestination))
        {
            problems |= LoopProblems.TurnBacks;
        }
        return new LoopCandidate(route, waypoints, analysis, fanBearing, deviation, distanceError, problems);
    }

    /// <summary>
    /// Waypoints with each one that leads into a dead end (a spur or a turn-back) moved to where the dead end leaves the
    /// route; null if no waypoint does. A waypoint leads into one when its closest point on the route is in it.
    /// </summary>
    private static IReadOnlyList<Coordinate>? MoveOffDeadEnds(LoopCandidate candidate)
    {
        var points = candidate.Route.Points;
        var track = new ProjectedTrack(points.Select(p => p.Position).ToList());
        var analysis = candidate.Analysis;
        // From where each dead end leaves the route to where it rejoins it. A spur appears once per pass: its first pass
        // runs from where it leaves to the end of the other pass.
        var deadEnds = analysis.Repeats
            .Where(r => r.IsSpur && r.StartKm < r.OtherPassStartKm)
            .Select(r => (FromKm: r.StartKm, ToKm: r.OtherPassEndKm))
            .Concat(analysis.TurnBacks.Where(t => !t.IsDestination).Select(t => (FromKm: t.AtKm - t.DepthKm, ToKm: t.AtKm + t.DepthKm)))
            .ToList();

        var waypoints = candidate.Waypoints.ToList();
        var moved = false;
        for (var i = 1; i < waypoints.Count - 1; i++)
        {
            var along = track.Closest(track.Projection.Project(candidate.Waypoints[i])).AlongKm;
            var containing = deadEnds.Where(d => along >= d.FromKm && along <= d.ToKm).ToList();
            if (containing.Count > 0)
            {
                // The outermost one, if they overlap (a long spur is also a turn-back).
                waypoints[i] = points[track.NearestPointIndex(containing.Min(d => d.FromKm))].Position;
                moved = true;
            }
        }
        return moved ? waypoints : null;
    }

    /// <summary>
    /// The next radius to try: between the largest radius that fell short and the smallest that went too far, if both
    /// are known; otherwise scaled by target / distance (at most halved or doubled). Null if it would repeat a radius.
    /// </summary>
    private static double? NextRadius(List<(double RadiusKm, double DistanceKm)> history, double targetKm)
    {
        var shorts = history.Where(h => h.DistanceKm < targetKm).ToList();
        var longs = history.Where(h => h.DistanceKm > targetKm).ToList();
        double radius;
        if (shorts.Count > 0 && longs.Count > 0
            && shorts.MaxBy(h => h.RadiusKm) is var below && longs.MinBy(h => h.RadiusKm) is var above
            && below.RadiusKm < above.RadiusKm)
        {
            radius = below.RadiusKm
                + (targetKm - below.DistanceKm) * (above.RadiusKm - below.RadiusKm) / (above.DistanceKm - below.DistanceKm);
        }
        else
        {
            var (lastRadius, lastDistance) = history[^1];
            radius = lastRadius * Math.Clamp(targetKm / lastDistance, 0.5, 2);
        }
        return history.Any(h => Math.Abs(h.RadiusKm - radius) < 0.02 * radius) ? null : radius;
    }

    private static double Normalize(double degrees) => (degrees % 360 + 360) % 360;
}
