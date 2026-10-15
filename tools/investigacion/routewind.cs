#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind.OpenMeteo;
// dotnet run tools/investigacion/routewind.cs -- <ride.gpx> <salida UTC, p. ej. 2026-10-16T08:00Z> [km/h] [carpeta donde grabar las respuestas]
// Viento previsto (Open-Meteo, AROME → best_match) en cada tramo de un GPX, con un punto cada 5 km, en una llamada.
// Si se da una carpeta, guarda allí cada respuesta de Open-Meteo tal cual (para fixtures).
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
var points = XDocument.Load(args[0]).Descendants(gpx + "trkpt")
    .Select(p => new RoutePoint(new Coordinate(
        double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
        double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture))))
    .ToList();
var route = new Route(points, [], 0, null, "GPX");
var departure = DateTimeOffset.Parse(args[1], CultureInfo.InvariantCulture).ToUniversalTime();
var speed = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : RideDistance.DefaultAverageSpeedKmh;
var save = args.Length > 3 ? args[3] : null;

var http = new HttpClient(new Recorder(save) { InnerHandler = new HttpClientHandler() });
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var legs = RouteLegs.Split(route, departure, speed);
var winds = await RouteWind.ForecastAsync(new OpenMeteoWindForecastProvider(http), route, legs);

Console.WriteLine($"{legs.Count} tramos, {RouteWind.SamplePoints(route).Count} puntos de viento, salida {departure:yyyy-MM-dd HH:mm}Z a {speed} km/h");
Console.WriteLine("  km        rumbo   hora   viento (desde)   racha   cara   lado  modelo");
foreach (var w in winds)
{
    var l = w.Leg;
    Console.WriteLine($"{l.StartKm,5:0.0}-{l.EndKm,-5:0.0} {l.BearingDegrees,4:0}° {l.MiddleTime:HH:mm}  {w.Wind.SpeedKmh,4:0} km/h {w.Wind.DirectionFromDegrees,4:0}°  {w.Wind.GustKmh,5:0}  {w.HeadwindKmh,5:0.0} {w.CrosswindKmh,6:0.0}  {w.Wind.Model}");
}
var half = legs.Count / 2;
Console.WriteLine($"Media de cara: 1ª mitad {winds.Take(half).Average(w => w.HeadwindKmh):0.0} km/h, 2ª mitad {winds.Skip(half).Average(w => w.HeadwindKmh):0.0} km/h");

class Recorder(string? folder) : DelegatingHandler
{
    private int _n;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (folder is not null)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var file = Path.Combine(folder, $"response-{++_n}.json");
            File.WriteAllText(file, body);
            Console.WriteLine($"{(int)response.StatusCode} {request.RequestUri} → {file}");
        }
        return response;
    }
}
