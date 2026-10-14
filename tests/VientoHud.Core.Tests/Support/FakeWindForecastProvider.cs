using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Support;

/// <summary>In-memory provider for tests of code that consumes wind forecasts.</summary>
internal sealed class FakeWindForecastProvider(IReadOnlyList<WindSample> samples) : IWindForecastProvider
{
    public const string Attribution = "Fake wind data";

    public Task<WindForecast> GetHourlyAsync(
        Coordinate location,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default)
    {
        var start = from.ToUniversalTime();
        start = new DateTimeOffset(start.Year, start.Month, start.Day, start.Hour, 0, 0, TimeSpan.Zero);
        var selected = samples.Where(s => s.Time >= start).Take(hours).ToList();
        if (selected.Count < hours)
        {
            throw new WindForecastException($"Fake provider has only {selected.Count} of {hours} hours from {start:u}.");
        }
        return Task.FromResult(new WindForecast(location, location, 0, selected, Attribution));
    }
}
