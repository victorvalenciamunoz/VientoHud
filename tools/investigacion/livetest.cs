#:project ../../src/VientoHud.Core
// Phase 2 live test (STATUS, step 4): does a simple generator get valid loops from brouter.de?
// For each case: fan waypoints (start + 4 points at R km), then up to MaxCalls requests:
//  - move each waypoint that causes a spur to where the spur leaves the route, and route again;
//  - scale R by target / distance;
//  - turn the bearing ±20°, ±40° on "target island" or after rounds without a valid loop.
// Valid: ≤ 10 % of the route on spurs, distance within ±10 %, 0 forbidden km, centre of the route within 45° of the
// requested bearing (DECISIONS 015). Responses are cached on disk, so a rerun does not ask brouter.de again.
// dotnet run tools/investigacion/livetest.cs -- <outDir> <strict|trunk> <name>:<lat>:<lon>:<bearing>:<km> ...
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

const int MaxCalls = 12;
var requestInterval = TimeSpan.FromSeconds(3);
var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
var cacheDir = Path.Combine(outDir, "cache");
Directory.CreateDirectory(cacheDir);
var allowTrunk = args[1] == "trunk";
var profileText = allowTrunk
    ? BRouterRouteEngine.StrictRoadProfile.Replace(
        "switch or highway=trunk highway=trunk_link          10000", "switch or highway=trunk highway=trunk_link          1.5")
    : BRouterRouteEngine.StrictRoadProfile;
if (allowTrunk && profileText == BRouterRouteEngine.StrictRoadProfile) throw new InvalidOperationException("trunk rule not found");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var last = DateTime.MinValue;
var networkCalls = 0;
string? profileId = null;

async Task<(HttpStatusCode Status, string Body)> Send(HttpRequestMessage request)
{
    var wait = requestInterval - (DateTime.UtcNow - last);
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
    last = DateTime.UtcNow;
    networkCalls++;
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (response.StatusCode == HttpStatusCode.Forbidden) throw new BlockedException(body);
    return (response.StatusCode, body);
}

string F(double d) => d.ToString("0.######", inv);

// Route through waypoints; null when BRouter finds no route. Cached by profile variant and waypoints.
async Task<Route?> RouteAsync(IReadOnlyList<Coordinate> waypoints)
{
    var lonLats = string.Join('|', waypoints.Select(w => $"{F(w.Longitude)},{F(w.Latitude)}"));
    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args[1] + lonLats)))[..16];
    var file = Path.Combine(cacheDir, key + ".txt");
    string status, body;
    if (File.Exists(file))
    {
        var cached = File.ReadAllText(file);
        status = cached[..3];
        body = cached[4..];
    }
    else
    {
        if (profileId is null)
        {
            var (s, b) = await Send(new HttpRequestMessage(HttpMethod.Post, "https://brouter.de/brouter/profile")
            { Content = new StringContent(profileText, Encoding.UTF8, "text/plain") });
            profileId = JsonDocument.Parse(b).RootElement.GetProperty("profileid").GetString();
        }
        var url = $"https://brouter.de/brouter?lonlats={lonLats}&profile={profileId}&alternativeidx=0&format=geojson";
        var (code, text) = await Send(new HttpRequestMessage(HttpMethod.Get, url));
        status = ((int)code).ToString(inv);
        body = text;
        File.WriteAllText(file, status + "\n" + body);
    }
    if (status == "400") return null;
    if (status != "200") throw new InvalidOperationException($"HTTP {status}: {body[..Math.Min(200, body.Length)]}");
    var parser = new BRouterRouteEngine(new HttpClient(new Stub(body)), new BRouterOptions { ServerProfile = "x", MinRequestInterval = TimeSpan.Zero });
    return await parser.RouteAsync(waypoints);
}

var results = new List<string>();
try
{
    foreach (var spec in args.Skip(2))
    {
        var p = spec.Split(':');
        var name = p[0];
        var start = new Coordinate(double.Parse(p[1], inv), double.Parse(p[2], inv));
        var bearing = double.Parse(p[3], inv);
        var targetKm = double.Parse(p[4], inv);
        results.Add(await RunCase(name, start, bearing, targetKm));
    }
}
catch (BlockedException ex)
{
    Console.WriteLine($"!! brouter.de blocked us (HTTP 403: {ex.Message.Trim()}). Stopping.");
}
Console.WriteLine();
Console.WriteLine($"profile {(allowTrunk ? "strict + trunk allowed" : "strict")}, network calls {networkCalls}");
results.ForEach(Console.WriteLine);

async Task<string> RunCase(string name, Coordinate start, double bearing, double targetKm)
{
    Console.WriteLine($"== {name}: {targetKm} km towards {bearing}°");
    var calls = 0;
    var radius = targetKm / 5.8;
    double[] turns = [0, 20, -20, 40, -40];
    var turn = 0;
    var roundsOnBearing = 0;
    (Route Route, RouteAnalysis Analysis, double Bearing, string Notes)? best = null;
    var waypoints = Fan(start, bearing, radius);

    while (calls < MaxCalls)
    {
        var heading = bearing + turns[turn];
        calls++;
        var route = await RouteAsync(waypoints);
        if (route is null)
        {
            Console.WriteLine($"   call {calls}: target island at {heading}° R {radius:0.0}");
            if (++turn >= turns.Length) break;
            waypoints = Fan(start, bearing + turns[turn], radius);
            roundsOnBearing = 0;
            continue;
        }
        var analysis = RouteAnalyzer.Analyze(route, waypoints);
        var verdict = Judge(route, analysis, start, bearing, targetKm);
        Console.WriteLine($"   call {calls}: {heading}° R {radius:0.0} → {Describe(route, analysis, start, bearing, targetKm)} {(verdict is null ? "VALID" : verdict)}");
        if (best is null || Score(route, analysis, start, bearing, targetKm) < Score(best.Value.Route, best.Value.Analysis, start, bearing, targetKm))
        {
            best = (route, analysis, heading, $"{calls} calls");
        }
        if (verdict is null) break;

        // Spurs first: move the waypoints that cause them, keeping R.
        var moved = MoveOffSpurs(route, analysis, waypoints);
        if (moved is not null && analysis.SpurFraction > 0.1)
        {
            waypoints = moved;
            continue;
        }
        // Then the distance, or a new bearing after three rounds on this one.
        if (++roundsOnBearing >= 3)
        {
            if (++turn >= turns.Length) break;
            roundsOnBearing = 0;
            radius = targetKm / 5.8;
        }
        else
        {
            radius *= Math.Clamp(targetKm / route.DistanceKm, 0.5, 2);
        }
        waypoints = Fan(start, bearing + turns[turn], radius);
    }

    if (best is not { } b) return $"{name,-14} {targetKm,4} km {bearing,3}°: no route ({calls} calls)";
    var final = Judge(b.Route, b.Analysis, start, bearing, targetKm);
    using (var f = File.Create(Path.Combine(outDir, $"{name}-{(allowTrunk ? "trunk" : "strict")}.gpx")))
    {
        GpxWriter.Write(new GpxTrack($"{name} {(allowTrunk ? "trunk" : "estricto")}",
            b.Route.Points.Select(q => new GpxTrackPoint(q.Position, q.ElevationMeters)).ToList(),
            DateTimeOffset.UtcNow, Copyright: GpxCopyright.OpenStreetMap), f);
    }
    return $"{name,-14} {targetKm,4} km {bearing,3}°: {(final is null ? "VALID  " : "invalid")} {Describe(b.Route, b.Analysis, start, bearing, targetKm)} ({calls} calls){(final is null ? "" : " — " + final)}";
}

static List<Coordinate> Fan(Coordinate start, double bearing, double radius)
{
    const int n = 5;
    var waypoints = new List<Coordinate> { start };
    for (var i = 1; i < n; i++) waypoints.Add(Destination(start, bearing - (90 - 180.0 * i / n), radius));
    waypoints.Add(start);
    return waypoints;
}

// Null if valid; otherwise why not.
static string? Judge(Route route, RouteAnalysis a, Coordinate start, double bearing, double targetKm)
{
    var why = new List<string>();
    if (a.SpurFraction > 0.1) why.Add("spurs");
    if (Math.Abs(route.DistanceKm / targetKm - 1) > 0.1) why.Add("distance");
    if (a.ForbiddenKm > 0) why.Add("forbidden");
    if (Deviation(route, start, bearing) > 45) why.Add("bearing");
    return why.Count == 0 ? null : string.Join(", ", why);
}

static double Score(Route route, RouteAnalysis a, Coordinate start, double bearing, double targetKm) =>
    (a.SpurFraction > 0.1 ? 10 + a.SpurFraction : 0) + (a.ForbiddenKm > 0 ? 10 : 0)
    + (Deviation(route, start, bearing) > 45 ? 5 : 0) + Math.Abs(route.DistanceKm / targetKm - 1);

static string Describe(Route route, RouteAnalysis a, Coordinate start, double bearing, double targetKm) =>
    $"{route.DistanceKm,5:0.0} km ({route.DistanceKm / targetKm - 1,4:+0%;-0%}) spurs {a.SpurFraction,3:P0} repeated {a.RepeatedFraction,3:P0} forbidden {a.ForbiddenKm:0.0} km (trunk {a.Roads.Where(r => r.Highway is "trunk" or "trunk_link").Sum(r => r.Km):0.0}) off bearing {Deviation(route, start, bearing),3:0}°";

// Angle between the requested bearing and the bearing from the start to the route's centre.
static double Deviation(Route route, Coordinate start, double bearing)
{
    var lat = route.Points.Average(q => q.Position.Latitude);
    var lon = route.Points.Average(q => q.Position.Longitude);
    var centre = Math.Atan2((lon - start.Longitude) * Math.Cos(start.Latitude * Math.PI / 180), lat - start.Latitude) * 180 / Math.PI;
    return Compass.AngleBetween((centre + 360) % 360, (bearing + 360) % 360);
}

// Each waypoint whose spur makes the route go in and out the same way moves to where the spur leaves the route.
static List<Coordinate>? MoveOffSpurs(Route route, RouteAnalysis a, IReadOnlyList<Coordinate> waypoints)
{
    var along = new double[route.Points.Count];
    for (var i = 1; i < along.Length; i++) along[i] = along[i - 1] + route.Points[i - 1].Position.DistanceKmTo(route.Points[i].Position);
    var scale = route.DistanceKm / along[^1];
    Coordinate At(double km)
    {
        var i = Array.FindIndex(along, x => x * scale >= km);
        return route.Points[Math.Max(0, i)].Position;
    }
    var result = waypoints.ToList();
    var movedAny = false;
    foreach (var spur in a.Repeats.Where(r => r.IsSpur && r.StartKm < r.OtherPassStartKm))
    {
        for (var w = 1; w < waypoints.Count - 1; w++)
        {
            var nearest = Enumerable.Range(0, along.Length).MinBy(i => route.Points[i].Position.DistanceKmTo(waypoints[w]));
            var km = along[nearest] * scale;
            if (km >= spur.StartKm && km <= spur.OtherPassEndKm)
            {
                result[w] = At(spur.StartKm);
                movedAny = true;
            }
        }
    }
    return movedAny ? result : null;
}

static Coordinate Destination(Coordinate from, double bearingDeg, double km)
{
    const double R = 6371.0088;
    var d = km / R; var b = bearingDeg * Math.PI / 180;
    var lat1 = from.Latitude * Math.PI / 180; var lon1 = from.Longitude * Math.PI / 180;
    var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d) + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(b));
    var lon2 = lon1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(lat1), Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));
    return new Coordinate(lat2 * 180 / Math.PI, (lon2 * 180 / Math.PI + 540) % 360 - 180);
}

class Stub(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
}

class BlockedException(string message) : Exception(message);
