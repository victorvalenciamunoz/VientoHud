#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
// dotnet run tools/investigacion/legs.cs -- <ride.gpx> [salida UTC, p. ej. 2026-10-15T08:00Z] [km/h]
// Tramos de ~1 km de un GPX (RouteLegs): km, rumbo HACIA, rectitud y hora de paso (UTC).
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
var points = XDocument.Load(args[0]).Descendants(gpx + "trkpt")
    .Select(p => new RoutePoint(new Coordinate(
        double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
        double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture))))
    .ToList();
var departure = args.Length > 1 ? DateTimeOffset.Parse(args[1], CultureInfo.InvariantCulture).ToUniversalTime() : new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
var speed = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : RideDistance.DefaultAverageSpeedKmh;
var legs = RouteLegs.Split(new Route(points, [], 0, null, "GPX"), departure, speed);
Console.WriteLine($"{legs.Count} tramos de {legs[0].LengthKm:0.000} km, salida {departure:HH:mm}Z a {speed} km/h");
Console.WriteLine("  km      rumbo  rectitud  hora (mitad)");
foreach (var leg in legs)
{
    Console.WriteLine($"{leg.StartKm,5:0.0}-{leg.EndKm,-5:0.0} {leg.BearingDegrees,4:0}° {Compass.ToCompassPoint(leg.BearingDegrees),-3} {leg.Straightness,5:0.00}   {leg.MiddleTime:HH:mm}");
}
