namespace VientoHud.Core.Rain.Forecasts;

/// <summary>Model forecasts of rain every few minutes: the latest one, or past ones for judging.</summary>
public interface IRainForecastSource
{
    /// <summary>
    /// The model's rain at each location for every interval ending in <c>(from, to]</c>, one series per location in the
    /// same order. Intervals the model has no value for are left out.
    /// </summary>
    /// <exception cref="RainForecastException">The source failed or the model does not cover a location.</exception>
    Task<IReadOnlyList<RainForecastSeries>> GetAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Rain in one interval of a model forecast.</summary>
/// <param name="End">End of the interval, in UTC; it starts <see cref="RainForecastSeries.Interval"/> before.</param>
/// <param name="MmPerHour">Mean rain rate over the interval in mm/h.</param>
public sealed record RainForecastSample(DateTimeOffset End, double MmPerHour);

/// <summary>A model's rain at a location, interval by interval.</summary>
/// <param name="GridPoint">Model cell centre the values belong to.</param>
public sealed record RainForecastSeries(
    Coordinate Location,
    Coordinate GridPoint,
    string Model,
    TimeSpan Interval,
    IReadOnlyList<RainForecastSample> Samples)
{
    /// <summary>
    /// Rate of the interval that contains <paramref name="time"/> (the one ending at it, or the next end after it), or
    /// null when the model has no value there. A radar frame at T covers the minutes before T, and so does this.
    /// </summary>
    public double? RateAt(DateTimeOffset time)
    {
        var ticks = Interval.Ticks;
        var end = new DateTimeOffset((time.UtcTicks + ticks - 1) / ticks * ticks, TimeSpan.Zero);
        return Samples.FirstOrDefault(s => s.End == end)?.MmPerHour;
    }
}

/// <summary>Hourly chance of rain from a model, the fallback beyond the AROME grid or horizon (DECISIONS 033).</summary>
public interface IRainProbabilitySource
{
    /// <summary>
    /// The model's chance of rain at each location for every interval ending in <c>(from, to]</c>, one series per
    /// location in the same order.
    /// </summary>
    /// <exception cref="RainForecastException">The source failed.</exception>
    Task<IReadOnlyList<RainProbabilitySeries>> GetProbabilityAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Chance of rain in one interval (Open-Meteo: more than 0.1 mm in it), in [0, 1].</summary>
public sealed record RainProbabilitySample(DateTimeOffset End, double Probability);

/// <summary>A model's chance of rain at a location, interval by interval.</summary>
public sealed record RainProbabilitySeries(
    Coordinate Location,
    Coordinate GridPoint,
    string Model,
    TimeSpan Interval,
    IReadOnlyList<RainProbabilitySample> Samples)
{
    /// <summary>Chance of the interval containing <paramref name="time"/>, or null when there is none.</summary>
    public double? ProbabilityAt(DateTimeOffset time)
    {
        var ticks = Interval.Ticks;
        var end = new DateTimeOffset((time.UtcTicks + ticks - 1) / ticks * ticks, TimeSpan.Zero);
        return Samples.FirstOrDefault(s => s.End == end)?.Probability;
    }
}

/// <summary>A rain forecast could not be obtained.</summary>
public sealed class RainForecastException(string message, Exception? innerException = null) : Exception(message, innerException);
