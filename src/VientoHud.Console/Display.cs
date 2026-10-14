using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Location;
using VientoHud.Core.Wind;

namespace VientoHud.ConsoleApp;

/// <summary>Spanish labels and number formats shared by the commands.</summary>
internal static class Display
{
    public static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    private static readonly Dictionary<CompassPoint, string> SpanishPoints = new()
    {
        [CompassPoint.N] = "N", [CompassPoint.NNE] = "NNE", [CompassPoint.NE] = "NE", [CompassPoint.ENE] = "ENE",
        [CompassPoint.E] = "E", [CompassPoint.ESE] = "ESE", [CompassPoint.SE] = "SE", [CompassPoint.SSE] = "SSE",
        [CompassPoint.S] = "S", [CompassPoint.SSW] = "SSO", [CompassPoint.SW] = "SO", [CompassPoint.WSW] = "OSO",
        [CompassPoint.W] = "O", [CompassPoint.WNW] = "ONO", [CompassPoint.NW] = "NO", [CompassPoint.NNW] = "NNO",
    };

    private static readonly Dictionary<string, string> ModelNames = new()
    {
        ["meteofrance_arome_france"] = "AROME",
        ["meteofrance_arome_france_hd"] = "AROME HD",
        ["best_match"] = "best_match",
        ["ecmwf_ifs"] = "ECMWF IFS",
        ["ecmwf_ifs025"] = "ECMWF 0,25°",
    };

    public static string Point(double degrees) => SpanishPoints[Compass.ToCompassPoint(degrees)];

    public static string Model(string model) => ModelNames.GetValueOrDefault(model, model);

    public static string Lead(ForecastLead lead) => lead switch
    {
        ForecastLead.ShortRange => "corto",
        ForecastLead.DayAhead => "24 h",
        _ => lead.ToString(),
    };

    public static string Kmh(double value) => $"{value.ToString("0", Spanish)} km/h";

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
            ? $"del dispositivo, ±{accuracy.ToString("0", Spanish)} m"
            : "del dispositivo";
        return (fix.Position, source);
    }
}
