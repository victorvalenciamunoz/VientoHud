using System.Globalization;

namespace VientoHud.Core.Text;

/// <summary>
/// Spanish words and number formats shared by the console and the app. Times are shown in the zone the caller gives:
/// Core keeps everything in UTC.
/// </summary>
public static class SpanishText
{
    public static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    private static readonly Dictionary<CompassPoint, string> Points = new()
    {
        [CompassPoint.N] = "N", [CompassPoint.NNE] = "NNE", [CompassPoint.NE] = "NE", [CompassPoint.ENE] = "ENE",
        [CompassPoint.E] = "E", [CompassPoint.ESE] = "ESE", [CompassPoint.SE] = "SE", [CompassPoint.SSE] = "SSE",
        [CompassPoint.S] = "S", [CompassPoint.SSW] = "SSO", [CompassPoint.SW] = "SO", [CompassPoint.WSW] = "OSO",
        [CompassPoint.W] = "O", [CompassPoint.WNW] = "ONO", [CompassPoint.NW] = "NO", [CompassPoint.NNW] = "NNO",
    };

    /// <summary>The compass point of a bearing or a wind direction, in Spanish (O for west).</summary>
    public static string Point(double degrees) => Points[Compass.ToCompassPoint(degrees)];

    /// <summary>"hacia el SO (225°)".</summary>
    public static string Heading(double bearing) => $"hacia el {Point(bearing)} ({bearing.ToString("0", Spanish)}°)";

    public static string Km(double km) => $"{km.ToString("0.#", Spanish)} km";

    public static string Kmh(double value) => $"{value.ToString("0", Spanish)} km/h";

    /// <summary>"+8 km/h", "-3 km/h", "0 km/h".</summary>
    public static string Signed(double kmh) => $"{kmh.ToString("+0;-0;0", Spanish)} km/h";

    public static string Percent(double fraction) => $"{(fraction * 100).ToString("0", Spanish)} %";

    /// <summary>A UTC time shown in <paramref name="zone"/>.</summary>
    public static string Local(DateTimeOffset utc, TimeZoneInfo zone, string format) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString(format, Spanish);

    public static string Capitalized(string text) => char.ToUpper(text[0], Spanish) + text[1..];
}
