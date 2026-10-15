#:project ../../src/VientoHud.Core
// Where a track repeats itself: the "string" of a balloon (leaving and coming home by the same road) versus
// out-and-back spurs in the middle (stars). Reads GPX or BRouter GeoJSON.
// dotnet run tools/investigacion/stem.cs -- <file> ...
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using VientoHud.Core;

var inv = CultureInfo.InvariantCulture;
const double Spacing = 0.1, Tolerance = 0.03, Separation = 1.0;

foreach (var file in args)
{
    var pts = Load(file);
    // Local plane, km.
    var o = pts[0]; var k = 6371.0088 * Math.PI / 180; var c = Math.Cos(o.Latitude * Math.PI / 180);
    var xy = pts.Select(p => ((p.Longitude - o.Longitude) * c * k, (p.Latitude - o.Latitude) * k)).ToArray();
    var along = new double[xy.Length];
    for (var i = 1; i < xy.Length; i++) along[i] = along[i - 1] + Dist(xy[i - 1], xy[i]);
    var L = along[^1];
    var closed = Dist(xy[0], xy[^1]) < 0.3;

    // For every checked point: is it repeated, and where along the track is the other pass (its "partner")?
    var n = (int)Math.Floor(L / Spacing + 1e-9) + 1;
    var partner = new double?[n];
    for (var j = 0; j < n; j++)
    {
        var s = j * Spacing; var p = At(s);
        var best = double.MaxValue; double bestU = 0;
        for (var i = 1; i < xy.Length; i++)
        {
            var (d, u) = SegDist(p, xy[i - 1], xy[i], along[i - 1], along[i]);
            var sep = Math.Abs(u - s); if (closed) sep = Math.Min(sep, L - sep);
            if (sep > Separation && d < best) { best = d; bestU = u; }
        }
        if (best < Tolerance) partner[j] = bestU;
    }

    // The string: from each end, the run of repeated points whose other pass is at the other end of the track.
    // Gaps under 1 km are allowed (the 0.5 km nearest home never count as repeated).
    bool InTail(double u) => u > L / 2; bool InHead(double u) => u < L / 2;
    var head = 0.0; var gap = 0.0;
    for (var j = 0; j < n && j * Spacing < L / 2; j++)
    {
        if (partner[j] is { } u && InTail(u)) { head = j * Spacing; gap = 0; } else if ((gap += Spacing) > 1.0) break;
    }
    var tail = L; gap = 0;
    for (var j = n - 1; j >= 0 && j * Spacing > L / 2; j--)
    {
        if (partner[j] is { } u && InHead(u)) { tail = j * Spacing; gap = 0; } else if ((gap += Spacing) > 1.0) break;
    }
    var repeated = partner.Count(p => p is not null) * Spacing;
    var middle = Enumerable.Range(0, n).Count(j => partner[j] is not null && j * Spacing > head && j * Spacing < tail) * Spacing;

    // Strip: one character per 1/60 of the route. '=' string, '#' repeated elsewhere, '.' ridden once.
    var strip = new string(Enumerable.Range(0, 60).Select(b =>
    {
        var js = Enumerable.Range(0, n).Where(j => j * Spacing >= b * L / 60 && j * Spacing < (b + 1) * L / 60).ToList();
        var rep = js.Count(j => partner[j] is not null);
        if (rep * 2 < js.Count || js.Count == 0) return '.';
        return js.All(j => j * Spacing <= head || j * Spacing >= tail) ? '=' : '#';
    }).ToArray());

    Console.WriteLine($"{Path.GetFileName(file),-34} {L,6:0.0} km  repeated {repeated,5:0.0} km ({repeated / L,4:P0})  string {head:0.0} + {L - tail:0.0} km  repeated outside the string {middle,5:0.0} km ({middle / L,4:P0})  end-start {Dist(xy[0], xy[^1]):0.0} km");
    Console.WriteLine($"{"",34} |{strip}|");

    (double, double) At(double s)
    {
        var i = Array.BinarySearch(along, s); if (i >= 0) return xy[i]; i = ~i;
        if (i >= along.Length) return xy[^1];
        var t = (s - along[i - 1]) / (along[i] - along[i - 1]);
        return (xy[i - 1].Item1 + t * (xy[i].Item1 - xy[i - 1].Item1), xy[i - 1].Item2 + t * (xy[i].Item2 - xy[i - 1].Item2));
    }
}

static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

// Distance from p to segment ab, and the along-track position of the closest point.
static (double, double) SegDist((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, double sa, double sb)
{
    var dx = b.X - a.X; var dy = b.Y - a.Y; var l2 = dx * dx + dy * dy;
    var t = l2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
    return (Dist(p, (a.X + t * dx, a.Y + t * dy)), sa + t * (sb - sa));
}

List<Coordinate> Load(string file)
{
    if (file.EndsWith(".gpx"))
    {
        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        return XDocument.Load(file).Descendants(ns + "trkpt")
            .Select(p => new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv))).ToList();
    }
    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    return doc.RootElement.GetProperty("features")[0].GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
        .Select(c => new Coordinate(c[1].GetDouble(), c[0].GetDouble())).ToList();
}
