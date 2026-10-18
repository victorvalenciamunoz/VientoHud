#:project ../../src/VientoHud.Core
// Juez de la fase 5 (paso 5e): en días pasados con lluvia, la app decide a las 9:00 y a las 12:00 (hora local) con lo que
// se sabía entonces —vueltas por el viento previsto (archivo "salgo ahora"), radar de 5 min antes y su movimiento, la
// última pasada archivada de AROME HD 15 min (cada 3 h) y la reserva de best_match— qué vuelta, en qué sentido y si
// salir ya o esperar (hasta 2 h). Luego se mira lo que pasó: km mojados según el radar en el medio de cada tramo a la
// hora de paso, y la ventaja de viento con el viento de los METAR. Rival: salir a esa hora con la vuelta elegida sin
// mirar el tiempo (rumbos cada 120° desde el norte, la mejor del generador). Usa un BRouter local; guarda en disco las
// respuestas de Open-Meteo y los recortes de radar.
// dotnet run tools/investigacion/judge5.cs -- <carpeta salida> <carpeta caché> [días por sitio]
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Observations;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
var cache = args[1];
var daysPerPlace = args.Length > 2 ? int.Parse(args[2], inv) : 7;
Directory.CreateDirectory(outDir);
const double Km = 60, Speed = 25;
var firstDay = new DateOnly(2026, 7, 16);
var lastDay = new DateOnly(2026, 10, 15);
var madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
TimeOnly[] decisionTimes = [new(9, 0), new(12, 0)];

(string Name, Coordinate Start, double MaxStationKm)[] places =
[
    ("La Bañeza", new Coordinate(42.3000, -5.8980), 45),
    ("Humanes", new Coordinate(40.2526, -3.8299), 20),
];

using var http = new HttpClient(new DiskCache(Path.Combine(cache, "http")) { InnerHandler = new HttpClientHandler() })
{
    Timeout = TimeSpan.FromSeconds(180),
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";
var engine = new CachedEngine(new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero }));
var generator = new LoopGenerator(engine);
var stationsSource = new IemWindObservationSource(http);
var precipitation = new IemPrecipitationObservationSource(http);
var windArchive = new OpenMeteoPastWindForecastSource(http);
var forecasts = new ArchivedForecast(windArchive, ForecastLead.ShortRange);
var opera = new OperaRadarSource(http);
var fallback = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.Archive);
var threshold = RainNearby.DefaultThresholdMmPerHour;

var csv = new StringBuilder("sitio,dia,decision,espera_min,rumbo,km,lluvia_prevista_km,mojado_app_km,mojado_app_sin_esperar_km,mojado_sin_mirar_km," +
                            "ventaja_app,ventaja_sin_mirar,rachas_app_km,rachas_sin_mirar_km,aviso_zona\n");
var rows = new List<Row>();

foreach (var (name, start, maxStationKm) in places)
{
    var stations = await stationsSource.FindNearestStationsAsync(start, 2, maxStationKm);
    Console.WriteLine($"== {name}: estaciones {string.Join(", ", stations.Select(s => $"{s.Id} {s.DistanceKm:0} km"))}");
    var from = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    var to = new DateTimeOffset(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    var windObservations = await stationsSource.GetObservationsAsync(stations.Select(s => s.Id).ToList(), from, to);
    var reports = await precipitation.GetReportsAsync(stations.Select(s => s.Id).ToList(), from, to);

    // The rainiest days at the stations while one rides (06-17 UTC).
    var days = reports.Where(r => r.Precipitation && r.Time.Hour is >= 6 and < 17)
        .GroupBy(r => DateOnly.FromDateTime(r.Time.UtcDateTime))
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
        .Take(daysPerPlace)
        .Select(g => g.Key)
        .Order()
        .ToList();
    Console.WriteLine($"   días: {string.Join(", ", days.Select(d => d.ToString("dd/MM", inv)))}");

    // Without the weather: bearings every 120° from north, the generator's best. Same roads every day.
    var blindLoops = await generator.GenerateManyAsync(start, LoopGenerator.SpreadBearings(3), Km);
    var blindRoute = blindLoops.FirstOrDefault(l => l.Found)?.Best!.Route;

    foreach (var day in days)
    {
        foreach (var local in decisionTimes)
        {
            var wall = day.ToDateTime(local);
            var now = new DateTimeOffset(wall, madrid.GetUtcOffset(wall)).ToUniversalTime();
            var label = $"{name} {day:dd/MM} {local:HH\\:mm}";
            try
            {
                var row = await JudgeAsync(name, start, now, stations, windObservations, blindRoute);
                if (row is null)
                {
                    Console.WriteLine($"  {label}: ninguna vuelta válida");
                    continue;
                }
                rows.Add(row);
                csv.AppendLine(string.Join(',', name, day.ToString("yyyy-MM-dd", inv), local.ToString("HH:mm", inv),
                    row.WaitMinutes.ToString("0", inv), row.Bearing.ToString("0", inv), F(row.RideKm), F(row.PredictedLikelyKm),
                    F(row.WetKm), F(row.WetKmIfNow), row.BlindWetKm is { } bw ? F(bw) : "",
                    row.Advantage is { } a ? F(a) : "", row.BlindAdvantage is { } ba ? F(ba) : "",
                    row.GustyKm is { } g ? F(g) : "", row.BlindGustyKm is { } bg ? F(bg) : "", row.AreaWarning));
                Console.WriteLine($"  {label}: {(row.WaitMinutes > 0 ? $"espera {row.WaitMinutes:0} min" : "sale ya"),-15} " +
                                  $"mojado {row.WetKm,4:0} km (sin esperar {row.WetKmIfNow,4:0}, sin mirar {row.BlindWetKm,4:0}); " +
                                  $"viento {row.Advantage,5:+0.0;-0.0} (sin mirar {row.BlindAdvantage,5:+0.0;-0.0}); prevista {row.PredictedLikelyKm:0} km");
            }
            catch (Exception ex) when (ex is WindForecastException or RadarException or RainForecastException or RouteException)
            {
                Console.WriteLine($"  {label}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

File.WriteAllText(Path.Combine(outDir, "casos.csv"), csv.ToString());

var md = new StringBuilder();
md.AppendLine($"# Juez de la fase 5 ({firstDay:yyyy-MM-dd} a {lastDay:yyyy-MM-dd})");
md.AppendLine();
md.AppendLine($"{daysPerPlace} días con más lluvia (METAR, 06-17 UTC) por sitio; decisiones a las 9:00 y 12:00 locales; {Km} km a {Speed} km/h; " +
              $"esperas de hasta 2 h. {engine.Requests} peticiones a BRouter ({engine.Hits} de la caché).");
md.AppendLine();
md.AppendLine("Km mojados según el radar (celda del medio de cada tramo a la hora de paso, desde 0,2 mm/h); ventaja de viento real (METAR).");
md.AppendLine();
md.AppendLine("| Sitio | Casos | Espera | Mojado app | Mojado app sin esperar | Mojado sin mirar | App ≤ sin mirar | Ventaja app | Ventaja sin mirar |");
md.AppendLine("|---|---|---|---|---|---|---|---|---|");
foreach (var g in rows.GroupBy(r => r.Place).Append(rows.GroupBy(_ => "Los dos").Single()))
{
    var r = g.ToList();
    var withBlind = r.Where(x => x.BlindWetKm is not null).ToList();
    var withWind = r.Where(x => x.Advantage is not null && x.BlindAdvantage is not null).ToList();
    md.AppendLine($"| {g.Key} | {r.Count} | {r.Count(x => x.WaitMinutes > 0)} | {r.Average(x => x.WetKm):0.0} km | " +
                  $"{r.Average(x => x.WetKmIfNow):0.0} km | {(withBlind.Count > 0 ? $"{withBlind.Average(x => x.BlindWetKm!.Value):0.0} km" : "—")} | " +
                  $"{(withBlind.Count > 0 ? $"{withBlind.Count(x => x.WetKm <= x.BlindWetKm!.Value + 1e-9) * 100.0 / withBlind.Count:0} %" : "—")} | " +
                  $"{(withWind.Count > 0 ? $"{withWind.Average(x => x.Advantage!.Value):+0.0;-0.0} km/h" : "—")} | " +
                  $"{(withWind.Count > 0 ? $"{withWind.Average(x => x.BlindAdvantage!.Value):+0.0;-0.0} km/h" : "—")} |");
}
var waits = rows.Where(r => r.WaitMinutes > 0).ToList();
md.AppendLine();
md.AppendLine(waits.Count == 0
    ? "Nunca esperó."
    : $"Cuando esperó ({waits.Count} veces, {waits.Average(w => w.WaitMinutes):0} min de media): mojado {waits.Average(w => w.WetKm):0.0} km " +
      $"frente a {waits.Average(w => w.WetKmIfNow):0.0} km saliendo ya con su mejor vuelta; esperar ayudó en {waits.Count(w => w.WetKm < w.WetKmIfNow)}, " +
      $"empeoró en {waits.Count(w => w.WetKm > w.WetKmIfNow)}.");
md.AppendLine();
md.AppendLine($"{OperaRadarSource.AttributionText}. {OpenMeteoRainForecastSource.Attribution}. {IemPrecipitationObservationSource.Attribution}. " +
              "Routing: BRouter · Map data © OpenStreetMap contributors (ODbL).");
File.WriteAllText(Path.Combine(outDir, "resumen.md"), md.ToString());
Console.WriteLine();
Console.WriteLine(md);

async Task<Row?> JudgeAsync(
    string place, Coordinate start, DateTimeOffset now, IReadOnlyList<WeatherStation> stations,
    IReadOnlyList<WindObservation> windObservations, Route? blindRoute)
{
    var plan = await new RidePlanner(generator, forecasts).PlanAsync(start, Km, now, Speed, 3);
    var found = Enumerable.Range(0, plan.Loops.Count).Where(i => plan.Loops[i].Found).ToList();
    if (found.Count == 0)
    {
        return null;
    }
    var routes = found.Select(i => plan.Loops[i].Best!.Route).ToList();
    var departures = DepartureChoice.Times(now);

    // What was known at `now`: the radar of 5 min before, the last archived AROME run already out (~35 min late).
    var radar = new PastRadar(opera, cache, place, start, now - TimeSpan.FromMinutes(5));
    var lastRun = now - TimeSpan.FromMinutes(40);
    var run = new DateTimeOffset(lastRun.Year, lastRun.Month, lastRun.Day, lastRun.Hour / 3 * 3, 0, 0, TimeSpan.Zero);
    var model = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.SingleRun(run));
    var reach = routes.SelectMany(r => r.Points).Max(p => p.Position.DistanceKmTo(start));
    var until = routes.Max(r => RouteLegs.Split(r, departures[^1], Speed)[^1].EndTime);
    var outlook = await RainOutlook.ForecastAsync(radar, model, fallback, start, reach, now, until);
    var advice = await DepartureChoice.ChooseAsync(forecasts, routes, departures, outlook, Speed);
    if (advice.Chosen.Best is not { } chosen || advice.First.Best is not { } ifNow)
    {
        return null;
    }

    // What happened.
    var observedWind = ObservedWind.Field(stations, windObservations,
        new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero), 7);
    var chosenLegs = chosen.Winds.Select(w => w.Leg).ToList();
    var blindLegs = blindRoute is null ? null : RouteLegs.Split(blindRoute, now, Speed);
    var loop = plan.Loops[found[chosen.RouteIndex]];
    return new Row(
        place,
        (advice.Chosen.Departure - now).TotalMinutes,
        loop.BearingDegrees,
        chosenLegs[^1].EndKm,
        chosen.LikelyRainKm,
        await WetKmAsync(radar, chosenLegs),
        await WetKmAsync(radar, ifNow.Winds.Select(w => w.Leg).ToList()),
        blindLegs is null ? null : await WetKmAsync(radar, blindLegs),
        observedWind is null ? null : RideJudge.Rescore(chosenLegs, observedWind).WindAdvantageKmh,
        observedWind is null || blindLegs is null ? null : RideJudge.Rescore(blindLegs, observedWind).WindAdvantageKmh,
        observedWind is null ? null : RideJudge.Rescore(chosenLegs, observedWind).GustyKm,
        observedWind is null || blindLegs is null ? null : RideJudge.Rescore(blindLegs, observedWind).GustyKm,
        chosen.Rain?.AreaWarning is not null);
}

// Km of legs the radar saw wet at their middle, with the frame every 10 min nearest before the time they were ridden.
async Task<double> WetKmAsync(PastRadar radar, IReadOnlyList<RouteLeg> legs)
{
    var wet = 0.0;
    foreach (var leg in legs)
    {
        var frame = await radar.FrameAsync(RadarFrames.AtOrBefore(leg.MiddleTime, TimeSpan.FromMinutes(10)));
        if (frame?.RateAt(leg.Middle) is { } rate && !float.IsNaN(rate) && rate >= threshold)
        {
            wet += leg.LengthKm;
        }
    }
    return wet;
}

static string F(double x) => x.ToString("0.0", CultureInfo.InvariantCulture);

record Row(
    string Place, double WaitMinutes, double Bearing, double RideKm, double PredictedLikelyKm,
    double WetKm, double WetKmIfNow, double? BlindWetKm, double? Advantage, double? BlindAdvantage,
    double? GustyKm, double? BlindGustyKm, bool AreaWarning);

// OPERA as it was at a past moment, with crops around the start kept on disk (gzip of the rates); frames after `latest`
// do not exist yet for the forecast, but the judge can still read them (FrameAsync) to see what happened.
sealed class PastRadar(OperaRadarSource inner, string cache, string place, Coordinate center, DateTimeOffset latest) : IRadarSource
{
    private readonly Dictionary<DateTimeOffset, RainGrid?> _memory = [];

    public TimeSpan FrameInterval => inner.FrameInterval;
    public string Attribution => inner.Attribution;

    public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RadarFrames.AtOrBefore(latest, inner.FrameInterval));

    public async Task<RainGrid?> GetFrameAsync(DateTimeOffset time, Coordinate c, double radiusKm, CancellationToken cancellationToken = default) =>
        time > latest ? null : await FrameAsync(time);

    public async Task<RainGrid?> FrameAsync(DateTimeOffset time)
    {
        if (_memory.TryGetValue(time, out var known))
        {
            return known;
        }
        var path = Path.Combine(cache, "radar", place, $"{time:yyyyMMdd'T'HHmm}.bin.gz");
        RainGrid? grid;
        if (File.Exists(path + ".missing"))
        {
            grid = null;
        }
        else if (File.Exists(path))
        {
            await using var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
            using var reader = new BinaryReader(input);
            var projection = new LambertAzimuthalEqualArea(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
            double left = reader.ReadDouble(), top = reader.ReadDouble(), cell = reader.ReadDouble();
            int rows = reader.ReadInt32(), columns = reader.ReadInt32();
            var rates = new float[rows * columns];
            for (var i = 0; i < rates.Length; i++)
            {
                rates[i] = reader.ReadSingle();
            }
            grid = new RainGrid(time, projection, left, top, cell, rows, columns, rates);
        }
        else
        {
            grid = await inner.GetFrameAsync(time, center, RainOutlook.RadarRadiusKm);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (grid is null)
            {
                await File.WriteAllTextAsync(path + ".missing", "");
            }
            else
            {
                await using (var output = new GZipStream(File.Create(path + ".part"), CompressionLevel.Fastest))
                await using (var writer = new BinaryWriter(output))
                {
                    var p = grid.Projection;
                    writer.Write(p.CenterLatitude);
                    writer.Write(p.CenterLongitude);
                    writer.Write(p.FalseEasting);
                    writer.Write(p.FalseNorthing);
                    writer.Write(grid.Left);
                    writer.Write(grid.Top);
                    writer.Write(grid.CellSizeMeters);
                    writer.Write(grid.Rows);
                    writer.Write(grid.Columns);
                    for (var r = 0; r < grid.Rows; r++)
                    {
                        for (var c = 0; c < grid.Columns; c++)
                        {
                            writer.Write(grid[r, c]);
                        }
                    }
                }
                File.Move(path + ".part", path, overwrite: true);
            }
        }
        _memory[time] = grid;
        return grid;
    }
}

// Open-Meteo and IEM answers kept on disk by URL, so a second run asks nothing (and stays under the daily limits).
sealed class DiskCache(string folder) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (request.Method != HttpMethod.Get || !(uri.Host.Contains("open-meteo") || uri.Host.Contains("iastate")))
        {
            return await base.SendAsync(request, cancellationToken);
        }
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))[..32];
        var path = Path.Combine(folder, key);
        if (File.Exists(path))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancellationToken)) };
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, body, cancellationToken);
            return new HttpResponseMessage(response.StatusCode) { Content = new ByteArrayContent(body) };
        }
        return response;
    }
}

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
