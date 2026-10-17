#:project ../../src/VientoHud.Core
using System.Globalization;
using System.IO.Compression;
using System.Text;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Observations;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Rain.Verification;
// dotnet run tools/investigacion/judge4.cs -- <carpeta salida> [carpeta caché de recortes] [desde aaaa-mm-dd] [hasta aaaa-mm-dd]
// Juez de la fase 4 (DECISIONS 031). Días con lluvia en el METAR de Madrid (Barajas, Getafe, Cuatro Vientos, Colmenar)
// y León; cada 30 min desde 2 h antes de la primera lluvia del día hasta 2 h después de la última. En cada momento T se
// toma la imagen de radar de T y se "sale" a T+5 (lo que tarda en publicarse); se prevé la lluvia en cada aeropuerto a
// +30, +60 y +90 min con:
//   movida      el movimiento de la última media hora (como `radar`), con tres tamaños del círculo;
//   quieta      la misma previsión sin movimiento;
//   arome       AROME HD cada 15 min (archivo de Open-Meteo): 100 % si su lluvia llega a 0,2 mm/h, 0 % si no.
// y se compara con lo que vio el radar en la celda del aeropuerto y con el METAR (precipitación a ±15 min).
// Escribe casos.csv y resumen.md. Guarda los recortes de radar comprimidos para poder repetir sin descargar.
var output = args[0];
var cropCache = args.Length > 1 ? args[1] : Path.Combine(output, "recortes");
var firstDay = args.Length > 2 ? DateOnly.Parse(args[2], CultureInfo.InvariantCulture) : new DateOnly(2026, 7, 16);
var lastDay = args.Length > 3 ? DateOnly.Parse(args[3], CultureInfo.InvariantCulture) : new DateOnly(2026, 10, 15);
Directory.CreateDirectory(output);
Directory.CreateDirectory(cropCache);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var radar = new OperaRadarSource(http);
var metar = new IemPrecipitationObservationSource(http);
var arome = new OpenMeteoPastRainForecastSource(http);

const double RadiusKm = 150;
var threshold = RainNearby.DefaultThresholdMmPerHour;
int[] leads = [30, 60, 90];
var publishDelay = TimeSpan.FromMinutes(5);

(string Name, Coordinate Center, (string Id, Coordinate Position)[] Stations)[] places =
[
    ("Madrid", new(40.45, -3.68),
    [
        ("LEMD", new(40.46667, -3.55556)), ("LEGT", new(40.3, -3.7225)),
        ("LEVS", new(40.37778, -3.78917)), ("LECV", new(40.69861, -3.76444)),
    ]),
    ("León", new(42.58889, -5.64944), [("LELN", new(42.58889, -5.64944))]),
];
var models = new (string Name, RainMotionUse Motion, NowcastOptions? Options)[]
{
    ("movida", RainMotionUse.Moving, new NowcastOptions()),
    ("movida-pequeño", RainMotionUse.Moving, new NowcastOptions { RadiusKm = 1, RadiusGrowthKmPerHour = 2.5 }),
    ("movida-grande", RainMotionUse.Moving, new NowcastOptions { RadiusKm = 3, RadiusGrowthKmPerHour = 10 }),
    ("movida-4+15", RainMotionUse.Moving, new NowcastOptions { RadiusKm = 4, RadiusGrowthKmPerHour = 15 }),
    ("movida-5+20", RainMotionUse.Moving, new NowcastOptions { RadiusKm = 5, RadiusGrowthKmPerHour = 20 }),
    ("quieta", RainMotionUse.Still, new NowcastOptions()),
    ("arome", RainMotionUse.Model, null),
};

// METAR of every station over the whole range, in one request per month.
var reports = new List<PrecipitationReport>();
var allStations = places.SelectMany(p => p.Stations.Select(s => s.Id)).ToList();
for (var month = new DateOnly(firstDay.Year, firstDay.Month, 1); month <= lastDay; month = month.AddMonths(1))
{
    var from = new DateTimeOffset(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    reports.AddRange(await metar.GetReportsAsync(allStations, from, from.AddMonths(1).AddHours(3)));
    await Task.Delay(1100);
}
reports = reports.DistinctBy(r => (r.StationId, r.Time)).ToList();
Console.Error.WriteLine($"METAR: {reports.Count} informes, {reports.Count(r => r.Precipitation)} con precipitación");

bool? MetarRain(string station, DateTimeOffset time)
{
    var near = reports.Where(r => r.StationId == station && Math.Abs((r.Time - time).TotalMinutes) <= 15).ToList();
    return near.Count == 0 ? null : near.Any(r => r.Precipitation);
}

var csv = new StringBuilder("sitio,estacion,T,plazo,modelo,probabilidad,radar_lluvia,metar_lluvia,movimiento\n");
var outcomes = new Dictionary<(string Place, string Truth, int Lead, string Model), List<RainOutcome>>();
void Add(string place, string truth, int lead, string model, double? probability, bool? rained)
{
    if (probability is not { } p || rained is not { } r)
    {
        return;
    }
    foreach (var key in new[] { (place, truth, lead, model), ("Todos", truth, lead, model) })
    {
        if (!outcomes.TryGetValue(key, out var list))
        {
            outcomes[key] = list = [];
        }
        list.Add(new RainOutcome(p, r));
    }
}

var daysJudged = 0;
var momentsJudged = 0;
foreach (var (placeName, center, stations) in places)
{
    // A day's window can start the evening before: never judge the same moment twice.
    var judgedMoments = new HashSet<DateTimeOffset>();
    for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var wet = reports.Where(r => r.Precipitation && stations.Any(s => s.Id == r.StationId)
                                     && r.Time >= dayStart && r.Time < dayStart.AddDays(1)).ToList();
        if (wet.Count == 0)
        {
            continue;
        }
        daysJudged++;
        var first = RadarFrames.AtOrBefore(wet.Min(r => r.Time) - TimeSpan.FromHours(2), TimeSpan.FromMinutes(30));
        var last = wet.Max(r => r.Time) + TimeSpan.FromHours(2);
        Console.Error.WriteLine($"{placeName} {day:yyyy-MM-dd}: {first:HH:mm}-{last:HH:mm} UTC");

        IReadOnlyList<RainForecastSeries> model;
        try
        {
            model = await arome.GetAsync(stations.Select(s => s.Position).ToList(), first, last.AddHours(2));
        }
        catch (RainForecastException ex)
        {
            Console.Error.WriteLine($"  AROME: {ex.Message}");
            model = [];
        }

        var crops = new Dictionary<DateTimeOffset, RainGrid?>();
        async Task<RainGrid?> Frame(DateTimeOffset time)
        {
            if (!crops.TryGetValue(time, out var grid))
            {
                grid = await LoadCropAsync(radar, cropCache, placeName, center, time);
                crops[time] = grid;
            }
            return grid;
        }

        for (var t = first; t <= last; t += TimeSpan.FromMinutes(30))
        {
            if (!judgedMoments.Add(t))
            {
                continue;
            }
            var latest = await Frame(t);
            if (latest is null)
            {
                continue;
            }
            var motionFrames = new List<RainGrid>();
            foreach (var minutes in new[] { 30, 20, 10 })
            {
                if (await Frame(t.AddMinutes(-minutes)) is { } f)
                {
                    motionFrames.Add(f);
                }
            }
            motionFrames.Add(latest);
            var combined = motionFrames.Count >= 2 ? RainMotions.Combine(motionFrames) : null;
            var motionText = combined?.Motion is { } m
                ? $"{m.TowardsDegrees:0}° {m.SpeedKmh:0} km/h"
                : combined?.Failure?.ToString() ?? "sin imágenes";
            var issuedAt = t + publishDelay;
            momentsJudged++;

            for (var s = 0; s < stations.Length; s++)
            {
                var (station, position) = stations[s];
                var nowcasts = models
                    .Where(x => x.Motion != RainMotionUse.Model)
                    .ToDictionary(x => x.Name, x => Nowcast.AtPoint(
                        latest, x.Motion == RainMotionUse.Moving ? combined?.Motion : null, position, issuedAt, x.Options));

                foreach (var lead in leads)
                {
                    var target = issuedAt.AddMinutes(lead);
                    var actual = await Frame(target);
                    var rate = actual?.RateAt(position);
                    bool? radarRain = rate is { } r && !float.IsNaN(r) ? r >= threshold : null;
                    var metarRain = MetarRain(station, target);

                    foreach (var (name, use, _) in models)
                    {
                        double? probability = use == RainMotionUse.Model
                            ? (s < model.Count && model[s].RateAt(target) is { } rateModel ? (rateModel >= threshold ? 1 : 0) : null)
                            : nowcasts[name].At(target).Probability;
                        csv.AppendLine(string.Join(',', placeName, station, issuedAt.ToString("yyyy-MM-dd HH:mm"), lead, name,
                            probability?.ToString("0.00", CultureInfo.InvariantCulture), radarRain, metarRain, motionText));
                        Add(placeName, "radar", lead, name, probability, radarRain);
                        Add(placeName, "metar", lead, name, probability, metarRain);
                    }
                }
            }
        }
    }
}

await File.WriteAllTextAsync(Path.Combine(output, "casos.csv"), csv.ToString());

// Summary tables.
static string Share(double? value) => value is { } v ? (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "—";
var md = new StringBuilder();
md.AppendLine($"# Juez de la fase 4 ({firstDay:yyyy-MM-dd} a {lastDay:yyyy-MM-dd})");
md.AppendLine();
md.AppendLine($"Días con lluvia juzgados: {daysJudged} (sitio y día); momentos: {momentsJudged}. Lluvia desde " +
              $"{threshold.ToString("0.00", CultureInfo.InvariantCulture)} mm/h en el radar; en el METAR, precipitación a ±15 min.");
foreach (var truth in new[] { "radar", "metar" })
{
    foreach (var place in new[] { "Todos", "Madrid", "León" })
    {
        md.AppendLine();
        md.AppendLine($"## Contra el {truth.ToUpperInvariant()} — {place}");
        md.AppendLine();
        md.AppendLine("| Plazo | Modelo | Casos | Llovió | Anunció | \"Probable\": llovió | \"No\": llovió | Brier |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var lead in leads)
        {
            foreach (var (name, _, _) in models)
            {
                if (!outcomes.TryGetValue((place, truth, lead, name), out var list))
                {
                    continue;
                }
                var sc = RainScores.Of(list);
                md.AppendLine($"| +{lead} | {name} | {sc.Cases} | {Share((double)sc.Rained / sc.Cases)} | {Share(sc.RainAnnounced)} | " +
                              $"{Share(sc.LikelyRained)} ({sc.SaidLikely}) | {Share(sc.NoRained)} ({sc.SaidNo}) | " +
                              $"{sc.Brier.ToString("0.000", CultureInfo.InvariantCulture)} |");
            }
        }
    }
}
md.AppendLine();
foreach (var (name, use, _) in models.Where(x => x.Motion == RainMotionUse.Moving))
{
    md.AppendLine($"## Fiabilidad de los porcentajes ({name}, contra el radar, todos los plazos)");
    md.AppendLine();
    md.AppendLine("| Banda | Casos | Porcentaje medio dicho | Veces que llovió |");
    md.AppendLine("|---|---|---|---|");
    var all = leads.SelectMany(l => outcomes.GetValueOrDefault(("Todos", "radar", l, name)) ?? []).ToList();
    foreach (var bin in RainScores.Of(all).Reliability)
    {
        md.AppendLine($"| {bin.From * 100:0}-{bin.To * 100:0} % | {bin.Cases} | {Share(double.IsNaN(bin.MeanProbability) ? null : bin.MeanProbability)} | " +
                      $"{Share(double.IsNaN(bin.RainedShare) ? null : bin.RainedShare)} |");
    }
    md.AppendLine();
}
md.AppendLine();
md.AppendLine($"{OperaRadarSource.AttributionText}. {OpenMeteoPastRainForecastSource.Attribution}. {IemPrecipitationObservationSource.Attribution}.");
await File.WriteAllTextAsync(Path.Combine(output, "resumen.md"), md.ToString());
Console.WriteLine(md.ToString());

// A crop of the frame around the place, from the cache (gzip of the rates) or downloaded and cut.
static async Task<RainGrid?> LoadCropAsync(OperaRadarSource radar, string cache, string place, Coordinate center, DateTimeOffset time)
{
    var path = Path.Combine(cache, place, $"{time:yyyyMMdd'T'HHmm}.bin.gz");
    var missing = path + ".missing";
    if (File.Exists(missing))
    {
        return null;
    }
    if (File.Exists(path))
    {
        await using var input = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var reader = new BinaryReader(input);
        var lat0 = reader.ReadDouble();
        var lon0 = reader.ReadDouble();
        var x0 = reader.ReadDouble();
        var y0 = reader.ReadDouble();
        var left = reader.ReadDouble();
        var top = reader.ReadDouble();
        var cell = reader.ReadDouble();
        var rows = reader.ReadInt32();
        var columns = reader.ReadInt32();
        var rates = new float[rows * columns];
        for (var i = 0; i < rates.Length; i++)
        {
            rates[i] = reader.ReadSingle();
        }
        return new RainGrid(time, new LambertAzimuthalEqualArea(lat0, lon0, x0, y0), left, top, cell, rows, columns, rates);
    }

    RainGrid? grid = null;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            grid = await radar.GetFrameAsync(time, center, RadiusKm);
            break;
        }
        catch (RadarException ex)
        {
            Console.Error.WriteLine($"  radar {time:u}: {ex.Message}" + (attempt < 3 ? "; reintento" : "; se deja"));
            if (attempt == 3)
            {
                // Not marked as missing: it may be there next time.
                return null;
            }
            await Task.Delay(TimeSpan.FromSeconds(10 * attempt));
        }
    }
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    if (grid is null)
    {
        // The frame does not exist in OPERA.
        await File.WriteAllTextAsync(missing, "");
        return null;
    }
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
    return grid;
}

enum RainMotionUse { Moving, Still, Model }
