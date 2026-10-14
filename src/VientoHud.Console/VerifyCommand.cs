using VientoHud.Core.Location;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;
using VientoHud.Core.Wind.Verification;
using static VientoHud.ConsoleApp.Display;

namespace VientoHud.ConsoleApp;

/// <summary>Prints how well each model forecast the wind at the nearest stations. Formatting only.</summary>
internal sealed class VerifyCommand(ForecastJudge judge, ILocationProvider device, TextWriter output)
{
    private static readonly ForecastLead[] Leads = [ForecastLead.ShortRange, ForecastLead.DayAhead];

    public async Task RunAsync(VerifyOptions options, CancellationToken cancellationToken)
    {
        var (location, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var to = new DateTimeOffset(options.Until.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var from = to.AddDays(-options.Days);

        var result = await judge.RunAsync(
            new JudgeRequest(location, options.Stations, options.RadiusKm, from, to, options.Models, Leads),
            cancellationToken);

        output.WriteLine(
            $"Juez del viento · {options.Days} días: {from.ToString("d MMM yyyy", Spanish)} – " +
            $"{to.AddDays(-1).ToString("d MMM yyyy", Spanish)} (UTC)");
        output.WriteLine($"Cerca de {Coordinates(location)} ({source})");
        if (result.Stations.Count == 0)
        {
            output.WriteLine($"No hay estaciones METAR a menos de {options.RadiusKm} km. Prueba con --radius mayor.");
            return;
        }

        output.WriteLine("Estaciones (METAR): " + string.Join(" · ",
            result.Stations.Select(s => $"{s.Id} {s.Name} ({s.DistanceKm.ToString("0", Spanish)} km)")));
        output.WriteLine($"Observaciones: {result.Observations.ToString("N0", Spanish)}");
        output.WriteLine();

        var windyFrom = result.Request.Options.WindyFromKmh.ToString("0", Spanish);
        output.WriteLine($"                      {"Todas las horas",-36}  Con viento (observado ≥ {windyFrom} km/h)");
        output.WriteLine("Plazo  Modelo         Horas  Sesgo  Vel.  Dir.  ±45°        Horas  Sesgo  Vel.  Dir.  ±45°");
        output.WriteLine("-----  -----------  ------  -----  ----  ----  ----      ------  -----  ----  ----  ----");
        foreach (var lead in Leads)
        {
            foreach (var verdict in result.Verdicts.Where(v => v.Lead == lead))
            {
                output.WriteLine(
                    $"{Lead(lead),-5}  {Model(verdict.Model),-11}  {Columns(verdict.Overall.All)}      {Columns(verdict.Overall.Windy)}");
            }
        }

        output.WriteLine();
        output.WriteLine($"Con viento (≥ {windyFrom} km/h), por estación: error de velocidad (km/h) · dirección dentro de ±45°");
        output.WriteLine($"{"",-12}" + string.Join("", result.Verdicts.Select(v => $"{Model(v.Model) + " " + Lead(v.Lead),18}")));
        foreach (var station in result.Stations)
        {
            output.WriteLine($"{station.Id,-12}" + string.Join("", result.Verdicts.Select(v =>
                $"{(v.ByStation.TryGetValue(station.Id, out var report) ? Brief(report.Windy) : "sin cobertura"),18}")));
        }

        output.WriteLine();
        output.WriteLine("Sesgo: previsto − observado (negativo = el modelo se queda corto). Vel.: error medio de velocidad.");
        output.WriteLine("Dir.: error medio de dirección (sin calma ni viento variable). ±45°: horas con la dirección dentro de ±45°.");
        output.WriteLine("Corto: previsión de pocas horas antes. 24 h: previsión del día anterior.");
        foreach (var verdict in result.Verdicts.Where(v => v.NotCovered.Count > 0).DistinctBy(v => v.Model))
        {
            output.WriteLine($"{Model(verdict.Model)} no cubre: {string.Join(", ", verdict.NotCovered)}.");
        }
        output.WriteLine();
        output.WriteLine(OpenMeteoPastWindForecastSource.Attribution);
        output.WriteLine(IemWindObservationSource.Attribution);
    }

    private static string Columns(VerificationScore s) => string.Join("  ",
        s.Hours.ToString("N0", Spanish).PadLeft(6),
        Signed(s.SpeedBiasKmh).PadLeft(5),
        Number(s.SpeedMeanAbsoluteErrorKmh, "0.0").PadLeft(4),
        Degrees(s.DirectionMeanAbsoluteErrorDegrees).PadLeft(4),
        Percent(s.DirectionHitRate).PadLeft(4));

    private static string Brief(VerificationScore s) =>
        s.Hours == 0 ? "sin horas" : $"{Number(s.SpeedMeanAbsoluteErrorKmh, "0.0")} · {Percent(s.DirectionHitRate)}";

    private static string Signed(double? value) => value is { } v ? v.ToString("+0.0;-0.0;0.0", Spanish) : "—";

    private static string Number(double? value, string format) => value is { } v ? v.ToString(format, Spanish) : "—";

    private static string Degrees(double? value) => value is { } v ? $"{v.ToString("0", Spanish)}°" : "—";

    private static string Percent(double? value) => value is { } v ? $"{(v * 100).ToString("0", Spanish)}%" : "—";
}
