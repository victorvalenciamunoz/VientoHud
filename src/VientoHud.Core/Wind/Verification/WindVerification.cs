using VientoHud.Core.Wind.Observations;

namespace VientoHud.Core.Wind.Verification;

/// <summary>A forecast hour paired with what a station measured at that time.</summary>
public sealed record MatchedHour(string StationId, WindSample Forecast, WindObservation Observed)
{
    /// <summary>Forecast minus observed speed, in km/h: positive when the model overestimated the wind.</summary>
    public double SpeedErrorKmh => Forecast.SpeedKmh - Observed.SpeedKmh;

    /// <summary>Angle between forecast and observed direction, in [0, 180]. Null when the observation has no direction.</summary>
    public double? DirectionErrorDegrees => Observed.DirectionFromDegrees is { } observed
        ? Compass.AngleBetween(Forecast.DirectionFromDegrees, observed)
        : null;
}

/// <summary>How well a forecast matched observations over a set of hours. Null metrics mean no hours to measure.</summary>
/// <param name="Hours">Matched hours.</param>
/// <param name="SpeedBiasKmh">Mean of forecast minus observed speed: negative when the model falls short.</param>
/// <param name="SpeedMeanAbsoluteErrorKmh">Mean absolute speed error.</param>
/// <param name="DirectionHours">Matched hours whose observation has a direction (not calm, not variable).</param>
/// <param name="DirectionMeanAbsoluteErrorDegrees">Mean angle between forecast and observed direction.</param>
/// <param name="DirectionHitRate">Fraction of <paramref name="DirectionHours"/> within the hit angle (±45° by default).</param>
public sealed record VerificationScore(
    int Hours,
    double? SpeedBiasKmh,
    double? SpeedMeanAbsoluteErrorKmh,
    int DirectionHours,
    double? DirectionMeanAbsoluteErrorDegrees,
    double? DirectionHitRate);

/// <summary>Scores for all hours and split by observed wind.</summary>
/// <param name="All">Every matched hour.</param>
/// <param name="Light">Hours with observed speed below <see cref="VerificationOptions.WindyFromKmh"/>.</param>
/// <param name="Windy">
/// Hours with observed speed at or above <see cref="VerificationOptions.WindyFromKmh"/>: the ones that matter
/// for the ride. Splitting by the observed speed biases the speed error of each band (strong observed hours tend
/// to be underforecast, light ones overforecast); compare models band by band, not bands with each other.
/// </param>
public sealed record VerificationReport(VerificationScore All, VerificationScore Light, VerificationScore Windy);

public sealed record VerificationOptions
{
    /// <summary>
    /// How far from the forecast hour an observation may be to count. METAR comes at :00 and :30, plus specials.
    /// Must be under 30 minutes so one observation cannot serve two hours.
    /// </summary>
    public TimeSpan MaxTimeOffset { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Observed speed from which an hour counts as windy. 12 km/h is the start of Beaufort force 3.</summary>
    public double WindyFromKmh { get; init; } = 12;

    /// <summary>A forecast direction within this angle of the observed one counts as a hit.</summary>
    public double DirectionHitDegrees { get; init; } = 45;
}

/// <summary>Compares past forecasts with station observations (the judge of phase 1).</summary>
public static class WindVerification
{
    /// <summary>
    /// Pairs each forecast hour with the observation of <paramref name="stationId"/> closest to it, within
    /// <see cref="VerificationOptions.MaxTimeOffset"/>. Hours without such an observation are left out.
    /// </summary>
    public static IReadOnlyList<MatchedHour> Match(
        string stationId,
        IEnumerable<WindSample> forecastHours,
        IEnumerable<WindObservation> observations,
        VerificationOptions? options = null)
    {
        options ??= new VerificationOptions();
        if (options.MaxTimeOffset < TimeSpan.Zero || options.MaxTimeOffset >= TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxTimeOffset, "MaxTimeOffset must be in [0, 30) minutes.");
        }

        var ofStation = observations.Where(o => o.StationId == stationId).OrderBy(o => o.Time).ToList();
        var matches = new List<MatchedHour>();
        foreach (var hour in forecastHours)
        {
            // Closest in time; on a tie the earlier one, which was measured before the forecast hour ended.
            var closest = ofStation
                .Where(o => (o.Time - hour.Time).Duration() <= options.MaxTimeOffset)
                .OrderBy(o => (o.Time - hour.Time).Duration())
                .ThenBy(o => o.Time)
                .FirstOrDefault();
            if (closest is not null)
            {
                matches.Add(new MatchedHour(stationId, hour, closest));
            }
        }
        return matches;
    }

    public static VerificationScore Score(IReadOnlyCollection<MatchedHour> hours, VerificationOptions? options = null)
    {
        options ??= new VerificationOptions();
        var directionErrors = hours
            .Select(h => h.DirectionErrorDegrees)
            .OfType<double>()
            .ToList();

        return new VerificationScore(
            hours.Count,
            hours.Count > 0 ? hours.Average(h => h.SpeedErrorKmh) : null,
            hours.Count > 0 ? hours.Average(h => Math.Abs(h.SpeedErrorKmh)) : null,
            directionErrors.Count,
            directionErrors.Count > 0 ? directionErrors.Average() : null,
            directionErrors.Count > 0
                ? directionErrors.Count(e => e <= options.DirectionHitDegrees) / (double)directionErrors.Count
                : null);
    }

    public static VerificationReport Report(IReadOnlyCollection<MatchedHour> hours, VerificationOptions? options = null)
    {
        options ??= new VerificationOptions();
        var windy = hours.Where(h => h.Observed.SpeedKmh >= options.WindyFromKmh).ToList();
        var light = hours.Where(h => h.Observed.SpeedKmh < options.WindyFromKmh).ToList();
        return new VerificationReport(Score(hours, options), Score(light, options), Score(windy, options));
    }
}
