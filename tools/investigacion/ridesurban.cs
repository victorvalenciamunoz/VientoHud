#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
// dotnet run tools/investigacion/ridesurban.cs -- <ride.gpx> ...
// Fase 6, paso 6c: cuánta ciudad tienen las salidas reales del autor, con la misma medida que las rutas generadas
// (RouteUrban). Cada salida se pasa por el BRouter local con un punto de paso cada km (para tener las etiquetas de vías y
// nodos) y se dice cuánto del resultado queda sobre la salida (a menos de 50 m), para fiarse de la medida.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var engine = new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero });
XNamespace gpx = "http://www.topografix.com/GPX/1/1";

Console.WriteLine("salida                         km    ciudad km   %    vuelve a casa  sobre la salida  tramos de ciudad");
foreach (var file in args)
{
    var ride = XDocument.Load(file).Descendants(gpx + "trkpt")
        .Select(p => new Coordinate(
            double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
            double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture)))
        .ToList();
    // A waypoint every km along the ride, and its last point.
    var waypoints = new List<Coordinate> { ride[0] };
    var along = 0.0;
    for (var i = 1; i < ride.Count; i++)
    {
        along += ride[i - 1].DistanceKmTo(ride[i]);
        if (along >= 1)
        {
            waypoints.Add(ride[i]);
            along = 0;
        }
    }
    waypoints.Add(ride[^1]);

    Route route;
    try
    {
        route = await engine.RouteAsync(waypoints);
    }
    catch (RouteException ex)
    {
        Console.WriteLine($"{Path.GetFileName(file),-30} sin ruta: {ex.Message}");
        continue;
    }
    // Share of the match within 50 m of the ride (every 10th point is enough).
    var sample = route.Points.Where((_, i) => i % 10 == 0).ToList();
    var onRide = sample.Count(p => ride.Min(r => r.DistanceKmTo(p.Position)) <= 0.05) / (double)sample.Count;
    var u = RouteUrban.Measure(route);
    Console.WriteLine($"{Path.GetFileName(file),-30} {u.DistanceKm,5:0.0} {u.UrbanKm,6:0.0} {u.UrbanFraction * 100,5:0} % {u.BackHomeKm,8:0.0}       {onRide * 100,6:0} %       " +
                      string.Join(" ", u.UrbanStretches.Select(s => $"{s.StartKm:0}-{s.EndKm:0}")));
}
