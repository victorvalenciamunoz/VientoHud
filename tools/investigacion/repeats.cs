#:project ../../src/VientoHud.Core
// Prints RouteAnalyzer's repeated stretches of a GPX file.
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
foreach (var file in args)
{
    var pts = XDocument.Load(file).Descendants(gpx + "trkpt")
      .Select(p => new RoutePoint(new Coordinate(double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture), double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture)))).ToList();
    var km = pts.Zip(pts.Skip(1), (a, b) => a.Position.DistanceKmTo(b.Position)).Sum();
    var a = RouteAnalyzer.Analyze(new Route(pts, [], km, null, "x"), []);
    Console.WriteLine($"== {Path.GetFileName(file)} {a.DistanceKm:0.0} km  repeated {a.RepeatedFraction:P1}  spurs {a.SpurFraction:P1}");
    foreach (var r in a.Repeats) Console.WriteLine($"   km {r.StartKm,5:0.0}-{r.EndKm,5:0.0}  other {r.OtherPassStartKm,5:0.0}-{r.OtherPassEndKm,5:0.0}  {(r.IsSpur ? "SPUR" : "ok")}");
}
