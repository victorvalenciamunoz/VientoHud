using VientoHud.Core.Location;
using VientoHud.Core.Rain;
using static VientoHud.ConsoleApp.Display;

namespace VientoHud.ConsoleApp;

/// <summary>
/// Shows the rain the radar sees now (or at a given time) around the location, and writes a PNG of the area with the
/// colours of AEMET's radar maps. Formatting only: all data comes from Core.
/// </summary>
internal sealed class RadarCommand(IRadarSource radar, ILocationProvider device, TextWriter output)
{
    public async Task<int> RunAsync(RadarOptions options, CancellationToken cancellationToken)
    {
        var (location, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var wanted = options.At is { } at
            ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(at, TimeZoneInfo.Local), TimeSpan.Zero)
            : await radar.GetLatestFrameTimeAsync(cancellationToken);

        output.WriteLine($"Radar de lluvia en {Coordinates(location)} ({source})");
        var grid = await radar.GetFrameAtOrBeforeAsync(wanted, location, options.RadiusKm, cancellationToken: cancellationToken);
        if (grid is null)
        {
            output.WriteLine($"No hay imagen de radar para {Local(wanted)} ni para las {RadarFrames.DefaultMaxStepsBack} anteriores.");
            return 1;
        }

        var rain = RainNearby.Describe(grid, location);
        output.WriteLine($"Imagen: {Local(rain.Time)} (hora local), {Age(now - rain.Time)}");
        var side = (grid.Rows * grid.CellSizeMeters / 1000).ToString("0", Spanish);
        output.WriteLine(
            $"Zona: {side} × {side} km en celdas de {(grid.CellSizeMeters / 1000).ToString("0", Spanish)} km; " +
            $"con datos el {Percent(rain.CoveredFraction)}, con lluvia el {Percent(rain.WetFraction)} de lo que tiene datos");
        output.WriteLine();

        output.WriteLine(float.IsNaN(rain.RateAtPoint) ? "Aquí: el radar no tiene datos."
            : rain.RainsAtPoint ? $"Aquí: llueve, {Rate(rain.RateAtPoint)}."
            : "Aquí: no llueve.");
        if (rain.Nearest is { DistanceKm: > 0 } nearest)
        {
            output.WriteLine($"Lluvia más cercana: {Where(nearest)}, {Rate(nearest.RateMmPerHour)}.");
        }
        if (rain.Strongest is { } strongest)
        {
            output.WriteLine($"Lo más fuerte: {Rate(strongest.RateMmPerHour)}, {Where(strongest)}.");
        }
        else
        {
            output.WriteLine("No llueve en ningún sitio de la zona.");
        }

        var frames = await radar.GetMotionFramesAsync(grid, location, options.RadiusKm, cancellationToken);
        var motion = frames.Count >= 2 ? RainMotions.Combine(frames) : null;
        output.WriteLine(Describe(motion, frames));

        // "Now" for the forecast: the time asked for, or the real now (the frame is a few minutes older).
        var issuedAt = options.At is not null ? wanted : now;
        if (issuedAt < grid.Time)
        {
            issuedAt = grid.Time;
        }
        var nowcast = Nowcast.AtPoint(grid, motion?.Motion, location, issuedAt);
        output.WriteLine();
        output.WriteLine($"Previsión aquí, desde las {LocalTime(issuedAt)}" +
            (motion?.Motion is null ? " (sin movimiento conocido: supone que la lluvia se queda donde está):" : ":"));
        foreach (var minutes in new[] { 0, 30, 60, 90 })
        {
            var label = minutes == 0 ? "ahora" : $"+{minutes} min";
            output.WriteLine($"  {label,-8} {Chance(nowcast.At(issuedAt.AddMinutes(minutes)))}");
        }
        output.WriteLine(nowcast.RainArrives is { } arrives ? $"La lluvia llega hacia las {LocalTime(arrives)}."
            : nowcast.RainStops is { } stops ? $"Deja de llover hacia las {LocalTime(stops)}."
            : nowcast.Steps[0].Likelihood == RainLikelihood.Likely ? "Sigue lloviendo durante la hora y media."
            : nowcast.Steps.Any(s => s.Likelihood == RainLikelihood.Possible) ? "Puede llover en la próxima hora y media, sin llegar a probable."
            : "No se espera lluvia en la próxima hora y media.");

        Directory.CreateDirectory(options.OutputDirectory);
        var file = Path.GetFullPath(Path.Combine(
            options.OutputDirectory, $"radar-{TimeZoneInfo.ConvertTime(rain.Time, TimeZoneInfo.Local):yyyyMMdd-HHmm}.png"));
        await using (var stream = File.Create(file))
        {
            PngWriter.Write(stream, RainImage.Render(grid, location, motion: motion?.Motion));
        }
        output.WriteLine();
        var stamp = TimeZoneInfo.ConvertTime(issuedAt, TimeZoneInfo.Local).ToString("yyyyMMdd-HHmm");
        var forecastFiles = new List<string>();
        if (motion?.Motion is { } moving)
        {
            foreach (var minutes in new[] { 30, 60 })
            {
                var ahead = Nowcast.Advect(grid, moving, issuedAt.AddMinutes(minutes));
                var path = Path.GetFullPath(Path.Combine(options.OutputDirectory, $"radar-{stamp}-mas{minutes}.png"));
                await using var forecastStream = File.Create(path);
                PngWriter.Write(forecastStream, RainImage.Render(ahead, location, motion: moving));
                forecastFiles.Add(path);
            }
        }
        output.WriteLine($"Imagen: {file}");
        foreach (var forecastFile in forecastFiles)
        {
            output.WriteLine($"Prevista: {forecastFile}");
        }
        output.WriteLine(
            $"  Colores de la leyenda de AEMET; negro: seco; gris: sin datos; cruz: tu posición; " +
            $"círculos cada {RainImage.RingSpacingKm.ToString("0", Spanish)} km. Norte arriba.");
        if (motion?.Motion is not null)
        {
            output.WriteLine("  Flecha: hacia dónde va la lluvia y hasta dónde llega en una hora.");
        }
        output.WriteLine();
        output.WriteLine(
            $"Cuenta como lluvia desde {rain.ThresholdMmPerHour.ToString("0.0", Spanish)} mm/h (lo más débil que pinta AEMET).");
        output.WriteLine(radar.Attribution);
        return 0;
    }

    private static string Describe(CombinedRainMotion? combined, IReadOnlyList<RainGrid> frames)
    {
        var minutes = (frames[^1].Time - frames[0].Time).TotalMinutes.ToString("0", Spanish);
        if (combined is null)
        {
            return "Movimiento: no se puede saber (faltan las imágenes anteriores).";
        }
        if (combined.Motion is not { } motion)
        {
            var reason = combined.Failure switch
            {
                RainMotionFailure.TooLittleRain => "llueve poco en la zona",
                RainMotionFailure.NoClearMatch => "las imágenes no se parecen lo bastante",
                RainMotionFailure.TooFast => "no se encuentra un desplazamiento claro",
                RainMotionFailure.PairsDisagree => "las imágenes dan movimientos distintos",
                _ => "",
            };
            return $"Movimiento: no se puede saber ({reason}).";
        }
        var pairs = $"{combined.PairsWithMotion} de {combined.PairsTried} parejas de imágenes de los últimos {minutes} min";
        return motion.IsAlmostStill
            ? $"La lluvia apenas se mueve ({pairs})."
            : $"La lluvia va hacia el {Point(motion.TowardsDegrees)} ({motion.TowardsDegrees.ToString("0", Spanish)}°) a " +
              $"{Kmh(motion.SpeedKmh)} ({pairs}).";
    }

    private static string Chance(RainChance chance) => chance.Probability is not { } p
        ? "sin datos del radar"
        : $"{Likelihood(chance.Likelihood!.Value)} ({Percent(p)})";

    private static string Likelihood(RainLikelihood likelihood) => likelihood switch
    {
        RainLikelihood.No => "no",
        RainLikelihood.Possible => "puede",
        RainLikelihood.Likely => "probable",
        _ => likelihood.ToString(),
    };

    private static string LocalTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).ToString("HH:mm", Spanish);

    private static string Local(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).ToString("ddd dd/MM HH:mm", Spanish);

    private static string Age(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "de ahora mismo",
        < 120 => $"de hace {age.TotalMinutes:0} min",
        < 48 * 60 => $"de hace {age.TotalHours:0} h",
        _ => $"de hace {age.TotalDays:0} días",
    };

    private static string Percent(double fraction) => $"{(fraction * 100).ToString("0", Spanish)} %";

    private static string Where(RainCell cell) =>
        $"a {cell.DistanceKm.ToString(cell.DistanceKm < 10 ? "0.0" : "0", Spanish)} km al {Point(cell.BearingDegrees)}";

    private static string Rate(float mmPerHour) => mmPerHour >= Reflectivity.MaxRainRate * 0.999
        ? $"torrencial o granizo (eco de más de {Reflectivity.CapDbz.ToString("0", Spanish)} dBZ)"
        : $"{Intensity(RainIntensities.Classify(mmPerHour))} ({mmPerHour.ToString(mmPerHour < 10 ? "0.0" : "0", Spanish)} mm/h)";

    private static string Intensity(RainIntensity intensity) => intensity switch
    {
        RainIntensity.Light => "débil",
        RainIntensity.Moderate => "moderada",
        RainIntensity.Heavy => "fuerte",
        RainIntensity.VeryHeavy => "muy fuerte",
        RainIntensity.Torrential => "torrencial",
        _ => intensity.ToString(),
    };
}
