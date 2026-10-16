#:project ../../src/VientoHud.Core
// Juez de la fase 3 (DECISIONS 024): en días pasados con viento, elige y ordena las vueltas como la app con la previsión
// archivada ("salgo ahora" y "el día antes") y las repuntúa con el viento observado (METAR). Usa un BRouter local.
// dotnet run tools/investigacion/judge3.cs -- <carpeta-salida> [días por sitio]
using System.Globalization;
using System.Text;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
var daysPerPlace = args.Length > 1 ? int.Parse(args[1], inv) : 15;
const double Km = 60, Speed = 25;
// The Previous Runs archive keeps AROME a day ahead for about 90 days.
var firstDay = new DateOnly(2026, 7, 20);
var lastDay = new DateOnly(2026, 10, 14);
var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

(string Name, Coordinate Start, double MaxStationKm)[] places =
[
    ("León", new Coordinate(42.5987, -5.5671), 30),
    ("Fuenlabrada", new Coordinate(40.2842, -3.7942), 30),
    ("Tres Cantos", new Coordinate(40.6006, -3.7083), 30),
    ("La Bañeza", new Coordinate(42.3000, -5.8980), 45),
];

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var engine = new CachedEngine(new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero }));
var generator = new LoopGenerator(engine);
var stationsSource = new IemWindObservationSource(http);
var archive = new OpenMeteoPastWindForecastSource(http);

var csv = new StringBuilder("place,day,lead,stations,observed_kmh,candidates,widened,chose_best,regret_kmh,forecast_adv,observed_adv,best_adv,blind_adv,gain_kmh,observed_gusty_km,forecast_gusty_km\n");
var rows = new List<Row>();
var noLoop = new List<(string Place, ForecastLead Lead)>();

foreach (var (name, start, maxStationKm) in places)
{
    var stations = await stationsSource.FindNearestStationsAsync(start, 2, maxStationKm);
    Console.WriteLine($"== {name}: estaciones {string.Join(", ", stations.Select(s => $"{s.Id} {s.DistanceKm:0} km"))}");
    var observations = await stationsSource.GetObservationsAsync(
        stations.Select(s => s.Id).ToList(),
        new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        new DateTimeOffset(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

    // The departure each day (10:00 in Madrid) and the hours the ride covers.
    DateTimeOffset Departure(DateOnly d) => DepartureTime.Next(new TimeOnly(10, 0), new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), madrid.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue))).ToUniversalTime(), madrid);
    var hours = (int)Math.Ceiling(Km / Speed) + 1;

    // The windiest days at the nearest station during the ride.
    var days = Enumerable.Range(0, lastDay.DayNumber - firstDay.DayNumber + 1)
        .Select(i => firstDay.AddDays(i))
        .Select(d => (Day: d, Field: ObservedWind.Field(stations, observations, Departure(d), hours)))
        .Where(d => d.Field is not null)
        .Select(d => (d.Day, d.Field, Wind: d.Field!.Points[0].Hours.Average(h => h.SpeedKmh)))
        .OrderByDescending(d => d.Wind)
        .Take(daysPerPlace)
        .OrderBy(d => d.Day)
        .ToList();

    // Without the wind: bearings every 120° from north, the generator's best (phase 2). Same roads every day.
    var blindLoops = await generator.GenerateManyAsync(start, LoopGenerator.SpreadBearings(3), Km);
    var blindRoute = blindLoops.FirstOrDefault(l => l.Found)?.Best!.Route;

    foreach (var (day, observed, wind) in days)
    {
        var departure = Departure(day);
        foreach (var lead in new[] { ForecastLead.ShortRange, ForecastLead.DayAhead })
        {
            var planner = new RidePlanner(generator, new ArchivedForecast(archive, lead));
            RidePlan plan;
            try
            {
                plan = await planner.PlanAsync(start, Km, departure, Speed, 3);
            }
            catch (WindForecastException ex)
            {
                Console.WriteLine($"  {day} {Lead(lead)}: sin previsión ({ex.Message})");
                continue;
            }
            if (plan.Ranked.Count == 0)
            {
                Console.WriteLine($"  {day} {Lead(lead)}: ninguna vuelta válida");
                noLoop.Add((name, lead));
                continue;
            }
            var blindLegs = blindRoute is null ? null : RouteLegs.Split(blindRoute, departure, Speed);
            var v = RideJudge.Judge(plan.Ranked, blindLegs, observed!);
            var row = new Row(name, day, lead, wind, v, plan.Bearings.Count > 3);
            rows.Add(row);
            csv.AppendLine(string.Join(',',
                name, day.ToString("yyyy-MM-dd", inv), lead, string.Join('+', stations.Select(s => s.Id)), F(wind),
                v.Candidates, row.Widened, v.ChoseBest, F(v.RegretKmh), F(v.ForecastScore.WindAdvantageKmh),
                F(v.ObservedScore.WindAdvantageKmh), F(v.ObservedBest.WindAdvantageKmh),
                v.BlindScore is { } b ? F(b.WindAdvantageKmh) : "", v.GainOverBlindKmh is { } g ? F(g) : "",
                F(v.ObservedScore.GustyKm), F(v.ForecastScore.GustyKm)));
            Console.WriteLine($"  {day} {Lead(lead),-12} viento {wind,4:0} km/h  {(v.ChoseBest ? "acierta" : "falla  ")}  " +
                $"pérdida {v.RegretKmh,5:0.0}  ventaja real {v.ObservedScore.WindAdvantageKmh,6:+0.0;-0.0} (prevista {v.ForecastScore.WindAdvantageKmh,6:+0.0;-0.0}, " +
                $"sin mirar {v.BlindScore?.WindAdvantageKmh,6:+0.0;-0.0})");
        }
    }
}

File.WriteAllText(Path.Combine(outDir, "fase3-juez.csv"), csv.ToString());

var summary = new StringBuilder();
summary.AppendLine($"Juez de la fase 3: {firstDay:yyyy-MM-dd} a {lastDay:yyyy-MM-dd}, {daysPerPlace} días con más viento por sitio, salida 10:00, {Km} km a {Speed} km/h.");
summary.AppendLine($"{engine.Requests} peticiones a BRouter ({engine.Hits} servidas de la caché).");
summary.AppendLine();
summary.AppendLine("| Sitio | Previsión | Casos | Viento medio | Acierto | Pérdida media | Ventaja real elegida | Sin mirar el viento | Ganancia | Ventaja real > 0 | Rachas sin aviso | Sin vuelta válida |");
summary.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
foreach (var g in rows.GroupBy(r => (r.Place, r.Lead)))
{
    summary.AppendLine(Line(g.Key.Place, Lead(g.Key.Lead), g.ToList(), noLoop.Count(n => n == g.Key)));
}
foreach (var g in rows.Where(r => r.Place != "La Bañeza").GroupBy(r => r.Lead))
{
    summary.AppendLine(Line("**Todos menos La Bañeza**", Lead(g.Key), g.ToList(), noLoop.Count(n => n.Place != "La Bañeza" && n.Lead == g.Key)));
}
File.WriteAllText(Path.Combine(outDir, "fase3-juez.md"), summary.ToString());
Console.WriteLine();
Console.WriteLine(summary);

string Line(string place, string lead, List<Row> r, int withoutLoop)
{
    var withBlind = r.Where(x => x.Verdict.GainOverBlindKmh is not null).ToList();
    return $"| {place} | {lead} | {r.Count} | {r.Average(x => x.Wind):0} km/h | {r.Count(x => x.Verdict.ChoseBest) * 100.0 / r.Count:0} % | " +
        $"{r.Average(x => x.Verdict.RegretKmh):0.0} km/h | {r.Average(x => x.Verdict.ObservedScore.WindAdvantageKmh):+0.0;-0.0} km/h | " +
        $"{(withBlind.Count > 0 ? withBlind.Average(x => x.Verdict.BlindScore!.WindAdvantageKmh).ToString("+0.0;-0.0") + " km/h" : "—")} | " +
        $"{(withBlind.Count > 0 ? withBlind.Average(x => x.Verdict.GainOverBlindKmh!.Value).ToString("+0.0;-0.0") + " km/h" : "—")} | " +
        $"{r.Count(x => x.Verdict.ObservedScore.WindAdvantageKmh > 0) * 100.0 / r.Count:0} % | " +
        $"{r.Count(x => x.Verdict.ObservedScore.GustyKm > 0 && x.Verdict.ForecastScore.GustyKm == 0)} | {withoutLoop} |";
}

static string Lead(ForecastLead lead) => lead == ForecastLead.ShortRange ? "salgo ahora" : "el día antes";

static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);

record Row(string Place, DateOnly Day, ForecastLead Lead, double Wind, RideVerdict Verdict, bool Widened);

// Past forecasts (AROME, then best_match) as if they were live, for one lead.
sealed class ArchivedForecast(IPastWindForecastSource archive, ForecastLead lead) : IWindForecastProvider
{
    public async Task<WindForecast> GetHourlyAsync(Coordinate location, DateTimeOffset from, int hours, CancellationToken ct = default) =>
        (await GetHourlyAsync([location], from, hours, ct))[0];

    public async Task<IReadOnlyList<WindForecast>> GetHourlyAsync(IReadOnlyList<Coordinate> locations, DateTimeOffset from, int hours, CancellationToken ct = default)
    {
        foreach (var model in new[] { "meteofrance_arome_france", "best_match" })
        {
            var past = await archive.GetAsync(locations, model, lead, from, from.AddHours(hours), ct);
            if (past.All(p => p.Hours.Count == hours))
            {
                return past.Select(p => new WindForecast(p.Requested, p.GridPoint!.Value, 0, p.Hours, OpenMeteoPastWindForecastSource.Attribution)).ToList();
            }
        }
        throw new WindForecastException($"No archived {lead} forecast for every place and hour from {from:u}.");
    }
}

// The same waypoints give the same route: ask BRouter once.
sealed class CachedEngine(IRouteEngine inner) : IRouteEngine
{
    private readonly Dictionary<string, Task<Route>> _cache = [];
    public int Requests { get; private set; }
    public int Hits { get; private set; }

    public Task<Route> RouteAsync(IReadOnlyList<Coordinate> waypoints, CancellationToken cancellationToken = default)
    {
        var key = string.Join('|', waypoints.Select(w => string.Create(CultureInfo.InvariantCulture, $"{w.Latitude:0.000000},{w.Longitude:0.000000}")));
        Requests++;
        if (_cache.TryGetValue(key, out var cached) && cached.IsCompletedSuccessfully)
        {
            Hits++;
            return cached;
        }
        return _cache[key] = inner.RouteAsync(waypoints, cancellationToken);
    }
}
