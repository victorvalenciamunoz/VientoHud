namespace VientoHud.Core.Wind;

/// <summary>Source of hourly wind forecasts.</summary>
public interface IWindForecastProvider
{
    /// <summary>
    /// Returns one sample per hour for <paramref name="hours"/> consecutive hours, starting at the hour
    /// that contains <paramref name="from"/>.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or has no data for some requested hour.</exception>
    Task<WindForecast> GetHourlyAsync(
        Coordinate location,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Like the single-location overload, for several locations at once: one forecast per location, in the same order
    /// as <paramref name="locations"/>.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or has no data for some location and hour.</exception>
    Task<IReadOnlyList<WindForecast>> GetHourlyAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default);
}
