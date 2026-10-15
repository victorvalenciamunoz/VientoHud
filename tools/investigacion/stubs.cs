#:project ../../src/VientoHud.Core
// Short out-and-back stubs in a GPX: places where the track turns back on itself (it goes in a few hundred metres and
// comes out the same way). RouteAnalyzer ignores repetition closer than 1 km along the track, so it misses these.
// For each turn: where it is, and how far the track runs in before coming back the same way.
// dotnet run tools/investigacion/stubs.cs -- <file.gpx> ...
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;

var inv = CultureInfo.InvariantCulture;
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
foreach (var file in args)
{
    var pts = XDocument.Load(file).Descendants(gpx + "trkpt")
        .Select(p => new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv)))
        .ToList();
    var along = new double[pts.Count];
    for (var i = 1; i < pts.Count; i++) along[i] = along[i - 1] + pts[i - 1].DistanceKmTo(pts[i]);
    Coordinate At(double s)
    {
        s = Math.Clamp(s, 0, along[^1]);
        var i = Array.BinarySearch(along, s);
        if (i >= 0) return pts[i];
        i = ~i;
        var t = (s - along[i - 1]) / (along[i] - along[i - 1]);
        return new Coordinate(pts[i - 1].Latitude + t * (pts[i].Latitude - pts[i - 1].Latitude), pts[i - 1].Longitude + t * (pts[i].Longitude - pts[i - 1].Longitude));
    }

    // A turn at s: 30 m before and after are the same place, and the track did move away (not a standstill).
    var stubs = new List<(double At, double Depth, Coordinate Where)>();
    for (var s = 0.05; s < along[^1] - 0.05; s += 0.01)
    {
        if (At(s - 0.03).DistanceKmTo(At(s + 0.03)) > 0.015) continue;
        if (stubs.Count > 0 && s - stubs[^1].At < 0.2) continue;
        // How deep: the largest d with the points d before and d after still together.
        var d = 0.03;
        while (s - d > 0 && s + d < along[^1] && At(s - d).DistanceKmTo(At(s + d)) < 0.03) d += 0.01;
        stubs.Add((s, d - 0.01, At(s)));
    }
    Console.WriteLine($"== {Path.GetFileName(file)}: {along[^1]:0.0} km, {stubs.Count} turns back");
    foreach (var (s, depth, where) in stubs)
        Console.WriteLine($"   km {s,5:0.00}: in and out {depth * 1000,5:0} m each way at {where.Latitude:0.00000},{where.Longitude:0.00000}");
}
