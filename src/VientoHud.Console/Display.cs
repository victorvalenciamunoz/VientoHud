using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Location;
using VientoHud.Core.Text;
using VientoHud.Core.Wind;

namespace VientoHud.ConsoleApp;

/// <summary>Labels that only the console shows. The Spanish words and numbers shared with the app are in <see cref="SpanishText"/>.</summary>
internal static class Display
{
    private static readonly Dictionary<string, string> ModelNames = new()
    {
        ["meteofrance_arome_france"] = "AROME",
        ["meteofrance_arome_france_hd"] = "AROME HD",
        ["best_match"] = "best_match",
        ["ecmwf_ifs"] = "ECMWF IFS",
        ["ecmwf_ifs025"] = "ECMWF 0,25°",
    };

    /// <summary>A UTC time in the local zone of this computer.</summary>
    public static string Local(DateTimeOffset utc, string format) => SpanishText.Local(utc, TimeZoneInfo.Local, format);

    public static string Model(string model) => ModelNames.GetValueOrDefault(model, model);

    public static string Lead(ForecastLead lead) => lead switch
    {
        ForecastLead.ShortRange => "corto",
        ForecastLead.DayAhead => "24 h",
        _ => lead.ToString(),
    };

    public static string Coordinates(Coordinate c) =>
        $"{c.Latitude.ToString("0.0000", CultureInfo.InvariantCulture)}, " +
        $"{c.Longitude.ToString("0.0000", CultureInfo.InvariantCulture)}";

    /// <summary>Where the location came from, for the header of each command.</summary>
    public static async Task<(Coordinate Location, string Source)> ResolveLocationAsync(
        Coordinate? given, ILocationProvider device, CancellationToken cancellationToken)
    {
        if (given is { } location)
        {
            return (location, "indicada con --lat/--lon");
        }

        var fix = await device.GetCurrentAsync(cancellationToken);
        var source = fix.AccuracyMeters is { } accuracy
            ? $"del dispositivo, ±{accuracy.ToString("0", SpanishText.Spanish)} m"
            : "del dispositivo";
        return (fix.Position, source);
    }

    /// <summary>Writes a GPX to <paramref name="directory"/> and returns the full path.</summary>
    public static async Task<string> WriteGpxAsync(string directory, string fileName, Core.Gpx.GpxTrack track, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var file = Path.GetFullPath(Path.Combine(directory, fileName));
        await using var stream = File.Create(file);
        Core.Gpx.GpxWriter.Write(track, stream);
        await stream.FlushAsync(cancellationToken);
        return file;
    }

    public static string AlertsNote(int count) => count switch
    {
        0 => "",
        1 => " (1 aviso en la ruta)",
        var n => $" ({n} avisos en la ruta)",
    };
}
