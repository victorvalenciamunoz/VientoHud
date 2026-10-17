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
// dotnet run tools/investigacion/judge5a.cs -- <carpeta salida> [carpeta caché de recortes] [días por aeropuerto]
// Fase 5, paso 5a-2: juez de AROME HD 15 min mirando una zona (DECISIONS 033). Los días más lluviosos (METAR) de Barajas
// y León entre el 16 jul. y el 15 oct. 2026; tras cada pasada de AROME archivada (cada 3 h, R) se "sale" a R+1:05, con
// esa pasada en una rejilla de 9 × 9 puntos cada 5 km alrededor del aeropuerto (Single Runs API: lo que dijo entonces,
// no el archivo cosido) y con la imagen de radar de R+1:00. Para cada plazo, la probabilidad de lluvia en el
// aeropuerto con varios radios y márgenes de tiempo (y en un punto), y el radar movido en los plazos cortos, frente al
// radar después en la celda del aeropuerto y al METAR. Escribe casos.csv y resumen.md. Pausa entre peticiones a
// Open-Meteo para no pasar de ~4.000 llamadas por hora (cada punto cuenta).
var output = args[0];
var cropCache = args.Length > 1 ? args[1] : Path.Combine(output, "recortes");
var daysPerStation = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 6;
Directory.CreateDirectory(output);

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var radar = new OperaRadarSource(http);
var metar = new IemPrecipitationObservationSource(http);

const double RadiusKm = 150;
var threshold = RainNearby.DefaultThresholdMmPerHour;
var firstDay = new DateOnly(2026, 7, 16);
var lastDay = new DateOnly(2026, 10, 15);
int[] leads = [60, 75, 90, 105, 120, 150, 180, 210, 240, 270];
int[] radarLeads = [60, 75, 90, 105, 120, 150, 180];
// The Single Runs archive keeps AROME HD 15 min runs every 3 hours (00, 03… UTC; checked 2026-10-17): leave one
// hour after each run (live, the run is 35-95 min old), so the judge is slightly pessimistic.
const int IssuesPerDay = 4;
const int GridPoints = 81;

// Same crop centres as judge4.cs, so its cache is reused.
(string Station, string Place, Coordinate Airport, Coordinate CropCenter)[] stations =
[
    ("LEMD", "Madrid", new(40.46667, -3.55556), new(40.45, -3.68)),
    ("LELN", "León", new(42.58889, -5.64944), new(42.58889, -5.64944)),
];
var variants = new List<(string Name, ModelRainOptions Options)>
{
    ("punto", new ModelRainOptions { RadiusKm = 0.1, TimeMargin = TimeSpan.Zero }),
};
foreach (var radius in new[] { 5.0, 10, 15, 20 })
{
    foreach (var margin in new[] { 0, 15, 30, 60 })
    {
        variants.Add(($"r{radius:0}-t{margin}", new ModelRainOptions { RadiusKm = radius, TimeMargin = TimeSpan.FromMinutes(margin) }));
    }
}

var reports = new List<PrecipitationReport>();
for (var month = new DateOnly(firstDay.Year, firstDay.Month, 1); month <= lastDay; month = month.AddMonths(1))
{
    var from = new DateTimeOffset(month.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    reports.AddRange(await metar.GetReportsAsync(stations.Select(s => s.Station).ToList(), from, from.AddMonths(1).AddHours(6)));
    await Task.Delay(1100);
}
reports = reports.DistinctBy(r => (r.StationId, r.Time)).ToList();

bool? MetarRain(string station, DateTimeOffset time)
{
    var near = reports.Where(r => r.StationId == station && Math.Abs((r.Time - time).TotalMinutes) <= 15).ToList();
    return near.Count == 0 ? null : near.Any(r => r.Precipitation);
}

var csv = new StringBuilder("estacion,salida,plazo,modelo,probabilidad,radar_lluvia,metar_lluvia\n");
var outcomes = new Dictionary<(string Truth, int Lead, string Model), List<RainOutcome>>();
void Add(string truth, int lead, string model, double? probability, bool? rained)
{
    if (probability is not { } p || rained is not { } r)
    {
        return;
    }
    if (!outcomes.TryGetValue((truth, lead, model), out var list))
    {
        outcomes[(truth, lead, model)] = list = [];
    }
    list.Add(new RainOutcome(p, r));
}

var calls = 0;
var clock = System.Diagnostics.Stopwatch.StartNew();
var judgedDays = new List<string>();
foreach (var (station, place, airport, cropCenter) in stations)
{
    // The rainiest days at the station, by number of reports with precipitation.
    var days = reports.Where(r => r.StationId == station && r.Precipitation)
        .GroupBy(r => DateOnly.FromDateTime(r.Time.UtcDateTime))
        .Where(g => g.Key >= firstDay && g.Key <= lastDay)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
        .Take(daysPerStation)
        .OrderBy(g => g.Key)
        .ToList();
    var grid = ModelRainField.GridAround(airport, 20, 5);
    if (grid.Count != GridPoints)
    {
        throw new InvalidOperationException($"Grid of {grid.Count} points.");
    }

    foreach (var day in days)
    {
        var firstRain = day.Min(r => r.Time);
        // The first run at or before 5 h before the first rain of the day, then every 3 h (the rain arrives within the leads).
        var early = firstRain.AddHours(-5);
        var start = new DateTimeOffset(early.Year, early.Month, early.Day, early.Hour / 3 * 3, 0, 0, TimeSpan.Zero);
        judgedDays.Add($"{station} {day.Key:yyyy-MM-dd} ({day.Count()} METAR con precipitación; pasadas desde {start:dd HH}:00)");
        Console.Error.WriteLine(judgedDays[^1]);
        var crops = new Dictionary<DateTimeOffset, RainGrid?>();
        async Task<RainGrid?> Frame(DateTimeOffset time)
        {
            if (!crops.TryGetValue(time, out var g))
            {
                g = await LoadCropAsync(radar, cropCache, place, cropCenter, time);
                crops[time] = g;
            }
            return g;
        }

        for (var i = 0; i < IssuesPerDay; i++)
        {
            var run = start.AddHours(3 * i);
            var hour = run.AddHours(1);
            var issuedAt = hour.AddMinutes(5);

            // Pace: at most ~4,000 point-calls per hour (only requests that are not cached count).
            var allowed = clock.Elapsed.TotalHours * 4000 + 600;
            if (calls + GridPoints > allowed)
            {
                var wait = TimeSpan.FromHours((calls + GridPoints - 600) / 4000.0) - clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                }
            }
            var aromeFile = Path.Combine(cropCache, "arome", $"{station}-{run:yyyyMMddHH}.csv");
            ModelRainField? field = File.Exists(aromeFile) ? ReadField(aromeFile, grid) : null;
            for (var attempt = 1; attempt <= 3 && field is null; attempt++)
            {
                try
                {
                    var source = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.SingleRun(run));
                    calls += GridPoints;
                    var series = await source.GetAsync(grid, run, run.AddHours(7));
                    WriteField(aromeFile, series);
                    field = new ModelRainField(series);
                }
                catch (RainForecastException ex)
                {
                    Console.Error.WriteLine($"  AROME {run:u}: {ex.Message}");
                    if (ex.Message.Contains("not available", StringComparison.Ordinal))
                    {
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30 * attempt));
                }
            }

            // Radar nowcast as `radar` would give it at H:05 with the frame of H:00.
            var latest = await Frame(hour);
            PointNowcast? nowcast = null;
            if (latest is not null)
            {
                var motionFrames = new List<RainGrid>();
                foreach (var minutes in new[] { 30, 20, 10 })
                {
                    if (await Frame(hour.AddMinutes(-minutes)) is { } f)
                    {
                        motionFrames.Add(f);
                    }
                }
                motionFrames.Add(latest);
                var combined = motionFrames.Count >= 2 ? RainMotions.Combine(motionFrames) : null;
                nowcast = Nowcast.AtPoint(latest, combined?.Motion, airport, issuedAt,
                    new NowcastOptions { Horizon = TimeSpan.FromMinutes(radarLeads.Max()) });
            }

            foreach (var lead in leads)
            {
                var target = issuedAt.AddMinutes(lead);
                var actual = await Frame(target);
                var rate = actual?.RateAt(airport);
                bool? radarRain = rate is { } r && !float.IsNaN(r) ? r >= threshold : null;
                var metarRain = MetarRain(station, target);

                var predictions = new List<(string Model, double? Probability)>();
                foreach (var (name, options) in variants)
                {
                    predictions.Add(($"arome-{name}", field?.Probability(airport, target, options)));
                }
                if (nowcast is not null && radarLeads.Contains(lead))
                {
                    predictions.Add(("radar", nowcast.At(target).Probability));
                }
                foreach (var (model, probability) in predictions)
                {
                    csv.AppendLine(string.Join(',', station, issuedAt.ToString("yyyy-MM-dd HH:mm"), lead, model,
                        probability?.ToString("0.000", CultureInfo.InvariantCulture), radarRain, metarRain));
                    Add("radar", lead, model, probability, radarRain);
                    Add("metar", lead, model, probability, metarRain);
                }
            }
        }
    }
}

await File.WriteAllTextAsync(Path.Combine(output, "casos.csv"), csv.ToString());

static string Share(double? value) => value is { } v ? (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "—";
static string Num(double value) => double.IsNaN(value) ? "—" : value.ToString("0.000", CultureInfo.InvariantCulture);
var md = new StringBuilder();
md.AppendLine("# Juez de AROME HD 15 min en zona (fase 5, paso 5a-2)");
md.AppendLine();
md.AppendLine($"Días: {string.Join("; ", judgedDays)}. Llamadas a Open-Meteo: {calls}.");
foreach (var truth in new[] { "radar", "metar" })
{
    md.AppendLine();
    md.AppendLine($"## Brier contra el {truth.ToUpperInvariant()} (más bajo, mejor) por plazo desde la salida");
    md.AppendLine();
    var models = outcomes.Keys.Where(k => k.Truth == truth).Select(k => k.Model).Distinct()
        .OrderBy(m => m == "radar" ? 0 : m == "arome-punto" ? 1 : 2).ThenBy(m => m, StringComparer.Ordinal).ToList();
    md.AppendLine("| Modelo | " + string.Join(" | ", leads.Select(l => $"+{l}")) + " |");
    md.AppendLine("|---|" + string.Concat(leads.Select(_ => "---|")));
    foreach (var model in models)
    {
        md.AppendLine($"| {model} | " + string.Join(" | ", leads.Select(l =>
            outcomes.TryGetValue((truth, l, model), out var list) ? Num(RainScores.Of(list).Brier) : "—")) + " |");
    }
    md.AppendLine();
    md.AppendLine("Casos y lluvia: " + string.Join(", ", leads.Select(l =>
        outcomes.TryGetValue((truth, l, "arome-punto"), out var list)
            ? $"+{l}: {list.Count} ({Share(list.Count(o => o.Rained) / (double)list.Count)})"
            : $"+{l}: —")));
}

// Detail of the best variants.
md.AppendLine();
md.AppendLine("## Detalle contra el radar, todos los plazos de AROME juntos");
md.AppendLine();
md.AppendLine("| Modelo | Casos | Anunció | \"Probable\": llovió | \"No\": llovió | Brier |");
md.AppendLine("|---|---|---|---|---|---|");
foreach (var model in outcomes.Keys.Where(k => k.Truth == "radar" && k.Model != "radar").Select(k => k.Model).Distinct().Order(StringComparer.Ordinal))
{
    var sc = RainScores.Of(leads.SelectMany(l => outcomes.GetValueOrDefault(("radar", l, model)) ?? []));
    md.AppendLine($"| {model} | {sc.Cases} | {Share(sc.RainAnnounced)} | {Share(sc.LikelyRained)} ({sc.SaidLikely}) | " +
                  $"{Share(sc.NoRained)} ({sc.SaidNo}) | {Num(sc.Brier)} |");
}
md.AppendLine();
md.AppendLine($"{OperaRadarSource.AttributionText}. {OpenMeteoRainForecastSource.Attribution}. {IemPrecipitationObservationSource.Attribution}.");
await File.WriteAllTextAsync(Path.Combine(output, "resumen.md"), md.ToString());
Console.WriteLine(md.ToString());

// AROME answers kept on disk: one line per point and interval (point index, end in unix seconds, mm/h).
static void WriteField(string path, IReadOnlyList<RainForecastSeries> series)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var lines = series.SelectMany((s, i) => s.Samples.Select(x =>
        $"{i},{x.End.ToUnixTimeSeconds()},{x.MmPerHour.ToString("R", CultureInfo.InvariantCulture)}"));
    File.WriteAllLines(path + ".part", lines);
    File.Move(path + ".part", path, overwrite: true);
}

static ModelRainField ReadField(string path, IReadOnlyList<Coordinate> grid)
{
    var samples = grid.Select(_ => new List<RainForecastSample>()).ToList();
    foreach (var line in File.ReadLines(path))
    {
        var f = line.Split(',');
        samples[int.Parse(f[0], CultureInfo.InvariantCulture)].Add(new RainForecastSample(
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(f[1], CultureInfo.InvariantCulture)),
            double.Parse(f[2], CultureInfo.InvariantCulture)));
    }
    return new ModelRainField(grid.Select((p, i) => new RainForecastSeries(p, p, "meteofrance_arome_france_hd_15min",
        TimeSpan.FromMinutes(15), samples[i])).ToList());
}

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

