using VientoHud.Core.Rain.Forecasts;

namespace VientoHud.Core.Rain;

/// <summary>Where a chance of rain comes from (DECISIONS 034).</summary>
public enum RainSourceKind
{
    /// <summary>The radar moved forward (up to 2 h from now).</summary>
    Radar,

    /// <summary>From 2 to 3 h: radar and model mixed, the model weighing more as time goes on.</summary>
    Blend,

    /// <summary>AROME in an area, where the radar has nothing (or in the mix, without radar).</summary>
    Model,

    /// <summary>The model's hourly chance of rain (best_match), where AROME does not reach.</summary>
    Fallback,

    /// <summary>Beyond 3 h: not counted place by place, only as a warning for the area.</summary>
    BeyondHorizon,

    /// <summary>No source has anything for that place and time.</summary>
    Unknown,
}

/// <summary>The chance of rain at a place and time, and where it comes from.</summary>
/// <param name="Probability">In [0, 1]; null when not counted (beyond 3 h) or unknown.</param>
public sealed record PlaceRain(double? Probability, RainSourceKind Source)
{
    public RainLikelihood? Likelihood => Probability is { } p ? RainLikelihoods.Classify(p) : null;
}

/// <summary>Warning for the area beyond 3 h: from when, and how likely at most, showers are somewhere around.</summary>
/// <param name="From">First time (UTC) with a chance of at least 20 % ("possible").</param>
/// <param name="MaxProbability">Highest chance in the period, in [0, 1].</param>
public sealed record AreaRain(DateTimeOffset From, double MaxProbability, RainSourceKind Source)
{
    public RainLikelihood Likelihood => RainLikelihoods.Classify(MaxProbability);
}

/// <summary>Settings for <see cref="RainOutlook"/> (DECISIONS 034).</summary>
public sealed record RainOutlookOptions
{
    /// <summary>The radar alone up to this long after now.</summary>
    public TimeSpan RadarUntil { get; init; } = TimeSpan.FromHours(2);

    /// <summary>From <see cref="RadarUntil"/> to this, radar and model mixed; after it, only a warning for the area.</summary>
    public TimeSpan CountedUntil { get; init; } = TimeSpan.FromHours(3);

    public NowcastOptions Nowcast { get; init; } = new();

    public ModelRainOptions Model { get; init; } = new();
}

/// <summary>
/// Everything known about the rain to come around a place, at one moment: the latest radar frame and its motion,
/// AROME on a grid, and the model's hourly chance as a fallback (DECISIONS 033 and 034). Gives the chance of rain at
/// any place and time of the next hours: the radar up to 2 h, a mix up to 3 h, and beyond that only a warning for the
/// whole area. Immutable.
/// </summary>
public sealed class RainOutlook
{
    // The whole grid: every point counts for the area warning.
    private static readonly ModelRainOptions WholeArea = new() { RadiusKm = 1e6, TimeMargin = TimeSpan.Zero };

    public RainOutlook(
        DateTimeOffset now,
        RainGrid? radar,
        RainMotion? motion,
        ModelRainField? model,
        RainProbabilitySeries? fallback,
        RainOutlookOptions? options = null)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Now must be in UTC.", nameof(now));
        }
        if (radar is not null && radar.Time > now)
        {
            throw new ArgumentException("The radar frame cannot be after now.", nameof(radar));
        }
        Now = now;
        Radar = radar;
        Motion = motion;
        Model = model;
        Fallback = fallback;
        Options = options ?? new RainOutlookOptions();
    }

    public DateTimeOffset Now { get; }
    public RainGrid? Radar { get; }
    public RainMotion? Motion { get; }
    public ModelRainField? Model { get; }
    public RainProbabilitySeries? Fallback { get; }
    public RainOutlookOptions Options { get; }

    /// <summary>Last time whose rain is counted place by place (3 h after now).</summary>
    public DateTimeOffset CountedUntil => Now + Options.CountedUntil;

    /// <summary>Chance of rain at <paramref name="place"/> at <paramref name="time"/> (UTC).</summary>
    public PlaceRain At(Coordinate place, DateTimeOffset time)
    {
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        var lead = time < Now ? TimeSpan.Zero : time - Now;
        if (lead > Options.CountedUntil)
        {
            return new PlaceRain(null, RainSourceKind.BeyondHorizon);
        }

        var radar = RadarChance(place, time);
        if (lead <= Options.RadarUntil && radar is not null)
        {
            return new PlaceRain(radar, RainSourceKind.Radar);
        }

        var (model, modelSource) = ModelChance(place, time);
        if (lead <= Options.RadarUntil)
        {
            // No radar here: the model, if anything.
            return model is null ? new PlaceRain(null, RainSourceKind.Unknown) : new PlaceRain(model, modelSource);
        }

        // From 2 to 3 h: the model's weight grows from 0 to 1.
        var weight = (lead - Options.RadarUntil) / (Options.CountedUntil - Options.RadarUntil);
        return (radar, model) switch
        {
            ({ } r, { } m) => new PlaceRain((1 - weight) * r + weight * m, RainSourceKind.Blend),
            ({ } r, null) => new PlaceRain(r, RainSourceKind.Radar),
            (null, { } m) => new PlaceRain(m, modelSource),
            _ => new PlaceRain(null, RainSourceKind.Unknown),
        };
    }

    /// <summary>
    /// Warning for the area from <paramref name="from"/> to <paramref name="to"/> (UTC), for the part of a ride beyond
    /// what is counted place by place: AROME over its whole grid, interval by interval, or the fallback where AROME has
    /// nothing. Null when nothing reaches a 20 % chance, or there is nothing to say.
    /// </summary>
    public AreaRain? AreaWarning(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return null;
        }
        var step = Model?.Interval ?? TimeSpan.FromMinutes(15);
        AreaRain? warning = null;
        for (var time = from; time <= to; time += step)
        {
            double? chance;
            RainSourceKind source;
            if (Model is not null && Model.LastTime >= time && Model.Probability(default, time, WholeArea) is { } area)
            {
                (chance, source) = (area, RainSourceKind.Model);
            }
            else
            {
                (chance, source) = (Fallback?.ProbabilityAt(time), RainSourceKind.Fallback);
            }
            if (chance is not { } p || p < RainLikelihoods.PossibleFrom)
            {
                continue;
            }
            warning = warning is null
                ? new AreaRain(time, p, source)
                : warning with { MaxProbability = Math.Max(warning.MaxProbability, p) };
        }
        return warning;
    }

    /// <summary>Square around the start the radar is cut to.</summary>
    public const double RadarRadiusKm = 150;

    /// <summary>
    /// Asks every source once for the rain around <paramref name="start"/> from <paramref name="now"/> to
    /// <paramref name="until"/>: the latest radar frame and the three before it (motion); AROME on a grid covering
    /// <paramref name="reachKm"/> around the start plus the model area radius, only if <paramref name="until"/> goes
    /// beyond the radar alone; and the fallback hourly chance at the start. A model that does not cover the place
    /// (outside AROME) is left out and the fallback takes its place.
    /// </summary>
    /// <param name="reachKm">How far from the start the rides go.</param>
    /// <exception cref="RadarException">The radar failed.</exception>
    /// <exception cref="RainForecastException">The fallback failed.</exception>
    public static async Task<RainOutlook> ForecastAsync(
        IRadarSource radar,
        IRainForecastSource model,
        IRainProbabilitySource fallback,
        Coordinate start,
        double reachKm,
        DateTimeOffset now,
        DateTimeOffset until,
        RainOutlookOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new RainOutlookOptions();
        if (now.Offset != TimeSpan.Zero || until.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Times must be in UTC.");
        }

        var latestTime = await radar.GetLatestFrameTimeAsync(cancellationToken);
        var frame = await radar.GetFrameAtOrBeforeAsync(
            latestTime < now ? latestTime : now, start, RadarRadiusKm, cancellationToken: cancellationToken);
        RainMotion? motion = null;
        if (frame is not null)
        {
            var frames = await radar.GetMotionFramesAsync(frame, start, RadarRadiusKm, cancellationToken);
            motion = frames.Count >= 2 ? RainMotions.Combine(frames).Motion : null;
        }

        ModelRainField? field = null;
        RainProbabilitySeries? probability = null;
        if (until > now + options.RadarUntil)
        {
            var radius = reachKm + options.Model.RadiusKm;
            // About 5 km between points, but never more than 15 × 15 points (each one counts against the limits).
            var spacing = Math.Max(5, radius / 7);
            try
            {
                var series = await model.GetAsync(
                    ModelRainField.GridAround(start, radius, spacing), now, until, cancellationToken);
                field = new ModelRainField(series);
            }
            catch (RainForecastException)
            {
                // Outside the model (e.g. southern Spain for AROME) or the model failed: the fallback stays.
            }
            probability = (await fallback.GetProbabilityAsync([start], now, until.AddHours(1), cancellationToken))
                .SingleOrDefault();
        }

        return new RainOutlook(now, frame, motion, field, probability, options);
    }

    private double? RadarChance(Coordinate place, DateTimeOffset time) =>
        Radar is null || Radar.CellOf(place) is null
            ? null
            : Nowcast.ChanceAt(Radar, Motion, place, time < Radar.Time ? Radar.Time : time, Options.Nowcast);

    private (double? Chance, RainSourceKind Source) ModelChance(Coordinate place, DateTimeOffset time)
    {
        if (Model?.Probability(place, time, Options.Model) is { } model)
        {
            return (model, RainSourceKind.Model);
        }
        return (Fallback?.ProbabilityAt(time), RainSourceKind.Fallback);
    }
}
