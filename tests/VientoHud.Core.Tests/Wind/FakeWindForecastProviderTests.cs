using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Wind;

public class FakeWindForecastProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly Coordinate Madrid = new(40.4168, -3.7038);

    private static readonly WindSample[] Samples =
        [.. Enumerable.Range(0, 5).Select(h => new WindSample(Start.AddHours(h), h, h + 5, 270, "fake"))];

    [Fact]
    public async Task Returns_requested_hours_starting_at_the_hour_containing_from()
    {
        var provider = new FakeWindForecastProvider(Samples);

        var forecast = await provider.GetHourlyAsync(Madrid, Start.AddHours(1).AddMinutes(40), 3);

        Assert.Equal([Start.AddHours(1), Start.AddHours(2), Start.AddHours(3)], forecast.Hours.Select(s => s.Time));
    }

    [Fact]
    public async Task Throws_when_it_runs_out_of_hours()
    {
        var provider = new FakeWindForecastProvider(Samples);

        await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, Start.AddHours(3), 3));
    }
}
