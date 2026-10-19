#:project ../../src/VientoHud.Core
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
// dotnet run tools/investigacion/outandback.cs -- <carpeta salida> [km]
// Fase 6, paso 6a: idas y vueltas de Core (LoopGenerator con RouteShape.OutAndBack) en vivo contra un BRouter (pensado
// para el local): tres rumbos cada 120° desde el norte en los sitios de prueba; validez, distancia, peticiones, km por tipo
// de vía y un GPX de cada una para mirarla en gpx.studio.
var output = args[0];
var km = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 60;
Directory.CreateDirectory(output);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var generator = new LoopGenerator(new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero }));

(string Name, Coordinate Start)[] places =
[
    ("leon", new(42.5987, -5.5671)), ("labaneza", new(42.3000, -5.8980)),
    ("humanes", new(40.2526, -3.8299)), ("trescantos", new(40.6006, -3.7083)),
];
foreach (var (name, start) in places)
{
    var results = await generator.GenerateManyAsync(start, LoopGenerator.SpreadBearings(3), km, RouteShape.OutAndBack);
    foreach (var r in results.OrderBy(r => r.BearingDegrees))
    {
        if (r.Best is not { } best)
        {
            Console.WriteLine($"{name,-11} {r.BearingDegrees,3:0}°: sin ruta ({r.Requests} peticiones)");
            continue;
        }
        var a = best.Analysis;
        var file = Path.Combine(output, $"{name}-{r.BearingDegrees:000}-ida-y-vuelta{(r.Found ? "" : "-descartada")}.gpx");
        await using (var stream = File.Create(file))
        {
            GpxWriter.Write(GpxTrack.FromRoute(best.Route, $"{name} {r.BearingDegrees:0}° ida y vuelta", DateTimeOffset.UtcNow), stream);
        }
        Console.WriteLine($"{name,-11} {r.BearingDegrees,3:0}°: {(r.Found ? "válida  " : "no válida")} {best.Route.DistanceKm,5:0.0} km " +
                          $"(rumbo {best.FanBearingDegrees:0}°, {r.Requests} peticiones), trunk {a.TrunkKm:0.0} km, caminos asfaltados {a.PavedTrackKm:0.0} km, " +
                          $"repite {a.RepeatedFraction:P0}{(best.Problems == LoopProblems.None ? "" : $"; falla: {best.Problems}")}");
    }
}
