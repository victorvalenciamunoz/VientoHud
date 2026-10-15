#:project ../../src/VientoHud.Core
// Fase 3, paso 3c-2: ejemplos de la puntuación. Genera vueltas en varios rumbos con un BRouter (pensado para el local),
// las puntúa en los dos sentidos con el viento previsto de un día pasado (archivo de Open-Meteo, AROME a corto plazo)
// y escribe un GPX por opción con su puesto en el nombre, para mirarlas en gpx.studio.
// dotnet run tools/investigacion/rank.cs -- <carpeta> <lat> <lon> <km> <salida UTC, p. ej. 2026-09-30T08:00Z> [rumbos] [km/h]
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
var start = new Coordinate(double.Parse(args[1], inv), double.Parse(args[2], inv));
var km = double.Parse(args[3], inv);
var departure = DateTimeOffset.Parse(args[4], inv).ToUniversalTime();
var count = args.Length > 5 ? int.Parse(args[5], inv) : 6;
var speed = args.Length > 6 ? double.Parse(args[6], inv) : RideDistance.DefaultAverageSpeedKmh;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var engine = new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero });
var results = await new LoopGenerator(engine).GenerateManyAsync(start, LoopGenerator.SpreadBearings(count), km);
var routes = results.Where(r => r.Found).ToList();
Console.WriteLine($"{routes.Count} vueltas válidas de {results.Count} rumbos ({string.Join(", ", results.Where(r => !r.Found).Select(r => $"{r.BearingDegrees:0}°"))} sin vuelta válida)");

var provider = new ArchivedForecast(new OpenMeteoPastWindForecastSource(http));
var options = await RideRanking.RankAsync(provider, routes.Select(r => r.Best!.Route).ToList(), departure, speed);
var bearingOf = routes.ToDictionary(r => r.Best!.Route, r => r.BearingDegrees);

Console.WriteLine($"Salida {departure:yyyy-MM-dd HH:mm}Z a {speed} km/h. Viento en la salida: {Describe(options[0].Winds[0].Wind)}");
Console.WriteLine(" #  rumbo  sentido       km   cara 1ª   cara 2ª   ventaja   km racha lado > 40   racha lado máx");
for (var i = 0; i < options.Count; i++)
{
    var o = options[i];
    var original = o.Reversed ? o.Route.Reversed() : o.Route;
    var bearing = bearingOf.First(kv => ReferenceEquals(kv.Key, original) || kv.Key.Points.SequenceEqual(original.Points)).Value;
    var turn = Clockwise(o.Route) ? "horario" : "antihorario";
    var maxCross = o.Winds.Max(w => w.CrossGustKmh ?? 0);
    Console.WriteLine($"{i + 1,2}  {bearing,4:0}°  {turn,-11} {o.Route.DistanceKm,5:0}   {o.Score.FirstHalfHeadwindKmh,6:+0.0;-0.0}    {o.Score.SecondHalfHeadwindKmh,6:+0.0;-0.0}    {o.Score.WindAdvantageKmh,6:+0.0;-0.0}         {o.Score.GustyKm,5:0}            {maxCross,5:0}");
    var file = Path.Combine(outDir, $"{i + 1:00}-rumbo-{bearing:000}-{turn}.gpx");
    using var f = File.Create(file);
    GpxWriter.Write(GpxTrack.FromRoute(o.Route, $"#{i + 1} rumbo {bearing:0}° {turn} (ventaja {o.Score.WindAdvantageKmh:+0;-0} km/h)", departure), f);
    if (i < 2)
    {
        foreach (var s in WindStretches.Describe(o.Winds))
        {
            Console.WriteLine($"      km {s.StartKm:0}-{s.EndKm:0}: {s.Feel} {s.MeanKmh:0} km/h, racha de lado hasta {s.MaxCrossGustKmh:0}");
        }
    }
}

static string Describe(LocalWind w) => $"{w.SpeedKmh:0} km/h desde {w.DirectionFromDegrees:0}° ({Compass.ToCompassPoint(w.DirectionFromDegrees)}), racha {w.GustKmh:0}";

// Clockwise on the map: negative signed area (shoelace) with x east and y north.
static bool Clockwise(Route r)
{
    var o = r.Points[0].Position;
    var cos = Math.Cos(o.Latitude * Math.PI / 180);
    var area = 0.0;
    foreach (var (a, b) in r.Points.Zip(r.Points.Skip(1)))
    {
        area += (a.Position.Longitude - o.Longitude) * cos * (b.Position.Latitude - o.Latitude)
              - (b.Position.Longitude - o.Longitude) * cos * (a.Position.Latitude - o.Latitude);
    }
    return area < 0;
}

// Past forecasts (AROME, then best_match) as if they were a live forecast.
sealed class ArchivedForecast(IPastWindForecastSource archive) : IWindForecastProvider
{
    public async Task<WindForecast> GetHourlyAsync(Coordinate location, DateTimeOffset from, int hours, CancellationToken ct = default) =>
        (await GetHourlyAsync([location], from, hours, ct))[0];

    public async Task<IReadOnlyList<WindForecast>> GetHourlyAsync(IReadOnlyList<Coordinate> locations, DateTimeOffset from, int hours, CancellationToken ct = default)
    {
        foreach (var model in new[] { "meteofrance_arome_france", "best_match" })
        {
            var past = await archive.GetAsync(locations, model, ForecastLead.ShortRange, from, from.AddHours(hours), ct);
            if (past.All(p => p.Hours.Count == hours))
            {
                return past.Select(p => new WindForecast(p.Requested, p.GridPoint!.Value, 0, p.Hours, OpenMeteoPastWindForecastSource.Attribution)).ToList();
            }
        }
        throw new WindForecastException("No archived forecast for every place and hour.");
    }
}
