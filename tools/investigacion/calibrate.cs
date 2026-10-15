#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;
// dotnet run tools/investigacion/calibrate.cs -- <salida1.gpx> <salida2.gpx> ...
// Fase 3, paso 3c-1: cuánto frena o empuja el viento al autor. Lee salidas de Garmin (con hora y altitud), las parte
// en tramos de 1 km, mide la velocidad en movimiento de cada tramo, su pendiente y el viento de cara que había según
// la previsión archivada (Open-Meteo, AROME a corto plazo; best_match si AROME no la tiene) en un punto cada 5 km.
// Ajusta: velocidad = media de la salida + a·cara + b·favor + c·pendiente, en tramos con pendiente entre -4 % y 4 %.
// No guarda nada de las salidas; escribe una tabla de tramos (sin posiciones ni horas) si se da CALIBRATE_CSV.
var inv = CultureInfo.InvariantCulture;
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var archive = new OpenMeteoPastWindForecastSource(http);
var rows = new List<(int Ride, double Speed, double Head, double Grade, double Cross)>();
var names = new List<string>();

foreach (var file in args)
{
    var pts = XDocument.Load(file).Descendants(gpx + "trkpt").Select(p => (
        Pos: new Coordinate(double.Parse(p.Attribute("lat")!.Value, inv), double.Parse(p.Attribute("lon")!.Value, inv)),
        Ele: double.Parse(p.Element(gpx + "ele")!.Value, inv),
        Time: DateTimeOffset.Parse(p.Element(gpx + "time")!.Value, inv).ToUniversalTime())).ToArray();
    var name = XDocument.Load(file).Descendants(gpx + "name").First().Value;
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
    double At(double[] values, double km)
    {
        var i = Array.BinarySearch(along, km);
        if (i >= 0) return values[i];
        i = ~i;
        if (i >= along.Length) return values[^1];
        var t = (km - along[i - 1]) / (along[i] - along[i - 1]);
        return values[i - 1] + t * (values[i] - values[i - 1]);
    }
    var ele = pts.Select(p => p.Ele).ToArray();
    var clock = pts.Select(p => (double)(p.Time - pts[0].Time).TotalHours).ToArray();

    var route = new Route(pts.Select(p => new RoutePoint(p.Pos, p.Ele)).ToList(), [], along[^1], null, "Garmin");
    var start = pts[0].Time;
    // Real times: each leg gets the clock time at which it was ridden.
    var legs = RouteLegs.Split(route, new DateTimeOffset(start.UtcDateTime, TimeSpan.Zero)).Select(l => l with
    {
        StartTime = start.AddHours(At(clock, l.StartKm)),
        EndTime = start.AddHours(At(clock, l.EndKm)),
    }).ToList();

    var points = RouteWind.SamplePoints(route);
    IReadOnlyList<PastWindForecast>? past = null;
    foreach (var model in new[] { "meteofrance_arome_france", "best_match" })
    {
        past = await archive.GetAsync(points, model, ForecastLead.ShortRange, start.AddHours(-1), pts[^1].Time.AddHours(2));
        if (past.All(p => p.Hours.Count > 0)) break;
    }
    var field = new WindField(past!.Select(p => new WindFieldPoint(p.Requested, p.Hours)).ToList());
    var winds = RouteWind.Along(legs, field);

    var ride = names.Count;
    names.Add($"{start:yyyy-MM-dd} {name} ({along[^1]:0} km, {past![0].Model})");
    foreach (var w in winds)
    {
        var hours = At(moving, w.Leg.EndKm) - At(moving, w.Leg.StartKm);
        if (hours <= 0) continue;
        var speed = w.Leg.LengthKm / hours;
        var grade = (At(ele, w.Leg.EndKm) - At(ele, w.Leg.StartKm)) / (w.Leg.LengthKm * 10);
        if (speed is < 8 or > 70) continue;
        rows.Add((ride, speed, w.HeadwindKmh, grade, Math.Abs(w.CrosswindKmh)));
    }
    var mine = rows.Where(r => r.Ride == ride).ToList();
    Console.WriteLine($"{names[ride]}: {mine.Count} tramos, {mine.Average(r => r.Speed):0.0} km/h de media en movimiento, " +
        $"viento de cara medio {mine.Average(r => r.Head):0.0} (de {mine.Min(r => r.Head):0.0} a {mine.Max(r => r.Head):0.0}) km/h");
}

if (Environment.GetEnvironmentVariable("CALIBRATE_CSV") is { } csv)
{
    File.WriteAllLines(csv, ["ride,speed_kmh,headwind_kmh,grade_pct,crosswind_kmh",
        .. rows.Select(r => string.Create(inv, $"{r.Ride},{r.Speed:0.00},{r.Head:0.00},{r.Grade:0.00},{r.Cross:0.00}"))]);
}

// Least squares: speed = v_ride + a·max(head,0) + b·min(head,0) + c·grade, on legs between -4 % and 4 %.
var fit = rows.Where(r => Math.Abs(r.Grade) <= 4).ToList();
var n = names.Count + 3;
var ata = new double[n, n];
var atb = new double[n];
foreach (var r in fit)
{
    var x = new double[n];
    x[r.Ride] = 1;
    x[names.Count] = Math.Max(r.Head, 0);
    x[names.Count + 1] = Math.Min(r.Head, 0);
    x[names.Count + 2] = r.Grade;
    for (var i = 0; i < n; i++)
    {
        atb[i] += x[i] * r.Speed;
        for (var j = 0; j < n; j++) ata[i, j] += x[i] * x[j];
    }
}
var beta = Solve(ata, atb);
var residuals = fit.Select(r => r.Speed - (beta[r.Ride] + beta[names.Count] * Math.Max(r.Head, 0)
    + beta[names.Count + 1] * Math.Min(r.Head, 0) + beta[names.Count + 2] * r.Grade)).ToList();
Console.WriteLine();
Console.WriteLine($"Ajuste con {fit.Count} tramos (pendiente entre -4 % y 4 %), error típico {Math.Sqrt(residuals.Average(e => e * e)):0.0} km/h:");
Console.WriteLine($"  cada km/h de viento de cara:   {beta[names.Count],6:0.00} km/h de velocidad");
Console.WriteLine($"  cada km/h de viento a favor:   {-beta[names.Count + 1],6:0.00} km/h de velocidad");
Console.WriteLine($"  cada 1 % de pendiente:         {beta[names.Count + 2],6:0.00} km/h de velocidad");
for (var i = 0; i < names.Count; i++) Console.WriteLine($"  velocidad sin viento ni pendiente, {names[i]}: {beta[i]:0.0} km/h");

// Plain picture: flat legs (±1 %), speed relative to the ride's own flat average, by wind along.
Console.WriteLine();
Console.WriteLine("Tramos llanos (±1 %): velocidad respecto a la media llana de su salida, por viento de cara (+) o a favor (−):");
var flat = rows.Where(r => Math.Abs(r.Grade) <= 1).ToList();
var flatMean = flat.GroupBy(r => r.Ride).ToDictionary(g => g.Key, g => g.Average(r => r.Speed));
foreach (var (lo, hi) in new[] { (-99.0, -8.0), (-8.0, -4.0), (-4.0, 0.0), (0.0, 4.0), (4.0, 8.0), (8.0, 99.0) })
{
    var bin = flat.Where(r => r.Head >= lo && r.Head < hi).ToList();
    var label = lo < -50 ? $"< {hi}" : hi > 50 ? $">= {lo}" : $"{lo} a {hi}";
    Console.WriteLine(bin.Count == 0
        ? $"  {label,-10} km/h: sin tramos"
        : $"  {label,-10} km/h: {bin.Count,4} tramos, {bin.Average(r => r.Speed - flatMean[r.Ride]),5:+0.0;-0.0} km/h");
}

static double[] Solve(double[,] a, double[] b)
{
    var n = b.Length;
    var m = (double[,])a.Clone();
    var x = (double[])b.Clone();
    for (var c = 0; c < n; c++)
    {
        var p = Enumerable.Range(c, n - c).MaxBy(r => Math.Abs(m[r, c]));
        for (var k = 0; k < n; k++) (m[c, k], m[p, k]) = (m[p, k], m[c, k]);
        (x[c], x[p]) = (x[p], x[c]);
        for (var r = 0; r < n; r++)
        {
            if (r == c) continue;
            var f = m[r, c] / m[c, c];
            for (var k = 0; k < n; k++) m[r, k] -= f * m[c, k];
            x[r] -= f * x[c];
        }
    }
    return Enumerable.Range(0, n).Select(i => x[i] / m[i, i]).ToArray();
}
