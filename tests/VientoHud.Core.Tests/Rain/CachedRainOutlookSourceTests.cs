using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class CachedRainOutlookSourceTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private sealed class CountingSource : IRainOutlookSource
    {
        public List<(Coordinate Start, double ReachKm, DateTimeOffset Now, DateTimeOffset Until)> Requests { get; } = [];

        public Task<RainOutlook> ForecastAsync(
            Coordinate start, double reachKm, DateTimeOffset now, DateTimeOffset until, CancellationToken cancellationToken = default)
        {
            Requests.Add((start, reachKm, now, until));
            return Task.FromResult(new RainOutlook(now, null, null, null, null));
        }
    }

    [Fact]
    public async Task Answers_nearby_asks_within_ten_minutes_from_the_last_outlook_asked_a_little_beyond()
    {
        var inner = new CountingSource();
        var cache = new CachedRainOutlookSource(inner);

        var first = await cache.ForecastAsync(Leon, 30, Now, Now.AddHours(4));
        var second = await cache.ForecastAsync(Leon.Destination(90, 0.8), 35, Now.AddMinutes(9), Now.AddHours(4.5));

        Assert.Same(first, second);
        Assert.Single(inner.Requests);
        // Asked 10 km farther and 1 h later than needed.
        Assert.Equal(40, inner.Requests[0].ReachKm);
        Assert.Equal(Now.AddHours(5), inner.Requests[0].Until);
        Assert.Equal(1, cache.Requests);
    }

    [Fact]
    public async Task Asks_again_when_the_rider_moves_or_time_passes_or_the_ride_goes_beyond()
    {
        var inner = new CountingSource();
        var cache = new CachedRainOutlookSource(inner);
        await cache.ForecastAsync(Leon, 30, Now, Now.AddHours(4));

        await cache.ForecastAsync(Leon.Destination(0, 5), 30, Now, Now.AddHours(4));
        Assert.Equal(2, inner.Requests.Count);

        await cache.ForecastAsync(Leon.Destination(0, 5), 30, Now.AddMinutes(11), Now.AddHours(4));
        Assert.Equal(3, inner.Requests.Count);

        await cache.ForecastAsync(Leon.Destination(0, 5), 45, Now.AddMinutes(12), Now.AddHours(4));
        Assert.Equal(4, inner.Requests.Count);

        await cache.ForecastAsync(Leon.Destination(0, 5), 45, Now.AddMinutes(13), Now.AddHours(6.5));
        Assert.Equal(5, inner.Requests.Count);
    }

    [Fact]
    public async Task Never_serves_an_outlook_made_after_the_time_asked_for()
    {
        var inner = new CountingSource();
        var cache = new CachedRainOutlookSource(inner);
        await cache.ForecastAsync(Leon, 30, Now, Now.AddHours(4));

        await cache.ForecastAsync(Leon, 30, Now.AddMinutes(-5), Now.AddHours(4));

        Assert.Equal(2, inner.Requests.Count);
    }
}
