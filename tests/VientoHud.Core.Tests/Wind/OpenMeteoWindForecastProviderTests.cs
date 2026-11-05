using System.Net;
using System.Web;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Tests.Wind;

public partial class OpenMeteoWindForecastProviderTests
{
    private const string Arome = "meteofrance_arome_france";
    private const string BestMatch = "best_match";

    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset FixtureStart = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);

    private static (OpenMeteoWindForecastProvider Provider, StubHttpMessageHandler Handler) Create(
        HttpStatusCode status,
        string body)
    {
        var handler = new StubHttpMessageHandler(status, body);
        return (new OpenMeteoWindForecastProvider(new HttpClient(handler)), handler);
    }

    private static string ModelOf(Uri uri) => HttpUtility.ParseQueryString(uri.Query)["models"]!;

    [Fact]
    public async Task Throws_with_the_reason_from_an_error_response()
    {
        var (provider, _) = Create(HttpStatusCode.BadRequest, Fixture.Read("OpenMeteo/error-latitude-out-of-range.json"));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Origin, FixtureStart, 1));

        Assert.Contains("HTTP 400", ex.Message);
        Assert.Contains("Latitude must be in range of -90 to 90°", ex.Message);
    }

    [Fact]
    public async Task Throws_when_the_service_is_overloaded_instead_of_falling_back()
    {
        // The real response body was recorded; its HTTP status was not, so 503 is an assumption.
        var (provider, handler) = Create(HttpStatusCode.ServiceUnavailable, Fixture.Read("OpenMeteo/error-service-overloaded.json"));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Origin, FixtureStart, 1));

        Assert.Contains("The service is overloaded", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Throws_when_the_body_is_not_json()
    {
        var (provider, _) = Create(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Origin, FixtureStart, 1));

        Assert.Contains("HTTP 502", ex.Message);
    }

    [Fact]
    public async Task Wraps_network_failures()
    {
        var provider = new OpenMeteoWindForecastProvider(new HttpClient(new FailingHttpMessageHandler()));

        var ex = await Assert.ThrowsAsync<WindForecastException>(() => provider.GetHourlyAsync(Origin, FixtureStart, 1));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
