#:project ../../src/VientoHud.Core
// Which kinds of road did a recorded ride use? Map-matches each GPX by routing through a point every km of it
// (strict profile with trunk allowed, so the match can follow national roads) and reads BRouter's tags.
// Reports how much of the match lies on the ride (within 50 m) and km per road type, with where the trunk stretches are.
// dotnet run tools/investigacion/matchrides.cs -- <outDir> <ride.gpx> ...
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
var cacheDir = Path.Combine(args[0], "cache");
Directory.CreateDirectory(cacheDir);
var profileText = BRouterRouteEngine.StrictRoadProfile.Replace(
    "switch or highway=trunk highway=trunk_link          10000", "switch or highway=trunk highway=trunk_link          1.5");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
string? profileId = null;
var last = DateTime.MinValue;

async Task<(int Status, string Body)> Send(HttpRequestMessage request)
{
    var wait = TimeSpan.FromSeconds(3) - (DateTime.UtcNow - last);
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
    last = DateTime.UtcNow;
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("brouter.de blocked us: " + body);
    return ((int)response.StatusCode, body);
}

foreach (var file in args.Skip(1))
{
    XNamespace gpx = "http://www.topografix.com/GPX/1/1";
    var ride = XDocument.Load(file).Descendants(gpx + "trkpt")
        .Select(p => new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv)))
        .ToList();

    // A waypoint every km of the ride, plus its last point.
    var waypoints = new List<Coordinate> { ride[0] };
    var since = 0.0;
    for (var i = 1; i < ride.Count; i++)
    {
        since += ride[i - 1].DistanceKmTo(ride[i]);
        if (since >= 1) { waypoints.Add(ride[i]); since = 0; }
    }
    waypoints.Add(ride[^1]);

    var lonLats = string.Join('|', waypoints.Select(w => $"{w.Longitude.ToString("0.######", inv)},{w.Latitude.ToString("0.######", inv)}"));
    var cache = Path.Combine(cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lonLats)))[..16] + ".txt");
    string body;
    if (File.Exists(cache))
    {
        body = File.ReadAllText(cache);
    }
    else
    {
        if (profileId is null)
        {
            var (_, b) = await Send(new HttpRequestMessage(HttpMethod.Post, "https://brouter.de/brouter/profile")
            { Content = new StringContent(profileText, Encoding.UTF8, "text/plain") });
            profileId = JsonDocument.Parse(b).RootElement.GetProperty("profileid").GetString();
        }
        var (status, text) = await Send(new HttpRequestMessage(HttpMethod.Get,
            $"https://brouter.de/brouter?lonlats={lonLats}&profile={profileId}&alternativeidx=0&format=geojson"));
        if (status != 200) { Console.WriteLine($"== {Path.GetFileName(file)}: HTTP {status} {text[..Math.Min(150, text.Length)]}"); continue; }
        body = text;
        File.WriteAllText(cache, body);
    }
    var parser = new BRouterRouteEngine(new HttpClient(new Stub(body)), new BRouterOptions { ServerProfile = "x", MinRequestInterval = TimeSpan.Zero });
    var match = await parser.RouteAsync(waypoints);

    // Where along the match each of its points is, and whether it lies on the ride.
    var along = new double[match.Points.Count];
    for (var i = 1; i < along.Length; i++) along[i] = along[i - 1] + match.Points[i - 1].Position.DistanceKmTo(match.Points[i].Position);
    var scale = match.DistanceKm / along[^1];
    var onRide = match.Points.Select(q => ride.Min(r => r.DistanceKmTo(q.Position)) < 0.05).ToArray();
    var onRideKm = Enumerable.Range(1, along.Length - 1).Where(i => onRide[i] && onRide[i - 1]).Sum(i => along[i] - along[i - 1]) * scale;

    var rideKm = ride.Zip(ride.Skip(1), (a, b) => a.DistanceKmTo(b)).Sum();
    Console.WriteLine($"== {Path.GetFileName(file)}: ride {rideKm:0.0} km, match {match.DistanceKm:0.0} km, {onRideKm / match.DistanceKm:P0} of the match on the ride");
    var byHighway = match.Segments.GroupBy(s => s.Highway ?? "?").Select(g => (g.Key, Km: g.Sum(s => s.LengthKm))).OrderByDescending(g => g.Km);
    Console.WriteLine("   " + string.Join("  ", byHighway.Where(g => g.Km >= 0.1).Select(g => $"{g.Key} {g.Km:0.0}")));
    foreach (var s in match.Segments.Where(s => s.Highway is "trunk" or "trunk_link" && s.LengthKm >= 0.05))
    {
        var i = Math.Max(0, Array.FindIndex(along, a => a * scale >= s.StartKm));
        var p = match.Points[i].Position;
        Console.WriteLine($"   {s.Highway} {s.LengthKm:0.0} km from km {s.StartKm:0.0} at {p.Latitude:0.0000},{p.Longitude:0.0000} ({string.Join(" ", s.WayTags.Where(t => t.Key != "highway").Select(t => $"{t.Key}={t.Value}"))})");
    }
}

class Stub(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
}
