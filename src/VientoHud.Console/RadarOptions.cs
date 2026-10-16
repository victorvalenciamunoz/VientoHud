using System.Globalization;
using VientoHud.Core;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>radar</c> command.</summary>
/// <param name="At">Local date and time of the frame to look at; null for the latest.</param>
/// <param name="RadiusKm">Half the side of the square around the location.</param>
internal sealed record RadarOptions(Coordinate? Location, DateTime? At, double RadiusKm, string OutputDirectory)
{
    public const double DefaultRadiusKm = 150;

    public const string Usage =
        "vientohud radar [--lat <grados> --lon <grados>] [--at \"<aaaa-mm-dd hh:mm>\"] [--radius <20-300 km>] [--out <carpeta>]";

    private static readonly HashSet<string> Known = ["--lat", "--lon", "--at", "--radius", "--out"];

    /// <summary>Parses the arguments after the command name. Returns null and an error message when invalid.</summary>
    public static RadarOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var values = CommandArgs.Parse(args, Known, out error);
        if (values is null
            || !CommandArgs.TryGetLocation(values, out var location, out error)
            || !CommandArgs.TryGetDouble(values, "--radius", 20, 300, out var radius, out error))
        {
            return null;
        }

        DateTime? at = null;
        if (values.TryGetValue("--at", out var atText))
        {
            if (!DateTime.TryParseExact(atText, ["yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                error = $"Fecha y hora no válidas para --at: {atText} (aaaa-mm-dd hh:mm, hora local, p. ej. \"2026-10-03 08:00\").";
                return null;
            }
            at = parsed;
        }

        return new RadarOptions(location, at, radius ?? DefaultRadiusKm, values.GetValueOrDefault("--out", "."));
    }
}
