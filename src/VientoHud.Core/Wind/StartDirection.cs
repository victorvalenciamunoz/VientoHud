namespace VientoHud.Core.Wind;

public enum StartAdviceKind
{
    /// <summary>Start heading into the mean wind, so the way back is downwind.</summary>
    HeadIntoWind,

    /// <summary>The wind is too weak to matter: any direction is fine.</summary>
    Calm,

    /// <summary>The wind changes direction too much over the period: no direction is clearly better.</summary>
    Variable,
}

/// <summary>Recommendation of which way to start a loop ride.</summary>
/// <param name="Kind">What the recommendation is.</param>
/// <param name="StartBearingDegrees">
/// Route bearing: degrees the rider should head TO, clockwise from north. Only set for
/// <see cref="StartAdviceKind.HeadIntoWind"/>.
/// </param>
/// <param name="MeanWindFromDegrees">
/// Meteorological convention: direction the mean wind blows FROM. Null when the hourly winds cancel out exactly.
/// </param>
/// <param name="MeanVectorSpeedKmh">Speed of the vector-averaged wind, in km/h.</param>
/// <param name="MeanSpeedKmh">Plain average of the hourly speeds, in km/h, regardless of direction.</param>
/// <param name="Steadiness">
/// <see cref="MeanVectorSpeedKmh"/> / <see cref="MeanSpeedKmh"/>, from 0 (directions cancel out) to 1 (constant direction).
/// </param>
/// <param name="Hours">Number of hourly samples the recommendation is based on.</param>
public sealed record StartDirectionAdvice(
    StartAdviceKind Kind,
    double? StartBearingDegrees,
    double? MeanWindFromDegrees,
    double MeanVectorSpeedKmh,
    double MeanSpeedKmh,
    double Steadiness,
    int Hours);

/// <summary>Thresholds for <see cref="StartDirection.Recommend"/>.</summary>
public sealed record StartDirectionOptions
{
    /// <summary>
    /// Below this mean speed the wind is ignored. 5 km/h is the top of Beaufort force 1, negligible next to a road
    /// bike's 25-30 km/h. To be tuned with data in phase 3.
    /// </summary>
    public double CalmBelowKmh { get; init; } = 5;

    /// <summary>
    /// Below this steadiness the wind is considered variable. 0.7 is roughly hourly directions spread over ±80°
    /// around the mean. To be tuned with data in phase 3.
    /// </summary>
    public double MinSteadiness { get; init; } = 0.7;
}

/// <summary>
/// Basic rule for loop rides: start into the wind so the second half, when the rider is tired, is downwind.
/// </summary>
public static class StartDirection
{
    private const double CancelledOutKmh = 1e-9;

    /// <param name="hours">Forecast for the hours of the ride.</param>
    public static StartDirectionAdvice Recommend(IReadOnlyList<WindSample> hours, StartDirectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(hours);
        if (hours.Count == 0)
        {
            throw new ArgumentException("At least one hourly sample is needed.", nameof(hours));
        }
        options ??= new StartDirectionOptions();

        // Average the wind as vectors pointing to where it comes FROM, so 350° and 10° average to 0°, not 180°,
        // and stronger hours weigh more.
        var east = hours.Average(h => h.SpeedKmh * Math.Sin(ToRadians(h.DirectionFromDegrees)));
        var north = hours.Average(h => h.SpeedKmh * Math.Cos(ToRadians(h.DirectionFromDegrees)));
        var vectorSpeed = Math.Sqrt(east * east + north * north);
        var meanSpeed = hours.Average(h => h.SpeedKmh);

        // Opposite winds leave floating-point residue (sin 180° is 1.2e-16, not 0) whose angle is meaningless.
        double? meanFrom = vectorSpeed > CancelledOutKmh ? Normalize(ToDegrees(Math.Atan2(east, north))) : null;
        if (meanFrom is null)
        {
            vectorSpeed = 0;
        }
        var steadiness = meanSpeed > 0 ? Math.Min(1, vectorSpeed / meanSpeed) : 0;

        var kind = meanSpeed < options.CalmBelowKmh ? StartAdviceKind.Calm
            : steadiness < options.MinSteadiness ? StartAdviceKind.Variable
            : StartAdviceKind.HeadIntoWind;

        // Heading into the wind means going TO the direction it comes FROM: same angle, different convention.
        var bearing = kind == StartAdviceKind.HeadIntoWind ? meanFrom : null;

        return new StartDirectionAdvice(kind, bearing, meanFrom, vectorSpeed, meanSpeed, steadiness, hours.Count);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;

    private static double Normalize(double degrees) => (degrees % 360 + 360) % 360;
}
