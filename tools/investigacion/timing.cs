#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
// dotnet run tools/investigacion/timing.cs -- <salida1.gpx> <salida2.gpx> ...
// Fase 3, paso 3c-1: ¿acierta RouteLegs la hora de paso? Para salidas de Garmin (con hora y altitud) compara, al final
// de cada tramo, el tiempo en movimiento real con el previsto a la velocidad media real de la salida: a velocidad fija
// y repartiendo el tiempo por la pendiente. No guarda nada de las salidas.
var inv = CultureInfo.InvariantCulture;
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
var departure = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
Console.WriteLine("Error en el tiempo transcurrido al final de cada tramo (min): medio | máximo");
Console.WriteLine($"{"salida",-42} {"km",4} {"km/h",5}   {"fija",13}   {"pendiente",13}");
var all = new List<(double Fixed, double Slope)>();
foreach (var file in args)
{
    var doc = XDocument.Load(file);
    var pts = doc.Descendants(gpx + "trkpt").Select(p => (
        Pos: new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv)),
        Ele: double.Parse(p.Element(gpx + "ele")!.Value, inv),
        Time: DateTimeOffset.Parse(p.Element(gpx + "time")!.Value, inv))).ToArray();
    var along = new double[pts.Length];
    var moving = new double[pts.Length];
    for (var i = 1; i < pts.Length; i++)
    {
        var km = pts[i - 1].Pos.DistanceKmTo(pts[i].Pos);
        var hours = (pts[i].Time - pts[i - 1].Time).TotalHours;
        along[i] = along[i - 1] + km;
        // Garmin pauses when stopped: a long gap with little distance is a stop, not riding.
        moving[i] = moving[i - 1] + (hours > 0 && km / hours >= 4 ? hours : 0);
    }
    double MovingAt(double km)
    {
        var i = Array.BinarySearch(along, km);
        if (i >= 0) return moving[i];
        i = ~i;
        if (i >= along.Length) return moving[^1];
        return moving[i - 1] + (km - along[i - 1]) / (along[i] - along[i - 1]) * (moving[i] - moving[i - 1]);
    }
    var speed = along[^1] / moving[^1];
    var withElevation = new Route(pts.Select(p => new RoutePoint(p.Pos, p.Ele)).ToList(), [], along[^1], null, "Garmin");
    var flat = new Route(pts.Select(p => new RoutePoint(p.Pos)).ToList(), [], along[^1], null, "Garmin");
    var errors = RouteLegs.Split(flat, departure, speed).Zip(RouteLegs.Split(withElevation, departure, speed), (f, s) => (
        Fixed: Math.Abs((f.EndTime - departure).TotalHours - MovingAt(f.EndKm)) * 60,
        Slope: Math.Abs((s.EndTime - departure).TotalHours - MovingAt(s.EndKm)) * 60)).ToList();
    all.AddRange(errors);
    var name = $"{pts[0].Time:yyyy-MM-dd} {doc.Descendants(gpx + "name").First().Value}";
    Console.WriteLine($"{name,-42} {along[^1],4:0} {speed,5:0.0}   {errors.Average(e => e.Fixed),5:0.0} | {errors.Max(e => e.Fixed),5:0.0}   {errors.Average(e => e.Slope),5:0.0} | {errors.Max(e => e.Slope),5:0.0}");
}
Console.WriteLine($"{"todas",-42} {"",4} {"",5}   {all.Average(e => e.Fixed),5:0.0} | {all.Max(e => e.Fixed),5:0.0}   {all.Average(e => e.Slope),5:0.0} | {all.Max(e => e.Slope),5:0.0}");
