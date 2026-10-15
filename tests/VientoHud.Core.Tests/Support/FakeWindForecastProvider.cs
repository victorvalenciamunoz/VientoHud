using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Support;

/// <summary>In-memory provider for tests of code that consumes wind forecasts: the same samples everywhere.</summary>
internal sealed class FakeWindForecastProvider(IReadOnlyList<WindSample> samples) : IWindForecastProvider
{
    public const string Attribution = "Fake wind data";

    /// <summary>The location lists asked for, one per call.</summary>
    public List<IReadOnlyList<Coordinate>> Requests { get; } = [];

    public async Task<WindForecast> GetHourlyAsync(
        Coordinate location,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default) =>
        (await GetHourlyAsync([location], from, hours, cancellationToken))[0];

    public Task<IReadOnlyList<WindForecast>> GetHourlyAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(locations);
        var start = from.ToUniversalTime();
        start = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero);
        var selected = samples.Where(s => s.Time >= start).Take(hours).ToList();
        if (selected.Count < hours)
        {
            throw new WindForecastException($"Fake provider has only {selected.Count} of {hours} hours from {start:u}.");
        }
        IReadOnlyList<WindForecast> forecasts = locations
            .Select(location => new WindForecast(location, location, 0, selected, Attribution))
            .ToList();
        return Task.FromResult(forecasts);
    }
}
