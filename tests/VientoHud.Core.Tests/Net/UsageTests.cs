using System.Net;
using VientoHud.Core.Net;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Net;

public class UsageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vientohud-usage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Noon = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://tile.thunderforest.com/cycle/12/2059/1473.png?apikey=x", UsageService.Thunderforest, 1)]
    [InlineData("https://tile.openstreetmap.org/12/2059/1473.png", UsageService.OpenStreetMap, 1)]
    [InlineData("https://brouter.de/brouter?lonlats=1,2|3,4&profile=x", UsageService.BRouter, 1)]
    [InlineData("http://10.0.2.2:17777/brouter?lonlats=1,2|3,4", UsageService.BRouter, 1)]
    [InlineData("https://s3.waw3-1.cloudferro.com/openradar-24h/2026/10/26/OPERA/COMP/RATE.h5", UsageService.OperaRadar, 1)]
    [InlineData("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/2059/1473.png", UsageService.Terrain, 1)]
    [InlineData("https://mesonet.agron.iastate.edu/cgi-bin/request/asos.py", UsageService.Other, 1)]
    // Open-Meteo: one call per location; three variables over a day weigh no more.
    [InlineData("https://api.open-meteo.com/v1/forecast?latitude=45.0&longitude=1.0&hourly=a,b,c&start_hour=2026-10-26T08:00&end_hour=2026-10-26T20:00", UsageService.OpenMeteo, 1)]
    [InlineData("https://api.open-meteo.com/v1/forecast?latitude=45.0,45.1,45.2&longitude=1.0,1.0,1.0&hourly=a,b,c&start_hour=2026-10-26T08:00&end_hour=2026-10-26T20:00", UsageService.OpenMeteo, 3)]
    // More than 10 variables or two weeks count more, fractionally (Open-Meteo's pricing page).
    [InlineData("https://api.open-meteo.com/v1/forecast?latitude=45.0&longitude=1.0&hourly=a,b,c,d,e,f,g,h,i,j,k,l,m,n,o&forecast_days=14", UsageService.OpenMeteo, 1.5)]
    [InlineData("https://historical-forecast-api.open-meteo.com/v1/forecast?latitude=45.0,47.0&longitude=1.0,2.0&hourly=a&start_date=2026-09-01&end_date=2026-09-28", UsageService.OpenMeteo, 4)]
    public void Tells_the_service_and_the_weight_of_each_request(string url, UsageService service, double weight)
    {
        var (s, w) = UsageClassifier.Classify(new Uri(url));

        Assert.Equal(service, s);
        Assert.Equal(weight, w, 6);
    }

    [Fact]
    public void Counts_by_minute_hour_day_and_month_against_the_limits()
    {
        var clock = new Clock(Noon);
        var meter = new UsageMeter(null, clock);

        meter.Record(UsageService.OpenMeteo, 25, 1000);
        clock.Now = Noon.AddMinutes(30);
        meter.Record(UsageService.OpenMeteo, 300, 2000);
        meter.Record(UsageService.Thunderforest, 1, 15_000);
        // Yesterday counts for the month, not for today.
        clock.Now = Noon.AddDays(-1);
        meter.Record(UsageService.OpenMeteo, 100, 500);
        clock.Now = Noon.AddMinutes(30.5);

        var rows = meter.Report();
        var meteo = rows.Single(r => r.Service == UsageService.OpenMeteo);
        Assert.Equal(300, meteo.LastMinute.Calls);
        Assert.Equal(325, meteo.LastHour.Calls);
        Assert.Equal(325, meteo.Today.Calls);
        Assert.Equal(3000, meteo.Today.Bytes);
        Assert.Equal(425, meteo.Month.Calls);
        // The busiest span: 300 in the last minute out of 600.
        Assert.Equal(0.5, meteo.Busiest!.Value, 6);
        var tiles = rows.Single(r => r.Service == UsageService.Thunderforest);
        Assert.Equal(1 / 150_000.0, tiles.Busiest!.Value, 9);
        Assert.Null(rows.Single(r => r.Service == UsageService.Terrain).Busiest);
        Assert.Equal(Enum.GetValues<UsageService>().Length, rows.Count);
    }

    [Fact]
    public void A_new_month_starts_from_zero()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 31, 23, 45, 0, TimeSpan.Zero));
        var meter = new UsageMeter(null, clock);
        meter.Record(UsageService.Thunderforest, 1000, 0);

        clock.Now = new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.Zero);
        var row = meter.Report().Single(r => r.Service == UsageService.Thunderforest);

        Assert.Equal(0, row.Month.Calls);
        Assert.Equal(0, row.Today.Calls);
        Assert.Equal(1000, row.LastHour.Calls);
    }

    [Fact]
    public void Keeps_the_days_in_a_file_and_drops_the_old_ones()
    {
        var path = Path.Combine(_directory, "usage.json");
        var clock = new Clock(Noon.AddDays(-70));
        var first = new UsageMeter(path, clock);
        first.Record(UsageService.BRouter, 1, 50_000);
        clock.Now = Noon;
        first.Record(UsageService.BRouter, 2, 100_000);
        first.Save();

        var again = new UsageMeter(path, clock);
        var row = again.Report().Single(r => r.Service == UsageService.BRouter);

        Assert.Equal(2, row.Today.Calls);
        Assert.Equal(100_000, row.Today.Bytes);
        Assert.DoesNotContain("2026-08-17", File.ReadAllText(path));
        // The last minute and hour are not kept: a restart starts them empty.
        Assert.Equal(0, row.LastMinute.Calls);
    }

    [Fact]
    public void A_broken_file_starts_the_counts_again()
    {
        var path = Path.Combine(_directory, "usage.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "{ not json");

        var meter = new UsageMeter(path, new Clock(Noon));

        Assert.All(meter.Report(), r => Assert.Equal(0, r.Month.Calls));
    }

    [Fact]
    public void Writes_the_lines_of_the_panel()
    {
        var clock = new Clock(Noon);
        var meter = new UsageMeter(null, clock);
        meter.Record(UsageService.OpenMeteo, 325, 3 * 1024 * 1024 + 200_000);
        meter.Record(UsageService.Thunderforest, 970, 0);
        meter.Record(UsageService.OperaRadar, 4, 8 * 1024 * 1024);

        var used = VientoHud.Core.Text.UsageText.Used(meter.Report());

        // Busiest first (Open-Meteo 3 % of the day, Thunderforest 0,6 % of the month), then those without a limit.
        Assert.Equal([UsageService.OpenMeteo, UsageService.Thunderforest, UsageService.OperaRadar], used.Select(r => r.Service));
        Assert.Equal("Hoy 325 de 10.000 llamadas (3 %) · mes 325 de 300.000 (0 %) · 3,2 MB", VientoHud.Core.Text.UsageText.Line(used[0]));
        Assert.Equal("Hoy 970 teselas · mes 970 de 150.000 (1 %)", VientoHud.Core.Text.UsageText.Line(used[1]));
        Assert.Equal("Hoy 4 peticiones · mes 4 · 8,0 MB", VientoHud.Core.Text.UsageText.Line(used[2]));
    }

    [Fact]
    public async Task The_shared_client_counts_every_attempt_and_the_bytes()
    {
        var meter = new UsageMeter(null, new Clock(Noon));
        var attempts = 0;
        var server = new ScriptedHttpMessageHandler(_ => ++attempts == 1
            ? (HttpStatusCode.ServiceUnavailable, "")
            : (HttpStatusCode.OK, new string('x', 1234)));
        using var client = new HttpClient(ResilientHttp.CreateHandler(server, firstDelay: TimeSpan.Zero, meter: meter));

        var body = await client.GetStringAsync("https://api.open-meteo.com/v1/forecast?latitude=1,2&longitude=3,4&hourly=a");

        Assert.Equal(1234, body.Length);
        var row = meter.Report().Single(r => r.Service == UsageService.OpenMeteo);
        // The 503 and the retry: two requests of two locations each.
        Assert.Equal(4, row.Today.Calls);
        Assert.Equal(1234, row.Today.Bytes);
    }
}
