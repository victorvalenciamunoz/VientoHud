using System.Net;
using System.Net.Http.Headers;
using VientoHud.Core.Net;
using VientoHud.Core.Rain;
using VientoHud.Core.Replay;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Replay;

public class ReplayDayTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vientohud-replay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("2026-10-19T10:00Z;45.1000;1.2340", true)]
    [InlineData("2026-10-19T12:00+02:00; 45.1 ; 1.234", true)]
    [InlineData("2026-10-19T10:00Z;45.1", false)]
    [InlineData("ayer;45.1;1.2", false)]
    [InlineData("2026-10-19T10:00Z;95;1.2", false)]
    [InlineData(null, false)]
    public void Reads_the_moment_and_the_place(string? text, bool ok)
    {
        Assert.Equal(ok, ReplayDay.TryParse(text, out var day));
        if (ok)
        {
            Assert.Equal(Noon, day.Start);
            Assert.Equal(TimeSpan.Zero, day.Start.Offset);
            Assert.Equal(45.1, day.Place.Latitude, 6);
            Assert.Equal(1.234, day.Place.Longitude, 6);
        }
    }

    [Fact]
    public async Task The_clock_starts_at_the_moment_and_runs_on_and_the_rider_is_there()
    {
        var real = new Clock(new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));
        var day = new ReplayDay(Noon, new Coordinate(45.1, 1.234));

        var clock = day.Clock(real);
        Assert.Equal(Noon, clock.GetUtcNow());
        real.Now += TimeSpan.FromMinutes(7);
        Assert.Equal(Noon.AddMinutes(7), clock.GetUtcNow());

        var fix = await day.Location(clock).GetCurrentAsync();
        Assert.Equal(day.Place, fix.Position);
        Assert.Equal(Noon.AddMinutes(7), fix.Timestamp);
    }

    [Theory]
    [InlineData(10, 0, 9)]   // 09:20 out: the 09:00 run
    [InlineData(9, 30, 6)]   // 08:50: the 06:00 run (the 09:00 one is not out yet)
    [InlineData(0, 20, 21)]  // 23:40 the day before: its 21:00 run
    public void Uses_the_last_arome_run_already_out(int hour, int minute, int runHour)
    {
        var day = new ReplayDay(new DateTimeOffset(2026, 10, 19, hour, minute, 0, TimeSpan.Zero), new Coordinate(45.1, 1.234));

        Assert.Equal(runHour, day.LastRun.Hour);
        Assert.Equal(0, day.LastRun.Minute);
        Assert.True(day.LastRun <= day.Start - ReplayDay.RunDelay);
    }

    private sealed class FakeRadar : IRadarSource
    {
        public List<DateTimeOffset> Asked { get; } = [];

        public TimeSpan FrameInterval => TimeSpan.FromMinutes(5);

        public string Attribution => "fake";

        public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The live radar must not be asked what is latest.");

        public Task<RainGrid?> GetFrameAsync(DateTimeOffset time, Coordinate center, double radiusKm, CancellationToken cancellationToken = default)
        {
            Asked.Add(time);
            return Task.FromResult<RainGrid?>(null);
        }
    }

    [Fact]
    public async Task The_radar_sees_nothing_after_the_moment()
    {
        var real = new Clock(new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero));
        var day = new ReplayDay(Noon.AddMinutes(12), new Coordinate(45.1, 1.234));
        var inner = new FakeRadar();
        var radar = day.Radar(inner, day.Clock(real));

        // 10:12 minus the 5 min delay, on the 5 min grid: 10:05.
        Assert.Equal(Noon.AddMinutes(5), await radar.GetLatestFrameTimeAsync());
        Assert.Null(await radar.GetFrameAsync(Noon.AddMinutes(10), day.Place, 150));
        await radar.GetFrameAsync(Noon.AddMinutes(5), day.Place, 150);
        Assert.Equal([Noon.AddMinutes(5)], inner.Asked);
    }

    private sealed class FakeArchive(Func<string, int> hoursOf) : IPastWindForecastSource
    {
        public List<string> Models { get; } = [];

        public Task<IReadOnlyList<PastWindForecast>> GetAsync(
            IReadOnlyList<Coordinate> locations, string model, ForecastLead lead, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            Models.Add(model);
            IReadOnlyList<PastWindForecast> result = locations.Select(l => new PastWindForecast(l, model, lead, l,
                Enumerable.Range(0, hoursOf(model)).Select(h => new WindSample(from.AddHours(h), 10, 15, 270, model)).ToList())).ToList();
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task The_archived_wind_falls_back_to_the_best_match_when_arome_lacks_hours()
    {
        var archive = new FakeArchive(model => model == "best_match" ? 3 : 2);
        var wind = new ArchivedWindForecast(archive, ForecastLead.ShortRange);

        var forecast = await wind.GetHourlyAsync(new Coordinate(45.1, 1.234), Noon, 3);

        Assert.Equal(["meteofrance_arome_france", "best_match"], archive.Models);
        Assert.Equal(3, forecast.Hours.Count);
    }

    [Fact]
    public async Task The_archived_wind_fails_when_no_model_has_every_hour()
    {
        var wind = new ArchivedWindForecast(new FakeArchive(_ => 1), ForecastLead.ShortRange);

        await Assert.ThrowsAsync<WindForecastException>(() => wind.GetHourlyAsync(new Coordinate(45.1, 1.234), Noon, 3));
    }

    private sealed class Network : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/fail", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") });
            }
            var range = request.Headers.Range?.ToString() ?? "all";
            var content = new StringContent($"answer {Calls} for {range}");
            if (request.Headers.Range is { } r)
            {
                content.Headers.ContentRange = new ContentRangeHeaderValue(r.Ranges.First().From!.Value, r.Ranges.First().To!.Value, 1000);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task Recorded_answers_come_back_without_the_network()
    {
        var network = new Network();
        var recorder = new RecordingHandler(_folder) { InnerHandler = network };
        using var http = new HttpClient(recorder);

        var first = await http.GetStringAsync("https://example.test/a?x=1");
        var again = await http.GetStringAsync("https://example.test/a?x=1");
        var other = await http.GetStringAsync("https://example.test/a?x=2");
        using var ranged = new HttpRequestMessage(HttpMethod.Get, "https://example.test/a?x=1");
        ranged.Headers.Range = new RangeHeaderValue(0, 99);
        using var partial = await http.SendAsync(ranged);
        using var rangedAgain = new HttpRequestMessage(HttpMethod.Get, "https://example.test/a?x=1");
        rangedAgain.Headers.Range = new RangeHeaderValue(0, 99);
        using var partialAgain = await http.SendAsync(rangedAgain);

        Assert.Equal("answer 1 for all", first);
        Assert.Equal(first, again);
        Assert.Equal("answer 2 for all", other);
        Assert.Equal(HttpStatusCode.PartialContent, partialAgain.StatusCode);
        Assert.Equal(await partial.Content.ReadAsStringAsync(), await partialAgain.Content.ReadAsStringAsync());
        Assert.Equal(1000, partialAgain.Content.Headers.ContentRange!.Length);
        Assert.Equal(3, network.Calls);
        Assert.Equal(2, recorder.Replayed);
        Assert.Equal(3, recorder.Recorded);
    }

    [Fact]
    public async Task A_failure_is_not_recorded()
    {
        var network = new Network();
        using var http = new HttpClient(new RecordingHandler(_folder) { InnerHandler = network });

        using var one = await http.GetAsync("https://example.test/fail");
        using var two = await http.GetAsync("https://example.test/fail");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, two.StatusCode);
        Assert.Equal(2, network.Calls);
    }
}
