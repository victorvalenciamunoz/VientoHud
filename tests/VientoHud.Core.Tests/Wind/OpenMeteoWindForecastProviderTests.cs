using System.Net;
using System.Web;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Tests.Wind;

public class OpenMeteoWindForecastProviderTests
{
    private const string Arome = "meteofrance_arome_france";
    private const string BestMatch = "best_match";

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly Coordinate Tarifa = new(36.01, -5.6);
    private static readonly DateTimeOffset FixtureStart = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);

    // Recorded 2026-10-14 for 2026-10-15T22:00Z .. 2026-10-17T02:00Z (29 hours), one model per file.
    // AROME has data for the first 15 hours and null afterwards.
    private static readonly Dictionary<string, (HttpStatusCode, string)> MadridResponses = new()
    {
        [Arome] = (HttpStatusCode.OK, Fixture.Read("OpenMeteo/madrid-arome-2026-10-15T22.json")),
        [BestMatch] = (HttpStatusCode.OK, Fixture.Read("OpenMeteo/madrid-best-match-2026-10-15T22.json")),
    };

    // Recorded 2026-10-14 for 2026-10-15T22:00Z .. 2026-10-16T01:00Z. Tarifa is outside AROME's domain.
    private static readonly Dictionary<string, (HttpStatusCode, string)> TarifaResponses = new()
    {
        [Arome] = (HttpStatusCode.BadRequest, Fixture.Read("OpenMeteo/error-no-data-for-location-tarifa-arome.json")),
        [BestMatch] = (HttpStatusCode.OK, Fixture.Read("OpenMeteo/tarifa-best-match-2026-10-15T22.json")),
    };

    private static (OpenMeteoWindForecastProvider Provider, StubHttpMessageHandler Handler) Create(
        Dictionary<string, (HttpStatusCode, string)> responsesByModel,
        OpenMeteoOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(uri => responsesByModel[ModelOf(uri)]);
        return (new OpenMeteoWindForecastProvider(new HttpClient(handler), options), handler);
    }

    private static (OpenMeteoWindForecastProvider Provider, StubHttpMessageHandler Handler) Create(
        HttpStatusCode status,
        string body)
    {
        var handler = new StubHttpMessageHandler(status, body);
        return (new OpenMeteoWindForecastProvider(new HttpClient(handler)), handler);
    }

    private static string ModelOf(Uri uri) => HttpUtility.ParseQueryString(uri.Query)["models"]!;

    [Fact]
    public async Task Builds_one_request_per_model_for_the_hour_range_in_utc()
    {
        var (provider, handler) = Create(MadridResponses);
        var fromMadridLocalTime = new DateTimeOffset(2026, 10, 16, 0, 25, 0, TimeSpan.FromHours(2));

        await provider.GetHourlyAsync(Madrid, fromMadridLocalTime, 29);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri =>
            Assert.Equal("https://api.open-meteo.com/v1/forecast", uri.GetLeftPart(UriPartial.Path)));
        Assert.Equal(
            "latitude=40.4168&longitude=-3.7038" +
            "&hourly=wind_speed_10m,wind_direction_10m,wind_gusts_10m" +
            "&models=meteofrance_arome_france" +
            "&start_hour=2026-10-15T22:00&end_hour=2026-10-17T02:00" +
            "&wind_speed_unit=kmh&timezone=GMT&timeformat=unixtime",
            handler.Requests[0].Query.TrimStart('?'));
        Assert.Equal(BestMatch, ModelOf(handler.Requests[1]));
    }

    [Fact]
    public async Task Fills_hours_the_first_model_lacks_with_the_next_one()
    {
        var (provider, _) = Create(MadridResponses);

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 29);

        Assert.Equal(
            Enumerable.Range(0, 29).Select(h => FixtureStart.AddHours(h)),
            forecast.Hours.Select(s => s.Time));
        Assert.Equal(new WindSample(FixtureStart, 7.4, 14.0, 166, Arome), forecast.Hours[0]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(14), 4.8, 16.2, 222, Arome), forecast.Hours[14]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(15), 5.2, 15.1, 214, BestMatch), forecast.Hours[15]);
        Assert.Equal(new WindSample(FixtureStart.AddHours(28), 6.3, 14.0, 24, BestMatch), forecast.Hours[28]);
        Assert.All(forecast.Hours.Take(15), s => Assert.Equal(Arome, s.Model));
        Assert.All(forecast.Hours.Skip(15), s => Assert.Equal(BestMatch, s.Model));
    }

    [Fact]
    public async Task Skips_later_models_when_the_first_covers_every_hour()
    {
        var (provider, handler) = Create(MadridResponses);

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 15);

        Assert.Equal(Arome, ModelOf(Assert.Single(handler.Requests)));
        Assert.All(forecast.Hours, s => Assert.Equal(Arome, s.Model));
    }

    [Fact]
    public async Task Falls_back_when_a_model_does_not_cover_the_location()
    {
        var (provider, handler) = Create(TarifaResponses);

        var forecast = await provider.GetHourlyAsync(Tarifa, FixtureStart, 4);

        Assert.Equal([Arome, BestMatch], handler.Requests.Select(ModelOf));
        Assert.Equal(new WindSample(FixtureStart, 30.5, 52.9, 73, BestMatch), forecast.Hours[0]);
        Assert.All(forecast.Hours, s => Assert.Equal(BestMatch, s.Model));
        Assert.Equal(new Coordinate(36.03362, -5.568066), forecast.GridPoint);
    }

    [Fact]
    public async Task Reports_grid_point_and_elevation_of_the_model_that_supplied_the_first_hours()
    {
        var (provider, _) = Create(MadridResponses);

        var forecast = await provider.GetHourlyAsync(Madrid, FixtureStart, 29);

        Assert.Equal(Madrid, forecast.Requested);
        Assert.Equal(new Coordinate(40.425, -3.6999998), forecast.GridPoint);
        Assert.Equal(666.0, forecast.ElevationMeters);
        Assert.Equal(OpenMeteoWindForecastProvider.Attribution, forecast.Attribution);
    }

    [Fact]
    public async Task Reads_a_short_single_model_response()
    {
        var (provider, _) = Create(
            new Dictionary<string, (HttpStatusCode, string)>
            {
                [Arome] = (HttpStatusCode.OK, Fixture.Read("OpenMeteo/madrid-arome-only-2026-10-15T22.json")),
            },
            new OpenMeteoOptions { Models = [Arome] });

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
        var (provider, _) = Create(MadridResponses, new OpenMeteoOptions { Models = [Arome] });

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 29));

        Assert.Contains("2026-10-16 13:00 UTC", ex.Message);
    }

    [Fact]
    public async Task Throws_when_no_model_covers_the_location()
    {
        var (provider, _) = Create(TarifaResponses, new OpenMeteoOptions { Models = [Arome] });

        await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Tarifa, FixtureStart, 4));
    }

    [Fact]
    public async Task Throws_with_the_reason_from_an_error_response()
    {
        var (provider, _) = Create(HttpStatusCode.BadRequest, Fixture.Read("OpenMeteo/error-latitude-out-of-range.json"));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.Contains("HTTP 400", ex.Message);
        Assert.Contains("Latitude must be in range of -90 to 90°", ex.Message);
    }

    [Fact]
    public async Task Throws_when_the_service_is_overloaded_instead_of_falling_back()
    {
        // The real response body was recorded; its HTTP status was not, so 503 is an assumption.
        var (provider, handler) = Create(HttpStatusCode.ServiceUnavailable, Fixture.Read("OpenMeteo/error-service-overloaded.json"));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 1));

        Assert.Contains("The service is overloaded", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Throws_when_the_body_is_not_json()
    {
        var (provider, _) = Create(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");

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
        var (provider, _) = Create(MadridResponses);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetHourlyAsync(Madrid, FixtureStart, 0));
    }
}
