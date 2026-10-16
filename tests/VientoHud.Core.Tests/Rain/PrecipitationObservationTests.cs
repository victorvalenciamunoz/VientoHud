using System.Net;
using VientoHud.Core.Rain.Observations;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class PrecipitationObservationTests
{
    private static readonly DateTimeOffset At12 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset At15 = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("-RA", true)]
    [InlineData("RA BR", true)]
    [InlineData("+SHRA", true)]
    [InlineData("TS SHRA", true)]
    [InlineData("+TSGRRA", true)]
    [InlineData("-DZRA", true)]
    [InlineData("DZ", true)]
    [InlineData("-SN", true)]
    [InlineData("FZRA", true)]
    [InlineData("", false)]
    [InlineData("BR", false)]
    [InlineData("BCFG", false)]
    [InlineData("TS", false)] // thunder heard, no rain at the station
    [InlineData("VCSH", false)] // showers in the vicinity
    [InlineData("VCTS VCSH", false)]
    [InlineData("RERA", false)] // recent rain, not now
    [InlineData("BLSN", false)] // snow blown from the ground
    [InlineData("TS VCSH", false)]
    public void Reads_precipitation_from_metar_weather(string codes, bool expected)
    {
        Assert.Equal(expected, MetarWeather.HasPrecipitation(codes));
    }

    private static (IemPrecipitationObservationSource Source, StubHttpMessageHandler Handler) Create(
        HttpStatusCode status = HttpStatusCode.OK, string? body = null)
    {
        var handler = new StubHttpMessageHandler(
            status, body ?? Fixture.Read("Iem/asos-wxcodes-LEMD-LELN-2026-10-06-12-to-15.csv"));
        return (new IemPrecipitationObservationSource(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Reads_the_reports_of_both_stations_in_order()
    {
        // Recorded 2026-10-16: LEMD and LELN on 6 Oct 12:00-15:00 UTC (storm at Barajas from 14:06).
        var (source, handler) = Create();

        var reports = await source.GetReportsAsync(["LEMD", "LELN"], At12, At15);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("station=LEMD&station=LELN&data=wxcodes", query);
        Assert.Contains("sts=2026-10-06T12:00:00Z&ets=2026-10-06T15:00:00Z", query);
        Assert.Equal(16, reports.Count);
        Assert.Equal(10, reports.Count(r => r.StationId == "LEMD"));
        // By station (LELN before LEMD), then by time.
        Assert.True(reports.Take(6).All(r => r.StationId == "LELN"));

        var lemd = reports.Where(r => r.StationId == "LEMD").ToList();
        Assert.Equal(
            [false, false, false, false, false, true, true, true, true, false],
            lemd.Select(r => r.Precipitation));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 14, 6, 0, TimeSpan.Zero), lemd[5].Time);
        Assert.Equal("TS SHRA", lemd[5].Codes);
        Assert.All(reports.Where(r => r.StationId == "LELN"), r => Assert.Equal("", r.Codes));
    }

    [Fact]
    public async Task Keeps_only_the_range_asked_for()
    {
        var (source, _) = Create();

        var reports = await source.GetReportsAsync(["LEMD", "LELN"], At12.AddHours(2), At12.AddHours(2).AddMinutes(20));

        Assert.Equal(
            ["LELN 14:00", "LEMD 14:00", "LEMD 14:06", "LEMD 14:12"],
            reports.Select(r => $"{r.StationId} {r.Time:HH:mm}"));
    }

    [Fact]
    public async Task Failures_are_observation_errors()
    {
        var (limited, _) = Create(HttpStatusCode.TooManyRequests, "slow down");
        var offline = new IemPrecipitationObservationSource(new HttpClient(new FailingHttpMessageHandler()));

        var error = await Assert.ThrowsAsync<PrecipitationObservationException>(
            () => limited.GetReportsAsync(["LEMD"], At12, At15));
        Assert.Contains("one request per second", error.Message);
        await Assert.ThrowsAsync<PrecipitationObservationException>(() => offline.GetReportsAsync(["LEMD"], At12, At15));
        var (wrong, _) = Create(body: "station,valid\nLEMD,2026-10-06 12:00");
        await Assert.ThrowsAsync<PrecipitationObservationException>(() => wrong.GetReportsAsync(["LEMD"], At12, At15));
    }

    [Fact]
    public async Task No_stations_no_request()
    {
        var (source, handler) = Create();

        Assert.Empty(await source.GetReportsAsync([], At12, At15));
        Assert.Empty(handler.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => source.GetReportsAsync(["LEMD"], At15, At12));
    }
}
