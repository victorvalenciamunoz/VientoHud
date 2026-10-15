#:project ../../src/VientoHud.Core
// Phase 2 investigation: ways to get loops that do not repeat.
//  M0  fan waypoints, one request (current approach)
//  M2  same waypoints, leg by leg; each leg pays a penalty on roads already ridden (BRouter weighted nogo circles)
//  M3  M0, then move each waypoint reached through a dead-end spur to the spur's base, one more request
//  M32 M3's waypoints routed as M2
// dotnet run loops.cs -- <outDir> <weight> <name>:<lat>:<lon>:<bearing>:<radiusKm> ...
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
var weight = double.Parse(args[1], inv);
var profileText = Environment.GetEnvironmentVariable("ALLOW_TRUNK") == "1"
    ? BRouterRouteEngine.StrictRoadProfile.Replace("switch or highway=trunk highway=trunk_link          10000", "switch or highway=trunk highway=trunk_link          1.5")
    : BRouterRouteEngine.StrictRoadProfile;
Console.WriteLine(profileText == BRouterRouteEngine.StrictRoadProfile ? "profile: strict" : "profile: strict + trunk allowed");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var last = DateTime.MinValue;
var calls = 0;

async Task<(HttpStatusCode, string)> Send(HttpRequestMessage req)
{
    var wait = TimeSpan.FromSeconds(1.1) - (DateTime.UtcNow - last);
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
    last = DateTime.UtcNow;
    calls++;
    using var res = await http.SendAsync(req);
    return (res.StatusCode, await res.Content.ReadAsStringAsync());
}

var (_, up) = await Send(new HttpRequestMessage(HttpMethod.Post, "https://brouter.de/brouter/profile")
{ Content = new StringContent(profileText, Encoding.UTF8, "text/plain") });
var profile = JsonDocument.Parse(up).RootElement.GetProperty("profileid").GetString()!;

string F(double d) => d.ToString("0.#####", inv);

async Task<Route?> RouteRaw(IReadOnlyList<Coordinate> wps, string nogos)
{
    var lonlats = string.Join('|', wps.Select(w => $"{F(w.Longitude)},{F(w.Latitude)}"));
    var url = $"https://brouter.de/brouter?lonlats={lonlats}&profile={profile}&alternativeidx=0&format=geojson"
        + (nogos.Length > 0 ? "&nogos=" + nogos : "");
    var (status, body) = await Send(new HttpRequestMessage(HttpMethod.Get, url));
    if (status != HttpStatusCode.OK)
    {
        Console.WriteLine($"     HTTP {(int)status} (url {url.Length} chars): {(body.Length > 120 ? body[..120] : body).Trim()}");
        return null;
    }
    var parser = new BRouterRouteEngine(new HttpClient(new Stub(body)), new BRouterOptions { ServerProfile = "x", MinRequestInterval = TimeSpan.Zero });
    return await parser.RouteAsync(wps);
}

// Weighted circles every 80 m along a route (radius 40 m): riding inside costs `weight` extra per metre.
string Nogos(IEnumerable<Route> legs)
{
    var sb = new StringBuilder();
    foreach (var leg in legs)
    {
        var acc = double.MaxValue; Coordinate? prev = null;
        foreach (var p in leg.Points.Select(p => p.Position))
        {
            if (prev is { } q) acc += q.DistanceKmTo(p);
            if (acc >= 0.08) { sb.Append($"{F(p.Longitude)},{F(p.Latitude)},40,{F(weight)}|"); acc = 0; }
            prev = p;
        }
    }
    return sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : "";
}

static Route Join(List<Route> legs)
{
    var points = new List<RoutePoint>(); var segments = new List<RouteSegment>(); var km = 0.0;
    foreach (var leg in legs)
    {
        points.AddRange(points.Count == 0 ? leg.Points : leg.Points.Skip(1));
        segments.AddRange(leg.Segments.Select(s => new RouteSegment(s.StartKm + km, s.LengthKm, s.WayTags)));
        km += leg.DistanceKm;
    }
    return new Route(points, segments, km, null, legs[0].Attribution);
}

async Task<Route?> ByLegs(IReadOnlyList<Coordinate> wps)
{
    var legs = new List<Route>();
    for (var i = 1; i < wps.Count; i++)
    {
        var leg = await RouteRaw([wps[i - 1], wps[i]], Nogos(legs));
        if (leg is null) return null;
        legs.Add(leg);
    }
    return Join(legs);
}

// Waypoints reached through an out-and-back spur move to where the spur leaves the rest of the loop.
static List<Coordinate> MoveOffSpurs(Route route, IReadOnlyList<Coordinate> wps, List<string> notes)
{
    var pts = route.Points.Select(p => p.Position).ToList();
    var along = new double[pts.Count];
    for (var i = 1; i < pts.Count; i++) along[i] = along[i - 1] + pts[i - 1].DistanceKmTo(pts[i]);
    Coordinate At(double s)
    {
        s = Math.Clamp(s, 0, along[^1]);
        var i = Array.BinarySearch(along, s); if (i >= 0) return pts[i]; i = ~i;
        var t = (s - along[i - 1]) / (along[i] - along[i - 1]);
        return new Coordinate(pts[i - 1].Latitude + t * (pts[i].Latitude - pts[i - 1].Latitude), pts[i - 1].Longitude + t * (pts[i].Longitude - pts[i - 1].Longitude));
    }
    var result = wps.ToList();
    for (var w = 1; w < wps.Count - 1; w++)
    {
        var tip = Enumerable.Range(0, pts.Count).MinBy(i => pts[i].DistanceKmTo(wps[w]));
        var s = along[tip];
        var d = 0.05;
        while (s - d > 0 && s + d < along[^1] && At(s - d).DistanceKmTo(At(s + d)) < 0.05) d += 0.05;
        d -= 0.05;
        if (d >= 0.3)
        {
            result[w] = At(s - d);
            notes.Add($"wp{w} spur {d:0.0} km");
        }
    }
    return result;
}

static double Roundness(Route r)
{
    var o = r.Points[0].Position; var k = 111.195; var c = Math.Cos(o.Latitude * Math.PI / 180);
    var xy = r.Points.Select(p => ((p.Position.Longitude - o.Longitude) * c * k, (p.Position.Latitude - o.Latitude) * k)).ToList();
    var a = 0.0;
    for (var i = 0; i < xy.Count; i++) { var (x1, y1) = xy[i]; var (x2, y2) = xy[(i + 1) % xy.Count]; a += x1 * y2 - x2 * y1; }
    return 4 * Math.PI * Math.Abs(a) / 2 / Math.Pow(r.DistanceKm, 2);
}

void Report(string name, string method, Route? r, IReadOnlyList<Coordinate> wps, double radius, int callsUsed, string extra = "")
{
    if (r is null) { Console.WriteLine($"   {method,-4} failed ({callsUsed} calls)"); return; }
    var a = RouteAnalyzer.Analyze(r, wps);
    Console.WriteLine($"   {method,-4} {r.DistanceKm,6:0.0} km ({r.DistanceKm / radius,4:0.0} R)  repeated {a.RepeatedFraction,4:P0}  roundness {Roundness(r):0.00}  forbidden {a.ForbiddenKm:0.0}  max offset {a.MaxWaypointOffsetKm:0.0}  calls {callsUsed} {extra}");
    using var f = File.Create(Path.Combine(outDir, $"{name}-{method}.gpx"));
    GpxWriter.Write(new GpxTrack($"{name} {method}", r.Points.Select(p => new GpxTrackPoint(p.Position, p.ElevationMeters)).ToList(), DateTimeOffset.UtcNow, Copyright: GpxCopyright.OpenStreetMap), f);
}

foreach (var spec in args.Skip(2))
{
    var p = spec.Split(':');
    var name = p[0];
    var start = new Coordinate(double.Parse(p[1], inv), double.Parse(p[2], inv));
    var bearing = double.Parse(p[3], inv);
    var radius = double.Parse(p[4], inv);
    const int n = 5;
    var wps = new List<Coordinate> { start };
    for (var i = 1; i < n; i++) wps.Add(Destination(start, bearing - (90 - 180.0 * i / n), radius));
    wps.Add(start);
    Console.WriteLine($"== {name} bearing {bearing}° R {radius} km");

    var c0 = calls; var m0 = await RouteRaw(wps, ""); Report(name, "M0", m0, wps, radius, calls - c0);
    c0 = calls; var m2 = await ByLegs(wps); Report(name, "M2", m2, wps, radius, calls - c0);
    if (m0 is null) continue;
    var notes = new List<string>();
    var moved = MoveOffSpurs(m0, wps, notes);
    if (notes.Count == 0) { Console.WriteLine("   M3   no spurs found"); continue; }
    c0 = calls; var m3 = await RouteRaw(moved, ""); Report(name, "M3", m3, moved, radius, calls - c0 + 1, string.Join(", ", notes));
    c0 = calls; var m32 = await ByLegs(moved); Report(name, "M32", m32, moved, radius, calls - c0 + 1);
}
Console.WriteLine($"total calls {calls}");

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
