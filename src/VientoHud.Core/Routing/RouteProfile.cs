namespace VientoHud.Core.Routing;

/// <summary>A point of the smoothed altitude profile of a route.</summary>
/// <param name="Km">Distance along the track from the start.</param>
/// <param name="ElevationMeters">Smoothed elevation above sea level.</param>
public readonly record struct ProfilePoint(double Km, double ElevationMeters);

/// <summary>A stretch of the route with the same <see cref="GradeBand"/>, as long as the band lasts.</summary>
/// <param name="StartKm">Where it starts, along the track.</param>
/// <param name="EndKm">Where it ends, along the track.</param>
/// <param name="StartElevationMeters">Smoothed elevation at the start.</param>
/// <param name="EndElevationMeters">Smoothed elevation at the end.</param>
/// <param name="Band">How steep it is on the whole (a few steps of a neighbouring band may be inside).</param>
public sealed record ProfileStretch(
    double StartKm,
    double EndKm,
    double StartElevationMeters,
    double EndElevationMeters,
    GradeBand Band)
{
    public double LengthKm => EndKm - StartKm;

    /// <summary>Average grade of the stretch in percent, positive uphill.</summary>
    public double GradePercent => LengthKm > 0 ? (EndElevationMeters - StartElevationMeters) / (LengthKm * 10) : 0;
}

/// <summary>
/// The altitude profile of a route, from the elevations of its track points: resampled every
/// <see cref="DefaultStepKm"/>, smoothed over <see cref="DefaultSmoothingKm"/> for drawing and for the grades
/// (engine elevations come from a 90 m grid and jump from point to point), cut into stretches by
/// <see cref="GradeBand"/>, with the climb, the drop and the extremes. The PERFIL view draws it and the 3D view
/// colours the track with its bands (DECISIONS 056: in the profile the GPX's own elevations rule).
/// </summary>
/// <param name="Points">The smoothed profile, one point per step, from km 0 to the end.</param>
/// <param name="Stretches">
/// Consecutive stretches by band, covering the whole route, none shorter than <see cref="MinStretchKm"/> (unless
/// the route is).
/// </param>
/// <param name="AscentMeters">
/// Total climb of the resampled (not smoothed) elevations, ignoring ups and downs smaller than
/// <see cref="HysteresisMeters"/>.
/// </param>
/// <param name="DescentMeters">Total drop, the same way.</param>
/// <param name="MinElevationMeters">Lowest point of the smoothed profile.</param>
/// <param name="MaxElevationMeters">Highest point of the smoothed profile.</param>
/// <param name="MaxGradePercent">
/// Steepest grade of the smoothed profile measured over <see cref="DefaultSmoothingKm"/>, in percent (negative if
/// the route never climbs).
/// </param>
public sealed record RouteProfile(
    IReadOnlyList<ProfilePoint> Points,
    IReadOnlyList<ProfileStretch> Stretches,
    double AscentMeters,
    double DescentMeters,
    double MinElevationMeters,
    double MaxElevationMeters,
    double MaxGradePercent)
{
    /// <summary>Distance between profile points.</summary>
    public const double DefaultStepKm = 0.1;

    /// <summary>Width of the moving average that smooths the elevations (centred on each point).</summary>
    public const double DefaultSmoothingKm = 0.3;

    /// <summary>
    /// A band has to last this long to show as a stretch of its own; shorter ones join a neighbour (a rolling road
    /// painted in 100 m slivers flickered on the author's screen, 2026-10-23).
    /// </summary>
    public const double MinStretchKm = 0.3;

    /// <summary>
    /// Ups and downs smaller than this do not count towards the climb or the drop (a hysteresis, the same idea as
    /// BRouter's "filtered ascend", whose profiles use a 10 m buffer): a 90 m grid wobbles a few metres on the flat.
    /// </summary>
    public const double HysteresisMeters = 10;

    /// <summary>Length of the route along the track.</summary>
    public double LengthKm => Points[^1].Km;

    /// <summary>
    /// The profile of <paramref name="route"/>, or null when the engine gave no elevation for some of its points.
    /// </summary>
    public static RouteProfile? From(Route route, double stepKm = DefaultStepKm, double smoothingKm = DefaultSmoothingKm)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Points.Any(p => p.ElevationMeters is null))
        {
            return null;
        }
        return From(
            route.Points.Select(p => p.Position).ToArray(),
            route.Points.Select(p => p.ElevationMeters!.Value).ToArray(),
            stepKm,
            smoothingKm);
    }

    /// <summary>
    /// The profile of a track whose elevation at each point is given (from the engine, or sampled from a terrain
    /// model by the 3D view).
    /// </summary>
    public static RouteProfile From(
        IReadOnlyList<Coordinate> track,
        IReadOnlyList<double> elevationsMeters,
        double stepKm = DefaultStepKm,
        double smoothingKm = DefaultSmoothingKm)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(elevationsMeters);
        if (track.Count < 2)
        {
            throw new ArgumentException("A track needs at least two points.", nameof(track));
        }
        if (elevationsMeters.Count != track.Count)
        {
            throw new ArgumentException("One elevation per track point.", nameof(elevationsMeters));
        }
        if (elevationsMeters.Any(e => !double.IsFinite(e)))
        {
            throw new ArgumentException("Elevations must be numbers.", nameof(elevationsMeters));
        }
        if (!double.IsFinite(stepKm) || stepKm is < 0.01 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stepKm), stepKm, "The step must be 10 m to 1 km.");
        }
        if (!double.IsFinite(smoothingKm) || smoothingKm is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingKm), smoothingKm, "The smoothing must be 0 to 5 km.");
        }

        var alongKm = new double[track.Count];
        for (var i = 1; i < track.Count; i++)
        {
            alongKm[i] = alongKm[i - 1] + track[i - 1].DistanceKmTo(track[i]);
        }
        var totalKm = alongKm[^1];
        if (totalKm == 0)
        {
            throw new ArgumentException("The track has no length.", nameof(track));
        }
        var elevations = elevationsMeters.ToArray();

        // Resample every stepKm (the last point at the exact end), then smooth with a centred moving average that
        // shrinks symmetrically at the ends (so a steady slope stays steady to the very end).
        var count = Math.Max(1, (int)Math.Round(totalKm / stepKm));
        var step = totalKm / count;
        var raw = Enumerable.Range(0, count + 1).Select(k => RouteLegs.ValueAt(elevations, alongKm, k * step)).ToArray();
        var half = (int)Math.Round(smoothingKm / 2 / step);
        var smooth = new double[raw.Length];
        for (var k = 0; k < raw.Length; k++)
        {
            var reach = Math.Min(half, Math.Min(k, raw.Length - 1 - k));
            smooth[k] = raw[(k - reach)..(k + reach + 1)].Average();
        }
        var points = smooth.Select((e, k) => new ProfilePoint(k * step, e)).ToArray();

        // The grade of each step is measured over the smoothing width around it (clipped at the ends), so that a
        // grid's wobble does not paint the flat in many colours. Stretches: consecutive steps in the same band.
        double GradeAt(int k)
        {
            var from = Math.Max(0, k - half);
            var to = Math.Min(count, k + 1 + half);
            return (smooth[to] - smooth[from]) / ((to - from) * step * 10);
        }
        var stretches = new List<ProfileStretch>();
        var startIndex = 0;
        var band = GradeBands.Of(GradeAt(0));
        var maxGrade = double.NegativeInfinity;
        for (var k = 0; k < count; k++)
        {
            var grade = GradeAt(k);
            maxGrade = Math.Max(maxGrade, grade);
            var next = GradeBands.Of(grade);
            if (next != band)
            {
                stretches.Add(Stretch(points, startIndex, k, band));
                startIndex = k;
                band = next;
            }
        }
        stretches.Add(Stretch(points, startIndex, count, band));
        MergeShortStretches(stretches, points, Math.Max(MinStretchKm, step));

        // The climb and the drop come from the resampled points before smoothing: smoothing flattens the short
        // hills of a rolling road and lost a quarter of the climb on one recorded route (STATUS, phase 8 step 1).
        return new RouteProfile(
            points,
            stretches,
            Climb(raw),
            Climb(raw.Select(e => -e).ToArray()),
            smooth.Min(),
            smooth.Max(),
            maxGrade);
    }

    /// <summary>The smoothed elevation at <paramref name="km"/> along the track (clamped to the route).</summary>
    public double ElevationAt(double km)
    {
        var step = Points[1].Km;
        var k = Math.Clamp(km / step, 0, Points.Count - 1);
        var i = Math.Min((int)k, Points.Count - 2);
        return Points[i].ElevationMeters + (k - i) * (Points[i + 1].ElevationMeters - Points[i].ElevationMeters);
    }

    /// <summary>The stretch that contains <paramref name="km"/> (the last one at the very end).</summary>
    public ProfileStretch StretchAt(double km) =>
        Stretches.FirstOrDefault(s => km < s.EndKm) ?? Stretches[^1];

    private static ProfileStretch Stretch(ProfilePoint[] points, int from, int to, GradeBand band) =>
        new(points[from].Km, points[to].Km, points[from].ElevationMeters, points[to].ElevationMeters, band);

    // A band has to last at least minKm to show: a shorter stretch joins the neighbour whose grade is closer to its
    // own (the previous one when alone), shortest first, until none is left. The ends keep the route's ends.
    private static void MergeShortStretches(List<ProfileStretch> stretches, ProfilePoint[] points, double minKm)
    {
        while (stretches.Count > 1)
        {
            var i = stretches.Select((s, k) => (s.LengthKm, k)).Where(x => x.LengthKm < minKm - 1e-9).OrderBy(x => x.LengthKm).Select(x => (int?)x.k).FirstOrDefault();
            if (i is not { } index)
            {
                return;
            }
            var s = stretches[index];
            var previous = index > 0 ? stretches[index - 1] : null;
            var next = index < stretches.Count - 1 ? stretches[index + 1] : null;
            var intoPrevious = previous is not null
                && (next is null || Math.Abs(previous.GradePercent - s.GradePercent) <= Math.Abs(next.GradePercent - s.GradePercent));
            if (intoPrevious)
            {
                stretches[index - 1] = previous! with { EndKm = s.EndKm, EndElevationMeters = s.EndElevationMeters };
            }
            else
            {
                stretches[index + 1] = next! with { StartKm = s.StartKm, StartElevationMeters = s.StartElevationMeters };
            }
            stretches.RemoveAt(index);
        }
    }

    // Total climb ignoring ups and downs smaller than the hysteresis: the elevation has to fall more than
    // HysteresisMeters below the highest point reached before the climb to it counts, and rise more than that above
    // the lowest point before a new climb starts. For the drop, call it with the elevations negated.
    private static double Climb(double[] elevations)
    {
        var total = 0.0;
        var low = elevations[0];
        var high = elevations[0];
        var climbing = false;
        foreach (var e in elevations)
        {
            if (climbing)
            {
                if (e > high)
                {
                    high = e;
                }
                else if (high - e > HysteresisMeters)
                {
                    total += high - low;
                    low = e;
                    climbing = false;
                }
            }
            else
            {
                if (e < low)
                {
                    low = e;
                }
                else if (e - low > HysteresisMeters)
                {
                    high = e;
                    climbing = true;
                }
            }
        }
        if (climbing)
        {
            total += high - low;
        }
        return total;
    }
}
