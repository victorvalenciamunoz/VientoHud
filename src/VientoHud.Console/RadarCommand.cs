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

        Directory.CreateDirectory(options.OutputDirectory);
        var file = Path.GetFullPath(Path.Combine(
            options.OutputDirectory, $"radar-{TimeZoneInfo.ConvertTime(rain.Time, TimeZoneInfo.Local):yyyyMMdd-HHmm}.png"));
        await using (var stream = File.Create(file))
        {
            PngWriter.Write(stream, RainImage.Render(grid, location));
        }
        output.WriteLine();
        output.WriteLine($"Imagen: {file}");
        output.WriteLine(
            $"  Colores de la leyenda de AEMET; negro: seco; gris: sin datos; cruz: tu posición; " +
            $"círculos cada {RainImage.RingSpacingKm.ToString("0", Spanish)} km. Norte arriba.");
        output.WriteLine();
        output.WriteLine(
            $"Cuenta como lluvia desde {rain.ThresholdMmPerHour.ToString("0.0", Spanish)} mm/h (lo más débil que pinta AEMET).");
        output.WriteLine(radar.Attribution);
        return 0;
    }

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
