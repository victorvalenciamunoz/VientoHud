#:project ../../src/VientoHud.Core
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Towns;
using VientoHud.Core.Towns.Ghsl;
// dotnet run tools/investigacion/urban.cs -- <carpeta salida> [km]
// Fase 6, paso 6c: cuánta ciudad tienen las rutas de hoy. Vueltas e idas y vueltas en tres rumbos (0°, 120°, 240°) desde
// los sitios de prueba, con el generador de Core contra un BRouter (pensado para el local); para cada una, km en ciudad
// (RouteUrban: km con 2 o más cruces, semáforos, stops o ceda el paso, o la mitad por calles), tramos de ciudad y km de
// vuelta cerca de casa a mitad de camino. Escribe un GPX por ruta, con un nombre que se puede citar ("humanes-NE-iv").
// Con un tercer argumento "pueblos", saca los puntos de paso de los pueblos de GHSL (paso 6e-2; cuadros en
// %LOCALAPPDATA%VientoHudghsl).
var output = args[0];
var km = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 60;
Directory.CreateDirectory(output);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var generator = new LoopGenerator(new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero }));
var useTowns = args.Length > 2 && args[2] == "pueblos";
using var ghslHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
ghslHttp.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var ghsl = new GhslBuiltUpSource(ghslHttp, new GhslOptions
{
    CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VientoHud", "ghsl"),
});

(string Name, Coordinate Start)[] places =
[
    ("leon", new(42.5987, -5.5671)), ("labaneza", new(42.3000, -5.8980)), ("humanes", new(40.2526, -3.8299)),
    ("trescantos", new(40.6006, -3.7083)), ("fuenlabrada", new(40.2842, -3.7942)),
];
string[] points = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSO", "SO", "OSO", "O", "ONO", "NO", "NNO"];
string Point(double bearing) => points[(int)Math.Round(bearing / 22.5) % 16];

Console.WriteLine("ruta                    válida  km    ciudad km   %    vuelve a casa km  tramos de ciudad");
foreach (var (name, start) in places)
{
    var placeGenerator = useTowns
        ? generator.WithTowns(TownMap.From(await ghsl.GetAroundAsync(start, km * RidePlanner.TownReachShare + RidePlanner.TownMarginKm)))
        : generator;
    foreach (var shape in new[] { RouteShape.Loop, RouteShape.OutAndBack })
    {
        foreach (var r in await placeGenerator.GenerateManyAsync(start, LoopGenerator.SpreadBearings(3), km, shape))
        {
            if (r.Best is not { } best)
            {
                continue;
            }
            var id = $"{name}-{Point(r.BearingDegrees)}-{(shape == RouteShape.Loop ? "v" : "iv")}";
            var u = RouteUrban.Measure(best.Route);
            await using (var stream = File.Create(Path.Combine(output, id + ".gpx")))
            {
                GpxWriter.Write(GpxTrack.FromRoute(best.Route, id, DateTimeOffset.UtcNow), stream);
            }
            var stretches = string.Join(" ", u.UrbanStretches.Select(s => $"{s.StartKm:0}-{s.EndKm:0}"));
            Console.WriteLine($"{id,-23} {(r.Found ? "sí" : "no"),-6} {u.DistanceKm,5:0.0} {u.UrbanKm,6:0.0} {u.UrbanFraction * 100,5:0} % {u.BackHomeKm,8:0.0}          {stretches}");
        }
    }
}
