using VientoHud.Core.Location;
using VientoHud.Core.Wind;
using static VientoHud.ConsoleApp.Display;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.ConsoleApp;

/// <summary>Prints the hourly wind forecast as a table. Formatting only: all data comes from Core.</summary>
internal sealed class WindCommand(IWindForecastProvider forecasts, ILocationProvider device, TextWriter output)
{
    public async Task RunAsync(WindOptions options, CancellationToken cancellationToken)
    {
        var (location, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var forecast = await forecasts.GetHourlyAsync(location, DateTimeOffset.UtcNow, options.Hours, cancellationToken);

        output.WriteLine($"Viento previsto a 10 m en {Coordinates(location)} ({source})");
        output.WriteLine(
            $"Celda del modelo: {Coordinates(forecast.GridPoint)}, {forecast.ElevationMeters.ToString("0", Spanish)} m de altitud");
        output.WriteLine();
        output.WriteLine("Hora (local)    Viento   Racha    Viene del       Modelo");
        output.WriteLine("--------------  -------  -------  --------------  ----------");
        foreach (var hour in forecast.Hours)
        {
            var local = TimeZoneInfo.ConvertTime(hour.Time, TimeZoneInfo.Local);
            output.WriteLine(string.Join("  ",
                local.ToString("ddd dd HH:mm", Spanish).PadRight(14),
                Kmh(hour.SpeedKmh).PadLeft(7),
                (hour.GustKmh is { } gust ? Kmh(gust) : "—").PadLeft(7),
                $"{Point(hour.DirectionFromDegrees),-3} ({hour.DirectionFromDegrees.ToString("0", Spanish),3}°)".PadRight(14),
                Model(hour.Model)));
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
            // One decimal: 4.8 rounded to "5 km/h" would read as contradicting the "below 5 km/h" threshold.
            StartAdviceKind.Calm =>
                $"Viento flojo (media {advice.MeanSpeedKmh.ToString("0.0", Spanish)} km/h {period}): " +
                "da igual hacia dónde empieces.",
            StartAdviceKind.Variable =>
                $"Viento variable: media {Kmh(advice.MeanSpeedKmh)}, pero cambia mucho de dirección {period}. " +
                "No hay un sentido claramente mejor.",
            _ => throw new ArgumentOutOfRangeException(nameof(advice), advice.Kind, null),
        };
    }
}
