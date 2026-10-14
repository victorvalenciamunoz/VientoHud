using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Location;
using VientoHud.Core.Wind;

namespace VientoHud.ConsoleApp;

/// <summary>Prints the hourly wind forecast as a table. Formatting only: all data comes from Core.</summary>
internal sealed class WindCommand(IWindForecastProvider forecasts, ILocationProvider device, TextWriter output)
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

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
    };

    public async Task RunAsync(WindOptions options, CancellationToken cancellationToken)
    {
        Coordinate location;
        string source;
        if (options.Location is { } given)
        {
            location = given;
            source = "indicada con --lat/--lon";
        }
        else
        {
            var fix = await device.GetCurrentAsync(cancellationToken);
            location = fix.Position;
            source = fix.AccuracyMeters is { } accuracy
                ? $"del dispositivo, ±{accuracy.ToString("0", Spanish)} m"
                : "del dispositivo";
        }

        var forecast = await forecasts.GetHourlyAsync(location, DateTimeOffset.UtcNow, options.Hours, cancellationToken);

        output.WriteLine($"Viento previsto a 10 m en {Format(location)} ({source})");
        output.WriteLine(
            $"Celda del modelo: {Format(forecast.GridPoint)}, {forecast.ElevationMeters.ToString("0", Spanish)} m de altitud");
        output.WriteLine();
        output.WriteLine("Hora (local)    Viento   Racha    Viene del       Modelo");
        output.WriteLine("--------------  -------  -------  --------------  ----------");
        foreach (var hour in forecast.Hours)
        {
            var local = TimeZoneInfo.ConvertTime(hour.Time, TimeZoneInfo.Local);
            var point = Point(hour.DirectionFromDegrees);
            output.WriteLine(string.Join("  ",
                local.ToString("ddd dd HH:mm", Spanish).PadRight(14),
                Kmh(hour.SpeedKmh).PadLeft(7),
                Kmh(hour.GustKmh).PadLeft(7),
                $"{point,-3} ({hour.DirectionFromDegrees.ToString("0", Spanish),3}°)".PadRight(14),
                ModelNames.GetValueOrDefault(hour.Model, hour.Model)));
        }
        output.WriteLine();
        output.WriteLine(Describe(StartDirection.Recommend(forecast.Hours)));
        output.WriteLine();
        output.WriteLine(forecast.Attribution);
    }

    private static string Describe(StartDirectionAdvice advice)
    {
        var period = advice.Hours == 1 ? "en la próxima hora" : $"en las próximas {advice.Hours} h";
        return advice.Kind switch
        {
            StartAdviceKind.HeadIntoWind =>
                $"Empieza hacia el {Point(advice.StartBearingDegrees!.Value)} " +
                $"({advice.StartBearingDegrees.Value.ToString("0", Spanish)}°): " +
                $"viento medio del {Point(advice.MeanWindFromDegrees!.Value)} a {Kmh(advice.MeanVectorSpeedKmh)} {period}. " +
                "Ida de cara, vuelta a favor.",
            StartAdviceKind.Calm =>
                // One decimal: 4.8 rounded to "5 km/h" would read as contradicting the "below 5 km/h" threshold.
                $"Viento flojo (media {advice.MeanSpeedKmh.ToString("0.0", Spanish)} km/h {period}): " +
                "da igual hacia dónde empieces.",
            StartAdviceKind.Variable =>
                $"Viento variable: media {Kmh(advice.MeanSpeedKmh)}, pero cambia mucho de dirección {period}. " +
                "No hay un sentido claramente mejor.",
            _ => throw new ArgumentOutOfRangeException(nameof(advice), advice.Kind, null),
        };
    }

    private static string Point(double degrees) => SpanishPoints[Compass.ToCompassPoint(degrees)];

    private static string Kmh(double value) => $"{value.ToString("0", Spanish)} km/h";

    private static string Format(Coordinate c) =>
        $"{c.Latitude.ToString("0.0000", CultureInfo.InvariantCulture)}, " +
        $"{c.Longitude.ToString("0.0000", CultureInfo.InvariantCulture)}";
}
