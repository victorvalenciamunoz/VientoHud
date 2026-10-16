#:project ../../src/VientoHud.Core
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
// dotnet run tools/investigacion/nowcastcheck.cs -- [carpeta caché]
// Fase 4, paso 4d-2: en momentos pasados de días de lluvia, la previsión de lluvia en un aeropuerto a +30, +60 y +90 min
// (como la daría `radar`: movimiento de la última media hora) frente a lo que vio de verdad el radar en la celda del
// aeropuerto a esas horas y frente al METAR (lluvia en el aeropuerto a ±15 min). También el rival "nada se mueve".
// No es el juez (4e): es una muestra para ver si la previsión se comporta con sentido.
var cache = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "vientohud-radar");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var source = new OperaRadarSource(http, new OperaOptions { CacheDirectory = cache });

(string Station, Coordinate Airport, string From, string To)[] cases =
[
    ("LEMD", new(40.4936, -3.5668), "2026-10-03T04:00Z", "2026-10-03T09:00Z"),
    ("LEMD", new(40.4936, -3.5668), "2026-10-06T12:30Z", "2026-10-06T17:30Z"),
    ("LELN", new(42.5890, -5.6556), "2026-09-30T04:00Z", "2026-09-30T12:00Z"),
    ("LELN", new(42.5890, -5.6556), "2026-10-03T08:30Z", "2026-10-03T12:30Z"),
    ("LELN", new(42.5890, -5.6556), "2026-08-24T12:00Z", "2026-08-24T18:00Z"),
];
int[] leads = [30, 60, 90];

// METAR present weather (IEM), per station: precipitation at the airport (not "VC", in the vicinity).
var metar = new Dictionary<string, List<(DateTimeOffset Time, bool Rain, string Codes)>>();
foreach (var station in cases.Select(c => c.Station).Distinct())
{
    var days = cases.Where(c => c.Station == station).ToList();
    var reports = new List<(DateTimeOffset, bool, string)>();
    foreach (var (_, _, from, to) in days)
    {
        var start = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture).AddHours(-1);
        var end = DateTimeOffset.Parse(to, CultureInfo.InvariantCulture).AddHours(3);
        var url = $"https://mesonet.agron.iastate.edu/cgi-bin/request/asos.py?station={station}&data=wxcodes" +
                  $"&sts={start:yyyy-MM-ddTHH:mm}Z&ets={end:yyyy-MM-ddTHH:mm}Z&tz=Etc/UTC&format=onlycomma&missing=M&latlon=no";
        var csv = await http.GetStringAsync(url);
        await Task.Delay(1100);
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var f = line.Split(',');
            var time = DateTimeOffset.ParseExact(f[1].Trim(), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            var codes = f.Length > 2 && f[2].Trim() != "M" ? f[2].Trim() : "";
            var rain = codes.Split(' ').Any(c => !c.StartsWith("VC") && (c.Contains("RA") || c.Contains("DZ") || c.Contains("GR") || c.Contains("GS")));
            reports.Add((time.ToUniversalTime(), rain, codes));
        }
    }
    metar[station] = reports;
}

bool? MetarRain(string station, DateTimeOffset time)
{
    var near = metar[station].Where(r => Math.Abs((r.Time - time).TotalMinutes) <= 15).ToList();
    return near.Count == 0 ? null : near.Any(r => r.Rain);
}

Console.WriteLine("estacion,T,lead,prob,palabra,prob_quieta,palabra_quieta,radar_mm_h,radar_lluvia,metar_lluvia,movimiento");
// Per lead and model: (predicted likely, radar rain) counts.
var tallies = new Dictionary<(int Lead, string Model, string Truth), int[]>();
void Tally(int lead, string model, string truth, RainLikelihood? said, bool? happened)
{
    if (said is null || happened is null)
    {
        return;
    }
    var key = (lead, model, truth);
    if (!tallies.TryGetValue(key, out var t))
    {
        tallies[key] = t = new int[6];
    }
    // [said no & dry, said no & rain, possible & dry, possible & rain, likely & dry, likely & rain]
    t[(int)said * 2 + (happened.Value ? 1 : 0)]++;
}

foreach (var (station, airport, from, to) in cases)
{
    var start = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    var end = DateTimeOffset.Parse(to, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    for (var t = start; t <= end; t += TimeSpan.FromMinutes(30))
    {
        var latest = await source.GetFrameAsync(t, airport, 150);
        if (latest is null)
        {
            continue;
        }
        var frames = await source.GetMotionFramesAsync(latest, airport, 150);
        var combined = frames.Count >= 2 ? RainMotions.Combine(frames) : null;
        var moving = Nowcast.AtPoint(latest, combined?.Motion, airport, t);
        var still = Nowcast.AtPoint(latest, null, airport, t);
        var motionText = combined?.Motion is { } m
            ? $"{m.TowardsDegrees:0}° {m.SpeedKmh:0} km/h"
            : combined?.Failure?.ToString() ?? "sin imágenes";

        foreach (var lead in leads)
        {
            var target = t.AddMinutes(lead);
            var actual = await source.GetFrameAsync(target, airport, 20);
            var rate = actual?.RateAt(airport);
            bool? radarRain = rate is { } r && !float.IsNaN(r) ? r >= RainNearby.DefaultThresholdMmPerHour : null;
            var metarRain = MetarRain(station, target);
            var a = moving.At(target);
            var b = still.At(target);
            Console.WriteLine(string.Join(',', station, t.ToString("yyyy-MM-dd HH:mm"), lead,
                a.Probability?.ToString("0.00", CultureInfo.InvariantCulture), a.Likelihood,
                b.Probability?.ToString("0.00", CultureInfo.InvariantCulture), b.Likelihood,
                rate?.ToString("0.00", CultureInfo.InvariantCulture), radarRain, metarRain, motionText));
            Tally(lead, "movida", "radar", a.Likelihood, radarRain);
            Tally(lead, "quieta", "radar", b.Likelihood, radarRain);
            Tally(lead, "movida", "metar", a.Likelihood, metarRain);
            Tally(lead, "quieta", "metar", b.Likelihood, metarRain);
        }
    }
}

Console.WriteLine();
Console.WriteLine("Por plazo: cuántas veces llovió de verdad cuando dijo no / puede / probable (llovió/total)");
foreach (var truth in new[] { "radar", "metar" })
{
    foreach (var lead in leads)
    {
        foreach (var model in new[] { "movida", "quieta" })
        {
            if (!tallies.TryGetValue((lead, model, truth), out var c))
            {
                continue;
            }
            Console.WriteLine($"  {truth,-5} +{lead} {model}: no {c[1]}/{c[0] + c[1]}, puede {c[3]}/{c[2] + c[3]}, probable {c[5]}/{c[4] + c[5]}");
        }
    }
}
Console.WriteLine(source.Attribution);
Console.WriteLine("METAR: Iowa Environmental Mesonet");
