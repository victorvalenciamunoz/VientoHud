using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Web;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public partial class BRouterRouteEngineTests
{
    // Waypoints of the synthetic loop (Fixtures/BRouter/route-synthetic-loop.geojson): a 2.5 km square north-east of
    // the test origin.
    private static readonly Coordinate[] SyntheticLoop =
    [
        new(45.0, 1.0), new(45.022483, 1.0), new(45.022483, 1.031795), new(45.0, 1.031795), new(45.0, 1.0),
    ];

    private const string UploadedId = "custom_1791993804565";

    private static readonly BRouterOptions NoWait = new() { MinRequestInterval = TimeSpan.Zero };

    /// <summary>A server that accepts profile uploads and answers every route with <paramref name="route"/>.</summary>
    private static ScriptedHttpMessageHandler Server(Func<RecordedRequest, (HttpStatusCode, string)>? route = null) =>
        new(request => request.Method == HttpMethod.Post
            ? (HttpStatusCode.OK, Fixture.Read("BRouter/profile-upload-ok.json"))
            : route?.Invoke(request) ?? (HttpStatusCode.OK, Fixture.Read("BRouter/route-synthetic-loop.geojson")));

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

        await engine.RouteAsync(SyntheticLoop);
        await engine.RouteAsync(SyntheticLoop);

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

        await Engine(server).RouteAsync(SyntheticLoop);

        var request = server.Requests[1].Uri;
        Assert.Equal("https://brouter.de/brouter", request.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Dictionary<string, string>
        {
            ["lonlats"] = "1,45|1,45.022483|1.031795,45.022483|1.031795,45|1,45",
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

            await Engine(server).RouteAsync([new(45.0125, 1.5), new(45.1, 1.25)]);

            Assert.Equal("1.5,45.0125|1.25,45.1", Query(server.Requests[1].Uri)["lonlats"]);
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

        await Engine(server, NoWait with { ServerProfile = "fastbike" }).RouteAsync(SyntheticLoop);

        var request = Assert.Single(server.Requests);
        Assert.Equal("fastbike", Query(request.Uri)["profile"]);
    }

    [Fact]
    public async Task Reads_the_track_distance_ascent_and_attribution_of_a_recorded_route()
    {
        var route = await Engine(Server()).RouteAsync(SyntheticLoop);

        Assert.Equal(10.0, route.DistanceKm, precision: 3);
        Assert.Equal(42, route.AscentMeters);
        Assert.Equal(BRouterRouteEngine.Attribution, route.Attribution);
        Assert.Equal(new RoutePoint(new Coordinate(45.0, 1.0), 200), route.Points[0]);
        Assert.InRange(route.Points[^1].Position.DistanceKmTo(SyntheticLoop[^1]), 0, 0.01);
        Assert.All(route.Points, p => Assert.NotNull(p.ElevationMeters));
    }

    [Fact]
    public async Task Segments_cover_the_route_end_to_end()
    {
        var route = await Engine(Server()).RouteAsync(SyntheticLoop);

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
        var route = await Engine(Server()).RouteAsync(SyntheticLoop);

        Assert.Contains(route.Segments, s => s.Highway == "tertiary" && s.Surface == "asphalt");
        Assert.All(route.Segments, s => Assert.NotNull(s.Highway));
        for (var i = 1; i < route.Segments.Count; i++)
        {
            Assert.NotEqual(route.Segments[i - 1].WayTags, route.Segments[i].WayTags, new TagsComparer());
        }
    }

    [Fact]
    public async Task Target_island_is_an_unreachable_waypoint()
    {
        var server = Server(_ => (HttpStatusCode.BadRequest, Fixture.Read("BRouter/target-island.txt")));

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(SyntheticLoop));

        Assert.Equal(RouteFailure.Unreachable, error.Failure);
        Assert.Contains("target island detected", error.Message);
    }

    [Fact]
    public async Task Uploads_the_profile_again_when_the_server_lost_it()
    {
        var routeCalls = 0;
        var server = Server(_ => ++routeCalls == 1
            ? (HttpStatusCode.InternalServerError, "")
            : (HttpStatusCode.OK, Fixture.Read("BRouter/route-synthetic-loop.geojson")));

        var route = await Engine(server).RouteAsync(SyntheticLoop);

        Assert.Equal(10.0, route.DistanceKm, precision: 3);
        Assert.Equal(["POST", "GET", "POST", "GET"], server.Requests.Select(r => r.Method.Method));
    }

    [Fact]
    public async Task Server_error_after_uploading_again_is_a_service_failure()
    {
        var server = Server(_ => (HttpStatusCode.InternalServerError, ""));

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(SyntheticLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.Contains("HTTP 500", error.Message);
        Assert.Equal(4, server.Requests.Count);
    }

    [Fact]
    public async Task Server_profile_is_not_uploaded_again_on_server_error()
    {
        var server = Server(_ => (HttpStatusCode.InternalServerError, ""));

        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(server, NoWait with { ServerProfile = "fastbike" }).RouteAsync(SyntheticLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Rejected_profile_is_a_service_failure_with_the_reason()
    {
        var server = new ScriptedHttpMessageHandler(_ => (HttpStatusCode.OK, Fixture.Read("BRouter/profile-upload-error.json")));

        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(server, NoWait with { ProfileText = "assign broken = surface=woodchips" }).RouteAsync(SyntheticLoop));

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

        var error = await Assert.ThrowsAsync<RouteException>(() => Engine(server).RouteAsync(SyntheticLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
    }

    [Fact]
    public async Task Network_failure_is_a_service_failure()
    {
        var error = await Assert.ThrowsAsync<RouteException>(
            () => Engine(new FailingHttpMessageHandler()).RouteAsync(SyntheticLoop));

        Assert.Equal(RouteFailure.Service, error.Failure);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task Needs_at_least_two_waypoints() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Engine(Server()).RouteAsync([TestPlaces.Origin]));

    [Fact]
    public async Task Spaces_requests_by_the_minimum_interval()
    {
        var server = Server();
        var engine = Engine(server, new BRouterOptions { MinRequestInterval = TimeSpan.FromMilliseconds(150) });
        var watch = Stopwatch.StartNew();

        await engine.RouteAsync(SyntheticLoop);   // upload + route
        await engine.RouteAsync(SyntheticLoop);   // route

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
        // For traced rides the detours to the points touched are the point (DECISIONS 051): only that line changes.
        var keeping = BRouterRouteEngine.StrictRoadProfileKeepingViaPoints;
        Assert.Contains("assign correctMisplacedViaPoints = false ", keeping);
        Assert.DoesNotContain("assign correctMisplacedViaPoints = true ", keeping);
        Assert.Equal(profile.Length + 1, keeping.Length);
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
