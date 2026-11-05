namespace VientoHud.Core.Routing;

/// <summary>How long a ride is, from the time the rider has (the console's <c>--hours</c>).</summary>
public static class RideDistance
{
    /// <summary>
    /// Average speed assumed when none is given: a steady road-bike ride on mixed terrain. Riders override it with their
    /// own; phase 3 will adjust it for the wind.
    /// </summary>
    public const double DefaultAverageSpeedKmh = 25;

    /// <summary>Km ridden in <paramref name="duration"/> at <paramref name="averageSpeedKmh"/>.</summary>
    public static double FromDuration(TimeSpan duration, double averageSpeedKmh = DefaultAverageSpeedKmh)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be positive.");
        }
        if (!double.IsFinite(averageSpeedKmh) || averageSpeedKmh is < 5 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(averageSpeedKmh), averageSpeedKmh, "Average speed must be 5-60 km/h.");
        }
        return duration.TotalHours * averageSpeedKmh;
    }
}
