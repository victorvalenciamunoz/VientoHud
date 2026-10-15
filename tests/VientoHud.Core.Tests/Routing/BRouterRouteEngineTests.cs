using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Web;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class BRouterRouteEngineTests
{
    // Waypoints of the recorded loop (Fixtures/BRouter/README.md): León, radius 3 km, heading north.
    private static readonly Coordinate[] LeonLoop =
    [
        new(42.5987, -5.5671), new(42.614558, -5.596752), new(42.624359, -5.578426),
        new(42.624359, -5.555774), new(42.614558, -5.537448), new(42.5987, -5.5671),
    ];

    private const string UploadedId = "custom_1791993804565";

    private static readonly BRouterOptions NoWait = new() { MinRequestInterval = TimeSpan.Zero };

    /// <summary>A server that accepts profile uploads and answers every route with <paramref name="route"/>.</summary>
    private static ScriptedHttpMessageHandler Server(Func<RecordedRequest, (HttpStatusCode, string)>? route = null) =>
        new(request => request.Method == HttpMethod.Post
            ? (HttpStatusCode.OK, Fixture.Read("BRouter/profile-upload-ok.json"))
            : route?.Invoke(request) ?? (HttpStatusCode.OK, Fixture.Read("BRouter/route-leon-loop.geojson")));

    private static BRouterRouteEngine Engine(HttpMessageHandler handler, BRouterOptions? options = null) =>
        new(new HttpClient(handler), options ?? NoWait);

    private static Dictionary<string, string> Query(Uri uri)
    {
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query.AllKeys.ToDictionary(k => k!, k => query[k]!);
    }

    [Fact]
    public async Task Uploads_the_strict_profile_once_and_routes_with_its_id()
    {
        var server = Server();
        var engine = Engine(server);

        await engine.RouteAsync(LeonLoop);
        await engine.RouteAsync(LeonLoop);

        Assert.Equal(3, server.Requests.Count);
        var upload = server.Requests[0];
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal("https://brouter.de/brouter/profile", upload.Uri.ToString());
        Assert.Equal(BRouterRouteEngine.StrictRoadProfile, upload.Body);
        Assert.All(server.Requests.Skip(1), r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task Asks_for_the_waypoints_in_order_as_lon_lat_with_the_uploaded_profile()
    {
        var server = Server();

        await Engine(server).RouteAsync(LeonLoop);

        var request = server.Requests[1].Uri;
        Assert.Equal("https://brouter.de/brouter", request.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Dictionary<string, string>
        {
            ["lonlats"] = "-5.5671,42.5987|-5.596752,42.614558|-5.578426,42.624359|-5.555774,42.624359|-5.537448,42.614558|-5.5671,42.5987",
            ["profile"] = UploadedId,
            ["alternativeidx"] = "0",
            ["format"] = "geojson",
        }, Query(request));
    }

    [Fact]
    public async Task Formats_coordinates_with_a_dot_whatever_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            var server = Server();

            await Engine(server).RouteAsync([new(40.4168, -3.7038), new(40.54, -3.88)]);

            Assert.Equal("-3.7038,40.4168|-3.88,40.54", Query(server.Requests[1].Uri)["lonlats"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Uses_a_server_profile_without_uploading()
    {
        var server = Server();

        await Engine(server, NoWait with { ServerProfile = "fastbike" }).RouteAsync(LeonLoop);

        var request = Assert.Single(server.Requests);
        Assert.Equal("fastbike", Query(request.Uri)["profile"]);
    }

    [Fact]
    public async Task Reads_the_track_distance_ascent_and_attribution_of_a_recorded_route()
    {
        var route = await Engine(Server()).RouteAsync(LeonLoop);

        Assert.Equal(17.402, route.DistanceKm, precision: 3);
        Assert.Equal(97, route.AscentMeters);
        Assert.Equal(BRouterRouteEngine.Attribution, route.Attribution);
        Assert.Equal(new RoutePoint(new Coordinate(42.5987, -5.567086), 836), route.Points[0]);
        Assert.InRange(route.Points[^1].Position.DistanceKmTo(LeonLoop[^1]), 0, 0.01);
        Assert.All(route.Points, p => Assert.NotNull(p.ElevationMeters));
    }

    [Fact]
    public async Task Segments_cover_the_route_end_to_end()
    {
        var route = await Engine(Server()).RouteAsync(LeonLoop);

        Assert.Equal(0, route.Segments[0].StartKm);
        for (var i = 1; i < route.Segments.Count; i++)
        {
            Assert.Equal(route.Segments[i - 1].StartKm + route.Segments[i - 1].LengthKm, route.Segments[i].StartKm, precision: 9);
        }
        Assert.Equal(route.DistanceKm, route.Segments[^1].StartKm + route.Segments[^1].LengthKm, precision: 6);
    }

    [Fact]
    public async Task Segments_carry_the_way_tags_and_merge_neighbours_with_the_same_tags()
    {
        var route = await Engine(Server()).RouteAsync(LeonLoop);

        Assert.Contains(route.Segments, s => s.Highway == "tertiary" && s.Surface == "asphalt");
        Assert.All(route.Segments, s => Assert.NotNull(s.Highway));
        for (var i = 1; i < route.Segments.Count; i++)
        {
            Assert.NotEqual(route.Segments[i - 1].WayTags, route.Segments[i].WayTags, new TagsComparer());
        }
    }

    [Fact]
    public async Task Recorded_strict_route_has_no_forbidden_ways()
    {
        // Characterises the profile on real data: no tracks, no unpaved surface. Recorded while trunk roads were still
        // forbidden (DECISIONS 016), so there is no trunk either.
        var route = await Engine(Server()).RouteAsync(LeonLoop);

        Assert.DoesNotContain(route.Segments, s => s.Highway is "motorway" or "motorway_link" or "trunk" or "trunk_link" or "track");
        Assert.DoesNotContain(route.Segments, s => s.Surface is "unpaved" or "gravel" or "dirt" or "ground" or "compacted");
    }

    [Fact]
    public async Task Target_island_is_an_unreachable_waypoint()
    {
        var server = Server(_ => (HttpStatusCode.BadRequest, Fixture.Read("BRouter/target-island.txt")));

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Unreachable, error.Failure);
        Assert.Contains("target island detected", error.Message);
    }

    [Fact]
    public async Task Uploads_the_profile_again_when_the_server_lost_it()
    {
        var routeCalls = 0;
        var server = Server(_ => ++routeCalls == 1
            ? (HttpStatusCode.InternalServerError, "")
            : (HttpStatusCode.OK, Fixture.Read("BRouter/route-leon-loop.geojson")));

        var route = await Engine(server).RouteAsync(LeonLoop);

        Assert.Equal(17.402, route.DistanceKm, precision: 3);
        Assert.Equal(["POST", "GET", "POST", "GET"], server.Requests.Select(r => r.Method.Method));
    }

    [Fact]
    public async Task Server_error_after_uploading_again_is_a_service_failure()
    {
        var server = Server(_ => (HttpStatusCode.InternalServerError, ""));

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.Contains("HTTP 500", error.Message);
        Assert.Equal(4, server.Requests.Count);
    }

    [Fact]
    public async Task Server_profile_is_not_uploaded_again_on_server_error()
    {
        var server = Server(_ => (HttpStatusCode.InternalServerError, ""));

        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(server, NoWait with { ServerProfile = "fastbike" }).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Rejected_profile_is_a_service_failure_with_the_reason()
    {
        var server = new ScriptedHttpMessageHandler(_ => (HttpStatusCode.OK, Fixture.Read("BRouter/profile-upload-error.json")));

        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(server, NoWait with { ProfileText = "assign broken = surface=woodchips" }).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.Contains("does not contain expressions for context way", error.Message);
        Assert.Equal("assign broken = surface=woodchips", Assert.Single(server.Requests).Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, "{\"type\": \"FeatureCollection\", \"features\": []}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html>down</html>")]
    public async Task Unexpected_responses_are_service_failures(HttpStatusCode status, string body)
    {
        var server = Server(_ => (status, body));

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
    }

    [Fact]
    public async Task Network_failure_is_a_service_failure()
    {
        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(new FailingHttpMessageHandler()).RouteAsync(LeonLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task Needs_at_least_two_waypoints() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Engine(Server()).RouteAsync([new(42.5987, -5.5671)]));

    [Fact]
    public async Task Spaces_requests_by_the_minimum_interval()
    {
        var server = Server();
        var engine = Engine(server, new BRouterOptions { MinRequestInterval = TimeSpan.FromMilliseconds(150) });
        var watch = Stopwatch.StartNew();

        await engine.RouteAsync(LeonLoop);   // upload + route
        await engine.RouteAsync(LeonLoop);   // route

        // Three requests: at least two intervals between them.
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(290), $"Took only {watch.Elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public void Strict_profile_is_embedded_penalises_trunk_and_forbids_construction_and_unpaved_tracks()
    {
        var profile = BRouterRouteEngine.StrictRoadProfile;

        Assert.StartsWith("# VientoHud strict road-bike profile.", profile);
        Assert.Contains("switch or highway=trunk highway=trunk_link          3\n", profile.ReplaceLineEndings("\n"));
        Assert.Contains("switch or highway=motorway highway=motorway_link    switch allow_motorways 1.5 10000", profile);
        Assert.Contains("assign allow_motorways       = false", profile);
        // BRouter cuts out going in and out of a dead end to reach a via point, whatever its length.
        Assert.Contains("assign correctMisplacedViaPoints = true ", profile);
        Assert.Contains("assign correctMisplacedViaPointsDistance = 0 ", profile);
        Assert.Contains("switch or highway=proposed or highway=abandoned highway=construction 10000", profile);
        Assert.Contains("switch    explicitlyunpaved                         10000", profile);
        Assert.Contains("   switch ispaved 2.0 10000", profile);
    }

    private sealed class TagsComparer : IEqualityComparer<IReadOnlyDictionary<string, string>>
    {
        public bool Equals(IReadOnlyDictionary<string, string>? x, IReadOnlyDictionary<string, string>? y) =>
            x!.Count == y!.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && v == kv.Value);

        public int GetHashCode(IReadOnlyDictionary<string, string> obj) => obj.Count;
    }
}
