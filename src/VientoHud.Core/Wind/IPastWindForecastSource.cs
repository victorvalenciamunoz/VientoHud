namespace VientoHud.Core.Wind;

/// <summary>How far ahead an archived forecast was issued.</summary>
public enum ForecastLead
{
    /// <summary>
    /// The first hours of each model run, stitched together: the forecast as it stood shortly before the valid time
    /// (within one run interval, a few hours). The "I'm leaving now" case.
    /// </summary>
    ShortRange,

    /// <summary>The forecast issued about 24 hours before the valid time. The "I'm riding tomorrow" case.</summary>
    DayAhead,
}

/// <summary>What a model forecast, in the past, for one location.</summary>
/// <param name="Requested">Location that was asked for.</param>
/// <param name="Model">Model name, provider-specific.</param>
/// <param name="GridPoint">Model grid cell used, or null when the model does not cover the location.</param>
/// <param name="Hours">Archived hours in chronological order. Hours the archive lacks are left out.</param>
public sealed record PastWindForecast(
    Coordinate Requested,
    string Model,
    ForecastLead Lead,
    Coordinate? GridPoint,
    IReadOnlyList<WindSample> Hours);

/// <summary>Archive of past wind forecasts, to compare them with what was observed.</summary>
public interface IPastWindForecastSource
{
    /// <summary>
    /// Archived hourly forecasts of one model for every hour with <c>from &lt;= hour &lt; to</c>,
    /// one result per location, in the same order as <paramref name="locations"/>.
    /// </summary>
    /// <exception cref="WindForecastException">The archive failed.</exception>
    Task<IReadOnlyList<PastWindForecast>> GetAsync(
        IReadOnlyList<Coordinate> locations,
        string model,
        ForecastLead lead,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
