using VientoHud.Core.Rain.Forecasts;

namespace VientoHud.Core.Tests.Support;

/// <summary>
/// Model rain and hourly chance from functions of place and time, and the requests received. Throws for the model
/// when <see cref="ModelFails"/> is set (a place outside the model).
/// </summary>
internal sealed class FakeRainForecastSource(
    Func<Coordinate, DateTimeOffset, double> rateMmPerHour,
    Func<DateTimeOffset, double>? chance = null) : IRainForecastSource, IRainProbabilitySource
{
    public bool ModelFails { get; init; }

    public List<IReadOnlyList<Coordinate>> ModelRequests { get; } = [];

    public int ProbabilityRequests { get; private set; }

    public Task<IReadOnlyList<RainForecastSeries>> GetAsync(
        IReadOnlyList<Coordinate> locations, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ModelRequests.Add(locations);
        if (ModelFails)
        {
            throw new RainForecastException("No data is available for this location");
        }
        var quarter = TimeSpan.FromMinutes(15);
        var first = new DateTimeOffset((from.UtcTicks / quarter.Ticks + 1) * quarter.Ticks, TimeSpan.Zero);
        IReadOnlyList<RainForecastSeries> series = locations.Select(l => new RainForecastSeries(l, l, "fake", quarter,
            Enumerable.Range(0, (int)((to - first) / quarter) + 1)
                .Select(i => first + i * quarter)
                .Select(end => new RainForecastSample(end, rateMmPerHour(l, end)))
                .ToList())).ToList();
        return Task.FromResult(series);
    }

    public Task<IReadOnlyList<RainProbabilitySeries>> GetProbabilityAsync(
        IReadOnlyList<Coordinate> locations, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        ProbabilityRequests++;
        var hour = TimeSpan.FromHours(1);
        var first = new DateTimeOffset((from.UtcTicks / hour.Ticks + 1) * hour.Ticks, TimeSpan.Zero);
        IReadOnlyList<RainProbabilitySeries> series = locations.Select(l => new RainProbabilitySeries(l, l, "fake", hour,
            Enumerable.Range(0, (int)((to - first) / hour) + 1)
                .Select(i => first + i * hour)
                .Select(end => new RainProbabilitySample(end, chance?.Invoke(end) ?? 0))
                .ToList())).ToList();
        return Task.FromResult(series);
    }
}
