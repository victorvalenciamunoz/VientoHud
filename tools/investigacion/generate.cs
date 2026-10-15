#:project ../../src/VientoHud.Core
// Live run of Core's LoopGenerator with the real BRouter client (phase 2, step 4c2). One loop per case, towards its
// bearing, or with "*" as bearing, three candidates spread 120° apart. Writes the best loop of each as GPX.
// dotnet run tools/investigacion/generate.cs -- <outDir> <name>:<lat>:<lon>:<bearing|*>:<km> ...
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var engine = new BRouterRouteEngine(http, new BRouterOptions { MinRequestInterval = TimeSpan.FromSeconds(2) });
var generator = new LoopGenerator(engine);
var summary = new List<string>();
var total = 0;

foreach (var spec in args.Skip(1))
{
    var p = spec.Split(':');
    var name = p[0];
    var start = new Coordinate(double.Parse(p[1], inv), double.Parse(p[2], inv));
    var km = double.Parse(p[4], inv);
    IReadOnlyList<double> bearings = p[3] == "*" ? LoopGenerator.SpreadBearings(3) : [double.Parse(p[3], inv)];

    var results = await generator.GenerateManyAsync(start, bearings, km);
    foreach (var r in results)
    {
        total += r.Requests;
        Console.WriteLine($"== {name} {km} km towards {r.BearingDegrees:0}°: {r.Requests} requests");
        foreach (var a in r.Attempts) Console.WriteLine($"   {Describe(a, km)}");
        var line = r.Best is { } b
            ? $"{name,-10} {km,4} km {r.BearingDegrees,3:0}°: {(r.Found ? "VALID  " : "invalid")} {Describe(b, km)} ({r.Requests} requests)"
            : $"{name,-10} {km,4} km {r.BearingDegrees,3:0}°: no route ({r.Requests} requests)";
        summary.Add(line);
        if (r.Best is { } best)
        {
            using var f = File.Create(Path.Combine(outDir, $"{name}-{r.BearingDegrees:0}.gpx"));
            GpxWriter.Write(new GpxTrack($"{name} {km} km hacia {r.BearingDegrees:0}°",
                best.Route.Points.Select(q => new GpxTrackPoint(q.Position, q.ElevationMeters)).ToList(),
                DateTimeOffset.UtcNow, Copyright: GpxCopyright.OpenStreetMap), f);
        }
    }
}
Console.WriteLine();
Console.WriteLine($"requests {total}");
summary.ForEach(Console.WriteLine);

static string Describe(LoopCandidate c, double km) =>
    $"fan {c.FanBearingDegrees,3:0}° {c.Route.DistanceKm,5:0.0} km ({c.DistanceError,4:+0%;-0%}) spurs {c.Analysis.SpurFraction,3:P0} " +
    $"repeated {c.Analysis.RepeatedFraction,3:P0} trunk {c.Analysis.TrunkKm:0.0} km forbidden {c.Analysis.ForbiddenKm:0.0} km " +
    $"off bearing {c.BearingDeviationDegrees,3:0}° {(c.IsValid ? "VALID" : c.Problems.ToString())}";
