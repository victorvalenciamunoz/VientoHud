using System.Net;
using System.Web;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Tests.Wind;

public class OpenMeteoPastWindForecastSourceTests
{
    private const string Arome = "meteofrance_arome_france";
    private const string EcmwfIfs = "ecmwf_ifs";
    private const string HistoricalHost = "historical-forecast-api.open-meteo.com";
    private const string PreviousRunsHost = "previous-runs-api.open-meteo.com";

    // Station coordinates as IEM lists them: Madrid-Barajas and León.
    private static readonly Coordinate Lemd = new(40.46667, -3.55556);
    private static readonly Coordinate Leln = new(42.58889, -5.64944);
    private static readonly Coordinate Tarifa = new(36.01, -5.6);

    private static readonly DateTimeOffset October1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset October3 = October1.AddDays(2);

    // Recorded 2026-10-14 for 2026-10-01T00:00Z .. 2026-10-02T23:00Z, keyed by host, latitude and model parameters.
    private static readonly Dictionary<(string Host, string Latitudes, string Model), (HttpStatusCode, string)> Responses = new()
    {
        [(HistoricalHost, "40.46667,42.58889", Arome)] =
            (HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-short-arome-lemd-leln-2026-10-01.json")),
        [(PreviousRunsHost, "40.46667,42.58889", Arome)] =
            (HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-dayahead-arome-lemd-leln-2026-10-01.json")),
        [(HistoricalHost, "40.46667,36.01", Arome)] =
            (HttpStatusCode.BadRequest, Fixture.Read("OpenMeteo/error-no-data-for-location-lemd-tarifa-arome.json")),
        [(HistoricalHost, "40.46667", Arome)] =
            (HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-short-arome-lemd-2026-10-01.json")),
        [(HistoricalHost, "36.01", Arome)] =
            (HttpStatusCode.BadRequest, Fixture.Read("OpenMeteo/error-no-data-for-location-tarifa-arome.json")),
        // ECMWF IFS archives no gusts a day ahead.
        [(PreviousRunsHost, "40.46667,42.58889", EcmwfIfs)] =
            (HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-dayahead-ecmwf-ifs-lemd-leln-2026-10-01.json")),
    };

    private static (OpenMeteoPastWindForecastSource Source, StubHttpMessageHandler Handler) Create()
    {
        var handler = new StubHttpMessageHandler(uri =>
            Responses.TryGetValue((uri.Host, Query(uri)["latitude"]!, Query(uri)["models"]!), out var response)
                ? response
                : (HttpStatusCode.NotFound, $"No fixture for {uri}"));
        return (new OpenMeteoPastWindForecastSource(new HttpClient(handler)), handler);
    }

    private static System.Collections.Specialized.NameValueCollection Query(Uri uri) =>
        HttpUtility.ParseQueryString(uri.Query);

    [Fact]
    public async Task Short_range_asks_the_historical_forecast_api_for_all_locations_at_once()
    {
        var (source, handler) = Create();

        await source.GetAsync([Lemd, Leln], Arome, ForecastLead.ShortRange, October1, October3);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https://historical-forecast-api.open-meteo.com/v1/forecast", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            "latitude=40.46667,42.58889&longitude=-3.55556,-5.64944" +
            "&hourly=wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
            "&models=meteofrance_arome_france" +
            "&start_hour=2026-10-01T00:00&end_hour=2026-10-02T23:00" +
            "&wind_speed_unit=kmh&timezone=GMT&timeformat=unixtime",
            uri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Short_range_returns_one_forecast_per_location_in_request_order()
    {
        var (source, _) = Create();

        var forecasts = await source.GetAsync([Lemd, Leln], Arome, ForecastLead.ShortRange, October1, October3);

        Assert.Equal([Lemd, Leln], forecasts.Select(f => f.Requested));
        Assert.All(forecasts, f =>
        {
            Assert.Equal(Arome, f.Model);
            Assert.Equal(ForecastLead.ShortRange, f.Lead);
            Assert.Equal(Enumerable.Range(0, 48).Select(h => October1.AddHours(h)), f.Hours.Select(s => s.Time));
        });
        Assert.Equal(new Coordinate(40.475, -3.5500002), forecasts[0].GridPoint);
        Assert.Equal(new Coordinate(42.6, -5.65), forecasts[1].GridPoint);
        Assert.Equal(new WindSample(October1, 10.7, 11.2, 310, Arome), forecasts[0].Hours[0]);
        Assert.Equal(new WindSample(October1.AddHours(22), 9.1, 15.1, 56, Arome), forecasts[0].Hours[22]);
        Assert.Equal(new WindSample(October1.AddHours(47), 7.9, 11.9, 39, Arome), forecasts[1].Hours[47]);
    }

    [Fact]
    public async Task Day_ahead_reads_the_previous_day_variables_from_the_previous_runs_api()
    {
        var (source, handler) = Create();

        var forecasts = await source.GetAsync([Lemd, Leln], Arome, ForecastLead.DayAhead, October1, October3);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal(PreviousRunsHost, uri.Host);
        Assert.Equal(
            "wind_speed_10m_previous_day1,wind_direction_10m_previous_day1,wind_gusts_10m_previous_day1",
            Query(uri)["hourly"]);
        Assert.All(forecasts, f => Assert.Equal(ForecastLead.DayAhead, f.Lead));
        Assert.Equal(new WindSample(October1, 17.2, 31.3, 296, Arome), forecasts[1].Hours[0]);
        Assert.Equal(new WindSample(October1.AddHours(47), 11.0, 16.6, 19, Arome), forecasts[1].Hours[47]);
    }

    [Fact]
    public async Task Keeps_hours_whose_gust_was_not_archived()
    {
        var (source, _) = Create();

        var forecasts = await source.GetAsync([Lemd, Leln], EcmwfIfs, ForecastLead.DayAhead, October1, October3);

        Assert.All(forecasts, f => Assert.Equal(48, f.Hours.Count));
        Assert.All(forecasts.SelectMany(f => f.Hours), s => Assert.Null(s.GustKmh));
        Assert.Equal(new WindSample(October1, 4.0, null, 185, EcmwfIfs), forecasts[0].Hours[0]);
    }

    [Fact]
    public async Task When_the_model_misses_a_location_asks_location_by_location()
    {
        var (source, handler) = Create();

        var forecasts = await source.GetAsync([Lemd, Tarifa], Arome, ForecastLead.ShortRange, October1, October3);

        Assert.Equal(["40.46667,36.01", "40.46667", "36.01"], handler.Requests.Select(u => Query(u)["latitude"]));
        Assert.Equal([Lemd, Tarifa], forecasts.Select(f => f.Requested));
        Assert.Equal(48, forecasts[0].Hours.Count);
        Assert.NotNull(forecasts[0].GridPoint);
        Assert.Null(forecasts[1].GridPoint);
        Assert.Empty(forecasts[1].Hours);
    }

    [Fact]
    public async Task A_single_uncovered_location_is_reported_without_retrying()
    {
        var (source, handler) = Create();

        var forecasts = await source.GetAsync([Tarifa], Arome, ForecastLead.ShortRange, October1, October3);

        Assert.Single(handler.Requests);
        var forecast = Assert.Single(forecasts);
        Assert.Null(forecast.GridPoint);
        Assert.Empty(forecast.Hours);
    }

    [Fact]
    public async Task Covers_whole_hours_from_inclusive_to_exclusive()
    {
        var (source, handler) = Create();

        await source.GetAsync([Lemd, Leln], Arome, ForecastLead.ShortRange, October1.AddMinutes(30), October3.AddMinutes(-30));

        var query = Query(Assert.Single(handler.Requests));
        Assert.Equal("2026-10-01T01:00", query["start_hour"]);
        Assert.Equal("2026-10-02T23:00", query["end_hour"]);
    }

    [Fact]
    public async Task Rejects_a_range_without_a_whole_hour()
    {
        var (source, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetAsync([Lemd], Arome, ForecastLead.ShortRange, October1.AddMinutes(10), October1.AddMinutes(50)));
    }

    [Fact]
    public async Task Makes_no_request_without_locations()
    {
        var (source, handler) = Create();

        var forecasts = await source.GetAsync([], Arome, ForecastLead.ShortRange, October1, October3);

        Assert.Empty(forecasts);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Fails_when_the_response_has_a_different_number_of_locations()
    {
        // The single-location body answering a two-location request.
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-short-arome-lemd-2026-10-01.json"));
        var source = new OpenMeteoPastWindForecastSource(new HttpClient(handler));

        await Assert.ThrowsAsync<WindForecastException>(() =>
            source.GetAsync([Lemd, Leln], Arome, ForecastLead.ShortRange, October1, October3));
    }

    [Fact]
    public async Task Other_errors_are_not_mistaken_for_missing_coverage()
    {
        // The real response body was recorded; its HTTP status was not, so 503 is an assumption.
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable, Fixture.Read("OpenMeteo/error-service-overloaded.json"));
        var source = new OpenMeteoPastWindForecastSource(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() =>
            source.GetAsync([Lemd, Leln], Arome, ForecastLead.ShortRange, October1, October3));

        Assert.Contains("The service is overloaded", ex.Message);
        Assert.Single(handler.Requests);
    }
}
