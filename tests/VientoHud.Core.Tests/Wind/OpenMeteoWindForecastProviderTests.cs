using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Tests.Wind;

public class OpenMeteoWindForecastProviderTests
{
    private const string Arome = "meteofrance_arome_france";
    private const string BestMatch = "best_match";

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset FixtureStart = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);

    // Recorded 2026-10-14 for 2026-10-15T22:00Z .. 2026-10-17T02:00Z (29 hours). AROME has data for the
    // first 12 hours and null afterwards, so the rest must come from best_match.
    private const string TwoModelFixture = "OpenMeteo/madrid-arome-best-match-2026-10-15T22.json";

    private static (OpenMeteoWindForecastProvider Provider, StubHttpMessageHandler Handler) Create(
        string fixture,
        HttpStatusCode status = HttpStatusCode.OK,
        OpenMeteoOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(status, Fixture.Read(fixture));
        return (new OpenMeteoWindForecastProvider(new HttpClient(handler), options), handler);
    }

    [Fact]
    public async Task Builds_request_for_the_hour_range_in_utc()
    {
        var (provider, handler) = Create(TwoModelFixture);
        var fromMadridLocalTime = new DateTimeOffset(2026, 10, 16, 0, 25, 0, TimeSpan.FromHours(2));

        await provider.GetHourlyAsync(Madrid, fromMadridLocalTime, 29);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https://api.open-meteo.com/v1/forecast", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            "latitude=40.4168&longitude=-3.7038" +
            "&hourly=wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
            "&models=meteofrance_arome_france,best_match" +
            "&start_hour=2026-10-15T22:00&end_hour=2026-10-17T02:00" +
            "&wind_speed_unit=kmh&timezone=GMT&timeformat=unixtime",
            uri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task Parses_hours_preferring_the_first_model_with_data()
    {
        var (provider, _) = Create(TwoModelFixture);

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 29);

        Assert.Equal(29, forecast.Hours.Count);
        Assert.Equal(
            Enumerable.Range(0, 29).Select(h => FixtureStart.AddHours(h)),
            forecast.Hours.Select(s => s.Time));

        Assert.Equal(new WindSample(FixtureStart, 6.5, 13.0, 174, Arome), forecast.Hours[0]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(11), 3.9, 11.9, 202, Arome), forecast.Hours[11]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(12), 2.2, 9.0, 189, BestMatch), forecast.Hours[12]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(28), 6.3, 14.0, 24, BestMatch), forecast.Hours[28]);
        Assert.All(forecast.Hours.Take(12), s => Assert.Equal(Arome, s.Model));
        Assert.All(forecast.Hours.Skip(12), s => Assert.Equal(BestMatch, s.Model));
    }

    [Fact]
    public async Task Reports_grid_point_elevation_and_attribution()
    {
        var (provider, _) = Create(TwoModelFixture);

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 29);

        Assert.Equal(Madrid, forecast.Requested);
        Assert.Equal(new Coordinate(40.425, -3.6999998), forecast.GridPoint);
        Assert.Equal(666.0, forecast.ElevationMeters);
        Assert.Equal(OpenMeteoWindForecastProvider.Attribution, forecast.Attribution);
    }

    [Fact]
    public async Task Reads_unsuffixed_variables_when_a_single_model_is_requested()
    {
        var (provider, _) = Create(
            "OpenMeteo/madrid-arome-only-2026-10-15T22.json",
            options: new OpenMeteoOptions { Models = [Arome] });

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 3);

        Assert.Equal(
            [
                new WindSample(FixtureStart, 6.5, 13.0, 174, Arome),
                new WindSample(FixtureStart.AddHours(1), 5.6, 11.2, 165, Arome),
                new WindSample(FixtureStart.AddHours(2), 4.6, 10.1, 129, Arome),
            ],
            forecast.Hours);
    }

    [Fact]
    public async Task Throws_when_no_model_covers_a_requested_hour()
    {
        // Only AROME configured: the fixture's hours 12-28 are null for it.
        var (provider, _) = Create(TwoModelFixture, options: new OpenMeteoOptions { Models = [Arome] });

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 29));

        Assert.Contains("2026-10-16 10:00 UTC", ex.Message);
    }

    [Fact]
    public async Task Throws_with_the_reason_from_an_error_response()
    {
        var (provider, _) = Create("OpenMeteo/error-latitude-out-of-range.json", HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.Contains("HTTP 400", ex.Message);
        Assert.Contains("Latitude must be in range of -90 to 90°", ex.Message);
    }

    [Fact]
    public async Task Throws_when_the_service_is_overloaded()
    {
        // The real response body was recorded; its HTTP status was not, so 503 is an assumption.
        var (provider, _) = Create("OpenMeteo/error-service-overloaded.json", HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.Contains("The service is overloaded", ex.Message);
    }

    [Fact]
    public async Task Throws_when_the_body_is_not_json()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");
        var provider = new OpenMeteoWindForecastProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.Contains("HTTP 502", ex.Message);
    }

    [Fact]
    public async Task Wraps_network_failures()
    {
        var provider = new OpenMeteoWindForecastProvider(new HttpClient(new FailingHttpMessageHandler()));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Rejects_less_than_one_hour()
    {
        var (provider, _) = Create(TwoModelFixture);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 0));
    }
}
