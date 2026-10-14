using System.Net;
using System.Web;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;

namespace VientoHud.Core.Tests.Wind;

public class IemWindObservationSourceTests
{
    private const double KmhPerKnot = 1.852;

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset October1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset October3 = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    // Recorded 2026-10-14. AT__ASOS is fetched for any point because IEM lists Port-of-Spain (Trinidad)
    // in the Austrian network, which stretches its bounding box across the Atlantic.
    private static readonly Dictionary<string, string> Fixtures = new()
    {
        ["/api/1/networks.json"] = "Iem/networks.json",
        ["/api/1/network/AT__ASOS.json"] = "Iem/network-AT__ASOS.json",
        ["/api/1/network/ES__ASOS.json"] = "Iem/network-ES__ASOS.json",
        ["/cgi-bin/request/asos.py"] = "Iem/asos-LEMD-LELN-2026-10-01-to-03.csv",
    };

    private static (IemWindObservationSource Source, StubHttpMessageHandler Handler) Create()
    {
        var handler = new StubHttpMessageHandler(uri =>
            Fixtures.TryGetValue(uri.AbsolutePath, out var fixture)
                ? (HttpStatusCode.OK, Fixture.Read(fixture))
                : (HttpStatusCode.NotFound, $"No fixture for {uri}"));
        return (new IemWindObservationSource(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Finds_nearest_stations_downloading_only_networks_whose_box_is_close()
    {
        var (source, handler) = Create();

        var stations = await source.FindNearestStationsAsync(Madrid, 5, 100);

        Assert.Equal(
            ["/api/1/networks.json", "/api/1/network/AT__ASOS.json", "/api/1/network/ES__ASOS.json"],
            handler.Requests.Select(u => u.AbsolutePath));
        Assert.Equal(["LEVS", "LEGT", "LEMD", "LETO", "LECV"], stations.Select(s => s.Id));
        Assert.Equal(8.4, stations[0].DistanceKm, precision: 1);
        Assert.Equal(13.7, stations[2].DistanceKm, precision: 1);
        Assert.Equal(new Coordinate(40.46667, -3.55556), stations[2].Position);
        Assert.True(stations.Zip(stations.Skip(1)).All(p => p.First.DistanceKm <= p.Second.DistanceKm));
    }

    [Fact]
    public async Task Leaves_out_stations_beyond_the_maximum_distance()
    {
        var (source, _) = Create();

        var stations = await source.FindNearestStationsAsync(Madrid, 5, 10);

        Assert.Equal(["LEVS"], stations.Select(s => s.Id));
    }

    [Fact]
    public async Task Returns_at_most_count_stations()
    {
        var (source, _) = Create();

        var stations = await source.FindNearestStationsAsync(Madrid, 2, 100);

        Assert.Equal(["LEVS", "LEGT"], stations.Select(s => s.Id));
    }

    [Fact]
    public async Task Skips_networks_with_unreadable_extents()
    {
        const string networks = """
            {"schema":{},"data":[
              {"index":0,"id":"XX__ASOS","extent":"not-hex"},
              {"index":1,"id":"YY__ASOS","extent":null},
              {"index":2,"id":"ZZ__ASOS","extent":"0101"}
            ]}
            """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, networks);
        var source = new IemWindObservationSource(new HttpClient(handler));

        var stations = await source.FindNearestStationsAsync(Madrid, 5, 100);

        Assert.Empty(stations);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Requests_observations_for_all_stations_in_one_call()
    {
        var (source, handler) = Create();

        await source.GetObservationsAsync(["LEMD", "LELN"], October1, October3);

        var uri = Assert.Single(handler.Requests);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("LEMD,LELN", query["station"]);
        Assert.Equal("drct,sknt,gust", query["data"]);
        Assert.Equal("2026-10-01T00:00:00Z", query["sts"]);
        Assert.Equal("2026-10-03T00:00:00Z", query["ets"]);
        Assert.Equal("Etc/UTC", query["tz"]);
        Assert.Equal("onlycomma", query["format"]);
    }

    [Fact]
    public async Task Parses_observations_converting_knots_to_kmh()
    {
        var (source, _) = Create();

        var observations = await source.GetObservationsAsync(["LEMD", "LELN"], October1, October3);

        Assert.Equal(196, observations.Count);
        // LELN 010000Z AUTO 35004KT
        Assert.Equal(new WindObservation("LELN", October1, 4 * KmhPerKnot, null, 350), observations[0]);
        // LEMD 012214Z 13017G29KT (special report during a thunderstorm)
        Assert.Contains(
            new WindObservation("LEMD", October1.AddHours(22).AddMinutes(14), 17 * KmhPerKnot, 29 * KmhPerKnot, 130),
            observations);
    }

    [Fact]
    public async Task Variable_and_calm_wind_have_no_direction()
    {
        var (source, _) = Create();

        var observations = await source.GetObservationsAsync(["LEMD", "LELN"], October1, October3);

        // LELN 010730Z VRB01KT
        Assert.Contains(new WindObservation("LELN", October1.AddHours(7.5), 1 * KmhPerKnot, null, null), observations);
        // LELN 020400Z AUTO 00000KT: IEM gives direction 0, which must not read as "from the north".
        Assert.Contains(new WindObservation("LELN", October1.AddDays(1).AddHours(4), 0, null, null), observations);
    }

    [Fact]
    public async Task Orders_by_station_then_time_and_keeps_only_the_requested_range()
    {
        var (source, _) = Create();
        var from = October1.AddHours(22);
        var to = October1.AddHours(23);

        var observations = await source.GetObservationsAsync(["LEMD", "LELN"], from, to);

        Assert.All(observations, o => Assert.InRange(o.Time, from, to.AddTicks(-1)));
        Assert.Equal(
            observations.OrderBy(o => o.StationId, StringComparer.Ordinal).ThenBy(o => o.Time),
            observations);
        Assert.Contains(observations, o => o.StationId == "LEMD" && o.Time == from.AddMinutes(14));
    }

    [Fact]
    public async Task Makes_no_request_without_stations()
    {
        var (source, handler) = Create();

        var observations = await source.GetObservationsAsync([], October1, October3);

        Assert.Empty(observations);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Explains_rate_limit_errors()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.TooManyRequests, "Too Many Requests");
        var source = new IemWindObservationSource(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<WindObservationException>(
            () => source.GetObservationsAsync(["LEMD"], October1, October3));

        Assert.Contains("HTTP 429", ex.Message);
        Assert.Contains("one request per second", ex.Message);
    }

    [Fact]
    public async Task Wraps_network_failures()
    {
        var source = new IemWindObservationSource(new HttpClient(new FailingHttpMessageHandler()));

        var ex = await Assert.ThrowsAsync<WindObservationException>(() => source.FindNearestStationsAsync(Madrid, 5, 100));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Rejects_an_empty_time_range()
    {
        var (source, _) = Create();

        await Assert.ThrowsAsync<ArgumentException>(() => source.GetObservationsAsync(["LEMD"], October3, October1));
    }
}
