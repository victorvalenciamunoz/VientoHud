#:project ../../src/VientoHud.Core
// Records raw BRouter responses for fan loops. Usage:
// dotnet run record.cs -- <outDir> <name>:<lat>:<lon>:<bearing>:<radiusKm>:<strict|serverProfile> ...
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using VientoHud.Core;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
DateTime last = DateTime.MinValue;
async Task<(HttpStatusCode, string)> Send(HttpRequestMessage req)
{
    var wait = TimeSpan.FromSeconds(1.2) - (DateTime.UtcNow - last);
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
    last = DateTime.UtcNow;
    using var res = await http.SendAsync(req);
    return (res.StatusCode, await res.Content.ReadAsStringAsync());
}

string? strictId = null;
foreach (var spec in args.Skip(1))
{
    var p = spec.Split(':');
    var name = p[0];
    var start = new Coordinate(double.Parse(p[1], inv), double.Parse(p[2], inv));
    var bearing = double.Parse(p[3], inv);
    var r = double.Parse(p[4], inv);
    var profile = p[5];
    if (profile == "strict")
    {
        if (strictId is null)
        {
            var (s, b) = await Send(new HttpRequestMessage(HttpMethod.Post, "https://brouter.de/brouter/profile")
            { Content = new StringContent(BRouterRouteEngine.StrictRoadProfile, Encoding.UTF8, "text/plain") });
            strictId = JsonDocument.Parse(b).RootElement.GetProperty("profileid").GetString();
            Console.WriteLine($"upload {s} {strictId}");
        }
        profile = strictId!;
    }

    const int n = 5;
    var wps = new List<Coordinate> { start };
    for (var i = 1; i < n; i++) wps.Add(Destination(start, bearing - (90 - 180.0 * i / n), r));
    wps.Add(start);
    var lonlats = string.Join('|', wps.Select(w => $"{w.Longitude.ToString("0.######", inv)},{w.Latitude.ToString("0.######", inv)}"));
    var url = $"https://brouter.de/brouter?lonlats={Uri.EscapeDataString(lonlats)}&profile={profile}&alternativeidx=0&format=geojson";
    var (status, body) = await Send(new HttpRequestMessage(HttpMethod.Get, url));
    var file = Path.Combine(outDir, name + (status == HttpStatusCode.OK ? ".geojson" : ".txt"));
    await File.WriteAllTextAsync(file, body);
    Console.WriteLine($"== {name} HTTP {(int)status} {body.Length / 1024} KB lonlats={lonlats}");
    if (status != HttpStatusCode.OK) { Console.WriteLine(body.Length > 200 ? body[..200] : body); continue; }

    // Quick look: length, waypoint distance to nearest track point, km per highway/surface.
    using var doc = JsonDocument.Parse(body);
    var f = doc.RootElement.GetProperty("features")[0];
    var pts = f.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
        .Select(c => new Coordinate(c[1].GetDouble(), c[0].GetDouble())).ToList();
    Console.WriteLine($"   track-length {f.GetProperty("properties").GetProperty("track-length").GetString()} m, {pts.Count} points");
    Console.WriteLine("   waypoint offsets km: " + string.Join(" ", wps.Select(w => pts.Min(q => q.DistanceKmTo(w)).ToString("0.00", inv))));
    var msgs = f.GetProperty("properties").GetProperty("messages");
    var km = new Dictionary<string, double>();
    foreach (var row in msgs.EnumerateArray().Skip(1))
    {
        var tags = row[9].GetString()!.Split(' ').Select(t => t.Split('=')).Where(t => t.Length == 2).ToDictionary(t => t[0], t => t[1]);
        var key = tags.GetValueOrDefault("highway", "?") + "/" + tags.GetValueOrDefault("surface", "-");
        km[key] = km.GetValueOrDefault(key) + double.Parse(row[3].GetString()!, inv) / 1000;
    }
    foreach (var kv in km.OrderByDescending(kv => kv.Value).Where(kv => kv.Value > 0.2)) Console.Write($"   {kv.Key} {kv.Value:0.0}");
    Console.WriteLine();
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
