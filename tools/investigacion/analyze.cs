#:project ../../src/VientoHud.Core
// Analyzes recorded BRouter GeoJSON files. Usage: dotnet run analyze.cs -- <file.geojson> <lonlats> ...
using System.Diagnostics;
using System.Globalization;
using System.Net;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
for (var a = 0; a < args.Length; a += 2)
{
    var body = File.ReadAllText(args[a]);
    var wps = args[a + 1].Split('|').Select(s => s.Split(','))
        .Select(p => new Coordinate(double.Parse(p[1], inv), double.Parse(p[0], inv))).ToList();
    var engine = new BRouterRouteEngine(new HttpClient(new Stub(body)),
        new BRouterOptions { ServerProfile = "x", MinRequestInterval = TimeSpan.Zero });
    var route = await engine.RouteAsync(wps);
    var sw = Stopwatch.StartNew();
    var r = RouteAnalyzer.Analyze(route, wps);
    Console.WriteLine($"== {Path.GetFileName(args[a])} {r.DistanceKm:0.0} km  repeated {r.RepeatedFraction:P1} ({r.RepeatedKm:0.0} km)  forbidden {r.ForbiddenKm:0.00}  pavedTrack {r.PavedTrackKm:0.00}  unknown {r.UnknownKm:0.00}  [{sw.ElapsedMilliseconds} ms]");
    Console.WriteLine("   offsets: " + string.Join(" ", r.WaypointOffsetsKm.Select(o => o.ToString("0.000", inv))));
    foreach (var u in r.Roads.Where(u => u.Category != RoadCategory.Road)) Console.WriteLine($"   {u.Category} {u.Highway}/{u.Surface} {u.Km:0.000}");
    // Variants of the measure for comparison.
    foreach (var (sp, tol) in new[] { (0.1, 0.03), (0.05, 0.03), (0.1, 0.05), (0.1, 0.02) })
    {
        var v = RouteAnalyzer.Analyze(route, wps, new RouteAnalysisOptions { SampleSpacingKm = sp, RepeatToleranceKm = tol });
        Console.Write($"   [{sp * 1000:0} m,{tol * 1000:0} m] {v.RepeatedFraction:P1}");
    }
    Console.WriteLine();
}

class Stub(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
}
