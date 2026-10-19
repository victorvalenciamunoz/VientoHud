#:project ../../src/VientoHud.Core
using System.Diagnostics;
using VientoHud.Core;
using VientoHud.Core.Routing.BRouter;
// dotnet run tools/investigacion/brouterspeed.cs -- <url BRouter> [<url BRouter>...]
// Fase 7: cuánto tarda UNA petición a cada servidor de BRouter con el perfil de Core, en Fuenlabrada y León: tres idas
// (23 km, la mitad de una ida y vuelta de 60 km) y una vuelta de 60 km por 4 puntos. Pocas peticiones y 1,5 s entre
// ellas (brouter.de es público): el tiempo medido es solo el de cada petición, sin la espera.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");

(string Name, Coordinate Start)[] places = [("fuenlabrada", new(40.2842, -3.7942)), ("leon", new(42.5987, -5.5671))];

foreach (var url in args)
{
    var engine = new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(url), MinRequestInterval = TimeSpan.Zero });
    Console.WriteLine($"== {url}");
    foreach (var (name, start) in places)
    {
        var requests = new List<(string What, Coordinate[] Waypoints)>
        {
            ("ida 23 km E", [start, start.Destination(90, 23)]),
            ("ida 23 km S", [start, start.Destination(180, 23)]),
            ("ida 23 km O", [start, start.Destination(270, 23)]),
            // About 60 km by road: a square of 4 points 9 km from home, round it and back.
            ("vuelta 60 km", [start, start.Destination(45, 9), start.Destination(135, 9), start.Destination(225, 9), start.Destination(315, 9), start]),
        };
        foreach (var (what, waypoints) in requests)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var route = await engine.RouteAsync(waypoints);
                Console.WriteLine($"{name,-12} {what,-14} {clock.Elapsed.TotalSeconds,6:0.00} s  {route.DistanceKm,6:0.0} km");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name,-12} {what,-14} {clock.Elapsed.TotalSeconds,6:0.00} s  ERROR {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(1.5));
        }
    }
}
