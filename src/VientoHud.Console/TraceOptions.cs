using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Routing;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>trace</c> command: a ride through points the rider gives (DECISIONS 048).</summary>
/// <param name="Via">The points, in order: one is an out and back; two or more, a loop.</param>
/// <param name="At">Local time of day to leave at (the next time it comes); null to leave now.</param>
internal sealed record TraceOptions(
    Coordinate? Location,
    IReadOnlyList<Coordinate> Via,
    double SpeedKmh,
    TimeOnly? At,
    string OutputDirectory)
{
    public const string Usage =
        "vientohud trace --via <lat,lon[;lat,lon...]> [--speed <10-45 km/h>] [--at <hh:mm>]\n" +
        "                [--lat <grados> --lon <grados>] [--out <carpeta>]   (decimales con punto)";

    private static readonly HashSet<string> Known = ["--via", "--speed", "--at", "--lat", "--lon", "--out"];

    /// <summary>Parses the arguments after the command name. Returns null and an error message when invalid.</summary>
    public static TraceOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var values = CommandArgs.Parse(args, Known, out error);
        if (values is null
            || !CommandArgs.TryGetLocation(values, out var location, out error)
            || !CommandArgs.TryGetDouble(values, "--speed", 10, 45, out var speed, out error))
        {
            return null;
        }
        if (!values.TryGetValue("--via", out var viaText))
        {
            error = "Indica al menos un punto con --via (lat,lon; varios separados por punto y coma).";
            return null;
        }
        var via = new List<Coordinate>();
        foreach (var point in viaText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = point.Split(',');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            {
                error = $"Punto no válido en --via: {point} (lat,lon con punto decimal, p. ej. 42.60,-5.30).";
                return null;
            }
            via.Add(new Coordinate(lat, lon));
        }

        TimeOnly? at = null;
        if (values.TryGetValue("--at", out var atText))
        {
            if (!TimeOnly.TryParseExact(atText, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                error = $"Hora no válida para --at: {atText} (hh:mm, p. ej. 10:30).";
                return null;
            }
            at = parsed;
        }
        return new TraceOptions(location, via, speed ?? RideDistance.DefaultAverageSpeedKmh, at, values.GetValueOrDefault("--out", "."));
    }
}
