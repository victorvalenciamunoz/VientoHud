using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>Where to head, before any route (phase 7d).</summary>
/// <param name="Wind">The wind at the start over the hours of the ride, and which way it says to head.</param>
/// <param name="Fan">The sector to draw on the map (<see cref="DirectionFan"/>); null when the wind gives no direction.</param>
/// <param name="Hours">Hours of forecast looked at.</param>
public sealed record DirectionAdvice(StartDirectionAdvice Wind, IReadOnlyList<Coordinate>? Fan, int Hours);

/// <summary>
/// Looks at the wind at the start for the hours a ride of the target distance lasts and says which way to head
/// (<see cref="StartDirection"/>). The first thing the planner does and the first thing the app shows.
/// </summary>
public sealed class DirectionAdvisor(IWindForecastProvider forecasts)
{
    /// <summary>The hours a ride spans: from the hour it starts to the one it should end in.</summary>
    public static int HoursOf(double targetKm, DateTimeOffset departure, double averageSpeedKmh) =>
        Math.Max(1, (int)Math.Ceiling(targetKm / averageSpeedKmh + departure.Minute / 60.0));

    /// <param name="departure">When the ride starts, in UTC.</param>
    /// <exception cref="WindForecastException">The forecast failed.</exception>
    public async Task<DirectionAdvice> AdviseAsync(
        Coordinate start,
        double targetKm,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        CancellationToken cancellationToken = default)
    {
        if (departure.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Departure must be in UTC.", nameof(departure));
        }
        var hours = HoursOf(targetKm, departure, averageSpeedKmh);
        var forecast = await forecasts.GetHourlyAsync(start, departure, hours, cancellationToken);
        var wind = StartDirection.Recommend(forecast.Hours);
        return new DirectionAdvice(wind, DirectionFan.For(wind, start, targetKm), hours);
    }
}
