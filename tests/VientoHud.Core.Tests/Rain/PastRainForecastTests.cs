using System.Net;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class PastRainForecastTests
{
    private static readonly Coordinate Barajas = new(40.4936, -3.5668);
    private static readonly Coordinate Leon = new(42.589, -5.6556);
    private static readonly DateTimeOffset Day = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    private static (OpenMeteoPastRainForecastSource Source, StubHttpMessageHandler Handler) Create(
        string fixture, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpMessageHandler(status, Fixture.Read($"OpenMeteo/{fixture}"));
        return (new OpenMeteoPastRainForecastSource(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Reads_15_minute_rain_for_several_places_in_one_request()
    {
        // Recorded 2026-10-16: AROME HD 15 min on 6 Oct at Barajas and León. At Barajas 3.1 mm fell from 16:00 to
        // 16:15 (the METAR gave +TSRA at 16:15).
        var (source, handler) = Create("arome-hd-15min-lemd-leln-2026-10-06.json");

        var series = await source.GetAsync([Barajas, Leon], Day, Day.AddDays(1));

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.Contains("latitude=40.4936,42.589&longitude=-3.5668,-5.6556", query);
        Assert.Contains("minutely_15=precipitation&models=meteofrance_arome_france_hd_15min", query);
        Assert.Contains("start_date=2026-10-06&end_date=2026-10-07", query);
        Assert.Equal(2, series.Count);
        Assert.Equal(Barajas, series[0].Location);
        Assert.Equal(Leon, series[1].Location);
        Assert.Equal(40.49, series[0].GridPoint.Latitude, tolerance: 0.01);
        Assert.Equal(TimeSpan.FromMinutes(15), series[0].Interval);
        Assert.Equal("meteofrance_arome_france_hd_15min", series[0].Model);

        var storm = series[0].Samples.Single(s => s.End == Day.AddHours(16).AddMinutes(15));
        Assert.Equal(12.4, storm.MmPerHour, tolerance: 1e-9);
    }

    [Fact]
    public async Task The_rate_at_a_time_is_that_of_the_interval_containing_it()
    {
        var (source, _) = Create("arome-hd-15min-lemd-2026-10-06.json");

        var barajas = (await source.GetAsync([Barajas], Day, Day.AddDays(1))).Single();

        var at1615 = Day.AddHours(16).AddMinutes(15);
        Assert.Equal(12.4, barajas.RateAt(at1615)!.Value, tolerance: 1e-9);
        Assert.Equal(12.4, barajas.RateAt(at1615.AddMinutes(-5))!.Value, tolerance: 1e-9);
        Assert.Equal(1.2, barajas.RateAt(at1615.AddMinutes(1))!.Value, tolerance: 1e-9);
        Assert.Equal(0, barajas.RateAt(Day.AddHours(14))!.Value);
        Assert.Null(barajas.RateAt(Day.AddDays(3)));
    }

    [Fact]
    public async Task Keeps_only_intervals_ending_in_the_range()
    {
        var (source, _) = Create("arome-hd-15min-lemd-2026-10-06.json");

        var barajas = (await source.GetAsync([Barajas], Day.AddHours(16), Day.AddHours(17))).Single();

        Assert.Equal(["16:15", "16:30", "16:45", "17:00"], barajas.Samples.Select(s => s.End.ToString("HH:mm")));
    }

    [Fact]
    public async Task A_place_outside_the_model_is_a_forecast_error_with_the_reason()
    {
        var (source, _) = Create("error-arome-hd-15min-sevilla.json", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<RainForecastException>(
            () => source.GetAsync([new Coordinate(37.3891, -5.9845)], Day, Day.AddDays(1)));

        Assert.Contains("No data is available for this location", error.Message);
    }

    [Fact]
    public async Task Network_failures_and_bad_answers_are_forecast_errors()
    {
        var offline = new OpenMeteoPastRainForecastSource(new HttpClient(new FailingHttpMessageHandler()));
        var (wrong, _) = Create("error-service-overloaded.json");

        await Assert.ThrowsAsync<RainForecastException>(() => offline.GetAsync([Barajas], Day, Day.AddDays(1)));
        await Assert.ThrowsAsync<RainForecastException>(() => wrong.GetAsync([Barajas], Day, Day.AddDays(1)));
    }

    [Fact]
    public async Task No_places_no_request()
    {
        var (source, handler) = Create("arome-hd-15min-lemd-2026-10-06.json");

        Assert.Empty(await source.GetAsync([], Day, Day.AddDays(1)));
        Assert.Empty(handler.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => source.GetAsync([Barajas], Day, Day));
    }
}
