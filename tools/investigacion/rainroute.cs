#:project ../../src/VientoHud.Core
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Routing;
// dotnet run tools/investigacion/rainroute.cs -- <ride.gpx> <ahora UTC, p. ej. 2026-10-06T13:00Z> [km/h] [carpeta caché radar]
// Fase 5, paso 5a-3: la lluvia prevista en cada tramo de una salida que empieza "ahora", con lo que se sabía entonces
// (imagen de radar de 5 min antes y su movimiento; la última pasada archivada de AROME HD 15 min publicada, de las de cada
// 3 h; la probabilidad por horas de best_match del archivo), frente a lo que vio de verdad el radar en el medio de cada
// tramo a la hora de paso. Tabla por tramos de 5 km y resumen.
XNamespace gpx = "http://www.topografix.com/GPX/1/1";
var points = XDocument.Load(args[0]).Descendants(gpx + "trkpt")
    .Select(p => new RoutePoint(
        new Coordinate(
            double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
            double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture)),
        p.Element(gpx + "ele") is { } ele ? double.Parse(ele.Value, CultureInfo.InvariantCulture) : null))
    .ToList();
var route = new Route(points, [], 0, null, "GPX");
var now = DateTimeOffset.Parse(args[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
var speed = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : RideDistance.DefaultAverageSpeedKmh;
var cache = args.Length > 3 ? args[3] : Path.Combine(Path.GetTempPath(), "vientohud-radar");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var opera = new OperaRadarSource(http, new OperaOptions { CacheDirectory = cache });
var legs = RouteLegs.Split(route, now, speed);
var start = points[0].Position;
var reach = points.Max(p => p.Position.DistanceKmTo(start));

// What was known at `now`: the radar of 5 min before, and the last archived AROME run published by then (runs every 3 h
// in the archive, ~35 min late).
var radar = new PastRadar(opera, now - TimeSpan.FromMinutes(5));
var lastRun = now - TimeSpan.FromMinutes(40);
var run = new DateTimeOffset(lastRun.Year, lastRun.Month, lastRun.Day, lastRun.Hour / 3 * 3, 0, 0, TimeSpan.Zero);
var model = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.SingleRun(run));
var fallback = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.Archive);
var outlook = await RainOutlook.ForecastAsync(radar, model, fallback, start, reach, now, legs[^1].EndTime);
var rain = RouteRain.Along(legs, outlook);

Console.WriteLine($"{legs[^1].EndKm:0.0} km, salida {now:yyyy-MM-dd HH:mm}Z a {speed} km/h, llegada {legs[^1].EndTime:HH:mm}Z; " +
                  $"alcance {reach:0} km");
Console.WriteLine($"Radar: {outlook.Radar?.Time:HH:mm}Z, movimiento {(outlook.Motion is { } m ? $"hacia {m.TowardsDegrees:0}° a {m.SpeedKmh:0} km/h" : "desconocido")}; " +
                  $"AROME: pasada {run:dd HH:mm}Z{(outlook.Model is null ? " (no disponible)" : $", hasta {outlook.Model.LastTime:HH:mm}Z, {outlook.Model.Series.Count} puntos")}; " +
                  $"reserva: {(outlook.Fallback is null ? "no" : "sí")}");
Console.WriteLine();
Console.WriteLine("  km      hora   prevista          fuente       lluvia vista por el radar");
var observedWet = 0.0;
var predictedLikely = 0.0;
var hits = 0.0;
foreach (var stretch in rain.Legs.Chunk(5))
{
    var probabilities = stretch.Select(l => l.Rain.Probability).Where(p => p is not null).Select(p => p!.Value).ToList();
    var sources = string.Join("/", stretch.Select(l => l.Rain.Source).Distinct());
    var wet = 0;
    var seen = 0;
    foreach (var leg in stretch)
    {
        var frame = await opera.GetFrameAsync(RadarFrames.AtOrBefore(leg.Leg.MiddleTime, opera.FrameInterval), start, 150);
        if (frame?.RateAt(leg.Leg.Middle) is { } rate && !float.IsNaN(rate))
        {
            seen++;
            var isWet = rate >= RainNearby.DefaultThresholdMmPerHour;
            if (isWet)
            {
                wet++;
                observedWet += leg.Leg.LengthKm;
            }
            if (leg.Rain.Likelihood == RainLikelihood.Likely)
            {
                predictedLikely += leg.Leg.LengthKm;
                if (isWet)
                {
                    hits += leg.Leg.LengthKm;
                }
            }
        }
    }
    var first = stretch[0].Leg;
    var predicted = probabilities.Count == 0 ? "   —   " : $"{probabilities.Average() * 100,3:0} % ({probabilities.Max() * 100:0} máx.)";
    Console.WriteLine($"{first.StartKm,5:0}-{stretch[^1].Leg.EndKm,-4:0} {first.MiddleTime:HH:mm}  {predicted,-16}  {sources,-12} {wet}/{seen} tramos mojados");
}

Console.WriteLine();
Console.WriteLine($"Previsto: {rain.LikelyKm:0} km con lluvia probable, {rain.PossibleKm:0} km posible, {rain.UncountedKm:0} km sin contar" +
                  (rain.FirstRain is { } fr ? $"; primera lluvia en el km {fr.Leg.StartKm:0} hacia las {fr.Leg.MiddleTime:HH:mm}Z" : ""));
Console.WriteLine(rain.AreaWarning is { } w
    ? $"Aviso para la zona: desde las {w.From:HH:mm}Z, hasta {w.MaxProbability * 100:0} % ({w.Source})"
    : "Sin aviso para la zona.");
Console.WriteLine($"Visto por el radar: {observedWet:0} km mojados; de los {predictedLikely:0} km con lluvia probable, mojados {hits:0}.");
Console.WriteLine($"{OperaRadarSource.AttributionText}. {OpenMeteoRainForecastSource.Attribution}.");

// The OPERA source as it was at a past moment: its latest frame is the one at or before `latest`.
sealed class PastRadar(OperaRadarSource inner, DateTimeOffset latest) : IRadarSource
{
    public TimeSpan FrameInterval => inner.FrameInterval;
    public string Attribution => inner.Attribution;

    public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RadarFrames.AtOrBefore(latest, inner.FrameInterval));

    public Task<RainGrid?> GetFrameAsync(DateTimeOffset time, Coordinate center, double radiusKm, CancellationToken cancellationToken = default) =>
        time > latest ? Task.FromResult<RainGrid?>(null) : inner.GetFrameAsync(time, center, radiusKm, cancellationToken);
}
