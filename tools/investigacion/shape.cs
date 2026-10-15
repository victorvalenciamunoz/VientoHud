#:project ../../src/VientoHud.Core
// Shape of tracks (GPX or BRouter GeoJSON): length, repetition, farthest point, ratio length / farthest distance.
// dotnet run shape.cs -- <file> ...
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;

var inv = CultureInfo.InvariantCulture;
foreach (var file in args)
{
    List<Coordinate> pts;
    if (file.EndsWith(".gpx"))
    {
        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        pts = XDocument.Load(file).Descendants(ns + "trkpt")
            .Select(p => new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv))).ToList();
    }
    else
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        pts = doc.RootElement.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
            .Select(c => new Coordinate(c[1].GetDouble(), c[0].GetDouble())).ToList();
    }
    var len = 0.0;
    for (var i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceKmTo(pts[i]);
    var route = new Route(pts.Select(p => new RoutePoint(p)).ToList(),
        [new RouteSegment(0, len, new Dictionary<string, string> { ["highway"] = "tertiary" })], len, null, "x");
    var a = RouteAnalyzer.Analyze(route, []);
    var start = pts[0];
    var far = pts.MaxBy(p => p.DistanceKmTo(start));
    var farKm = far.DistanceKmTo(start);
    var bearing = (Math.Atan2((far.Longitude - start.Longitude) * Math.Cos(start.Latitude * Math.PI / 180), far.Latitude - start.Latitude) * 180 / Math.PI + 360) % 360;
    // Enclosed area (shoelace on local km) vs perimeter: 1 = circle, 0 = out-and-back line.
    var k = 6371.0088 * Math.PI / 180; var c0 = Math.Cos(start.Latitude * Math.PI / 180);
    var xy = pts.Select(p => ((p.Longitude - start.Longitude) * c0 * k, (p.Latitude - start.Latitude) * k)).ToList();
    var area = 0.0;
    for (var i = 0; i < xy.Count; i++) { var (x1, y1) = xy[i]; var (x2, y2) = xy[(i + 1) % xy.Count]; area += x1 * y2 - x2 * y1; }
    area = Math.Abs(area) / 2;
    var roundness = 4 * Math.PI * area / (len * len);
    Console.WriteLine($"{Path.GetFileName(file),-45} {len,6:0.0} km  repeated {a.RepeatedFraction,6:P0}  farthest {farKm,5:0.0} km at {bearing,3:0}°  length/farthest {len / farKm,4:0.0}  roundness {roundness:0.00}  end-start {pts[^1].DistanceKmTo(start):0.00} km  points {pts.Count}");
}
