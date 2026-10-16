#:project ../../src/VientoHud.Core
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
// dotnet run tools/investigacion/motioncheck.cs -- [carpeta caché]
// Fase 4, paso 4c-2: ¿el movimiento calculado sirve para lo que viene? En cada momento T de varios días de lluvia,
// calcula el movimiento como lo haría la app a T-30 min (imágenes de T-60, T-50, T-40 y T-30), mueve la imagen de
// T-30 lo que ese movimiento recorre en 30 min y mide cuánto se parece a la imagen real de T, frente a dejarla quieta.
// Imprime una línea por momento (CSV) y un resumen. Reflectividad de OPERA, 300 km alrededor del sitio.
var cache = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "vientohud-radar");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var source = new OperaRadarSource(http, new OperaOptions { CacheDirectory = cache });

// Sitio, centro, y horas UTC de inicio y fin (cada 30 min) con lluvia según los METAR.
(string Place, Coordinate Center, string From, string To)[] cases =
[
    ("Madrid", new(40.4168, -3.7038), "2026-10-03T05:00Z", "2026-10-03T09:00Z"),
    ("Madrid", new(40.4168, -3.7038), "2026-10-06T13:30Z", "2026-10-06T18:00Z"),
    ("León", new(42.5987, -5.5671), "2026-09-30T05:00Z", "2026-09-30T12:30Z"),
    ("León", new(42.5987, -5.5671), "2026-10-03T09:30Z", "2026-10-03T13:00Z"),
    ("León", new(42.5987, -5.5671), "2026-08-24T13:00Z", "2026-08-24T18:30Z"),
];

Console.WriteLine("sitio,T,hacia,kmh,parejas,dispersion,parecido_movida,parecido_quieta,fallo");
var rows = new List<(double Moved, double Still)>();
var failures = 0;
foreach (var (place, center, from, to) in cases)
{
    var start = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    var end = DateTimeOffset.Parse(to, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
    for (var t = start; t <= end; t += TimeSpan.FromMinutes(30))
    {
        var frames = new List<RainGrid>();
        foreach (var minutes in new[] { 60, 50, 40, 30 })
        {
            if (await source.GetFrameAsync(t.AddMinutes(-minutes), center, 150) is { } frame)
            {
                frames.Add(frame);
            }
        }
        var now = await source.GetFrameAsync(t, center, 150);
        var label = $"{place},{t:yyyy-MM-dd HH:mm}";
        if (now is null || frames.Count < 2 || frames[^1].Time != t.AddMinutes(-30))
        {
            Console.WriteLine($"{label},,,,,,,falta imagen");
            failures++;
            continue;
        }

        var combined = RainMotions.Combine(frames);
        var still = RainMotions.Similarity(frames[^1], now, 0, 0);
        if (combined.Motion is not { } motion)
        {
            Console.WriteLine($"{label},,,{combined.PairsWithMotion}/{combined.PairsTried},{combined.SpreadKmh:0.0},,{still:0.000},{combined.Failure}");
            failures++;
            continue;
        }
        var moved = RainMotions.Similarity(frames[^1], now, motion.EastKmh * 0.5, motion.NorthKmh * 0.5);
        Console.WriteLine(string.Join(',',
            label, motion.TowardsDegrees.ToString("0", CultureInfo.InvariantCulture),
            motion.SpeedKmh.ToString("0.0", CultureInfo.InvariantCulture),
            $"{combined.PairsWithMotion}/{combined.PairsTried}",
            combined.SpreadKmh?.ToString("0.0", CultureInfo.InvariantCulture),
            moved?.ToString("0.000", CultureInfo.InvariantCulture), still?.ToString("0.000", CultureInfo.InvariantCulture), ""));
        if (moved is { } m && still is { } s)
        {
            rows.Add((m, s));
        }
    }
}

Console.WriteLine();
Console.WriteLine($"Momentos con movimiento: {rows.Count}; sin movimiento o sin imagen: {failures}");
if (rows.Count > 0)
{
    Console.WriteLine($"Movida se parece más que quieta: {rows.Count(r => r.Moved > r.Still)} de {rows.Count}");
    Console.WriteLine($"Parecido medio: movida {rows.Average(r => r.Moved):0.000}, quieta {rows.Average(r => r.Still):0.000}");
}
Console.WriteLine(source.Attribution);
