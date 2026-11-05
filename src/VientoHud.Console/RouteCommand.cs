using VientoHud.Core.Gpx;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;
using VientoHud.Core.Wind;
using static VientoHud.ConsoleApp.Display;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.ConsoleApp;

/// <summary>
/// Plans loops for the wind and the rain, says whether to leave now or wait, writes one GPX per valid loop in its better
/// way round, ranked, with the summary and points that warn on the way, and prints why. Layout only: the loops, the
/// wind, the rain, the ranking, the departure and every sentence come from Core.
/// </summary>
internal sealed class RouteCommand(
    RideAdvisor advisor,
    ILocationProvider device,
    TextWriter output,
    string engineName,
    string windAttribution)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;

    /// <summary>0 when at least one valid loop was written, 1 otherwise.</summary>
    public async Task<int> RunAsync(RouteOptions options, CancellationToken cancellationToken)
    {
        var (start, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var targetKm = options.Km ?? RideDistance.FromDuration(TimeSpan.FromHours(options.Hours!.Value), options.SpeedKmh);
        var now = DateTimeOffset.UtcNow;
        var departure = options.At is { } at ? DepartureTime.Next(at, now, Zone) : now;

        output.WriteLine($"Vueltas de {Km(targetKm)} desde {Coordinates(start)} ({source})");
        output.WriteLine(
            $"Salida {Local(departure, "ddd dd/MM HH:mm")}{(options.At is null ? " (ahora)" : "")} a {Kmh(options.SpeedKmh)} " +
            $"de media: llegada hacia las {Local(departure.AddHours(targetKm / options.SpeedKmh), "HH:mm")}.");
        output.WriteLine($"Motor de rutas: BRouter en {engineName}.");
        output.WriteLine();

        var ride = await advisor.AdviseAsync(
            new RideRequest(start, targetKm, departure, options.SpeedKmh, options.Count, options.Bearing), now,
            new LineProgress(r => output.WriteLine($"  {RideText.Kind(r.Shape)} {Heading(r.BearingDegrees)}: {RideText.Outcome(r)}")),
            cancellationToken);
        var plan = ride.Plan;
        var chosen = ride.Departure.Chosen;
        // The progress lines come after the planner has looked at the wind; say what it saw first.
        output.WriteLine();
        output.WriteLine(RideText.Wind(plan.Wind));
        output.WriteLine(options.Bearing is null
            ? $"Rumbos buscados: {string.Join(", ", plan.Bearings.Select(Heading))}." +
              (plan.Bearings.Count > options.Count ? " Se amplió el abanico: contra el viento no salía ninguna vuelta válida." : "")
            : $"Rumbos buscados (a partir del indicado con --bearing): {string.Join(", ", plan.Bearings.Select(Heading))}.");

        output.WriteLine();
        var recommendation = RideText.Recommendation(ride.Departure, options.At is null, Zone);
        output.WriteLine(recommendation);
        if (ride.RainFailure is not null)
        {
            output.WriteLine($"Sin previsión de lluvia: {ride.RainFailure}");
        }

        output.WriteLine();
        if (chosen.Ranked.Count == 0)
        {
            output.WriteLine("No hay ninguna vuelta válida.");
        }
        else if (plan.Wind.Kind != StartAdviceKind.HeadIntoWind)
        {
            output.WriteLine("Con este viento el orden apenas importa.");
        }
        for (var i = 0; i < chosen.Ranked.Count; i++)
        {
            var option = chosen.Ranked[i];
            var loop = ride.LoopOf(option);
            var turn = RideText.Turn(option, loop.Shape);
            var lines = new List<string> { $"{Capitalized(Heading(loop.BearingDegrees))}, {turn}: {RideText.Advantage(option.Score)}" };
            lines.AddRange(RideText.Lines(option, ride.RainFailure, Zone));
            lines.Add($"{RideText.LoopSummary(loop.Best!)}. {RideText.Arrival(option, Zone)}");

            var outAndBack = loop.Shape == RouteShape.OutAndBack;
            var track = RideGpx.Track(
                option, RideGpx.Name(chosen.Departure, option.Route.DistanceKm, loop.BearingDegrees, loop.Shape, Zone), chosen.Departure,
                new[] { recommendation, $"{i + 1}.ª opción. {RideText.Kind(loop.Shape)} {Km(option.Route.DistanceKm)}{(outAndBack ? "" : $", {turn}")}." }
                    .Concat(lines)
                    .Concat(RideText.Attributions(option.Route, ride.Rain)),
                Zone);
            var file = await WriteGpxAsync(
                options.OutputDirectory, RideGpx.FileName(loop.Shape, targetKm, loop.BearingDegrees, chosen.Departure, Zone, i + 1),
                track, cancellationToken);

            output.WriteLine($"{i + 1}. {lines[0]}");
            foreach (var line in lines.Skip(1))
            {
                output.WriteLine($"   {line}");
            }
            output.WriteLine($"   GPX: {file}{AlertsNote(track.Waypoints.Count)}");
        }

        // Bearings without a valid loop: the closest one is still written, marked as discarded (as in phase 2).
        foreach (var loop in plan.Loops.Where(l => !l.Found))
        {
            output.WriteLine();
            if (loop.Best is not { } best)
            {
                output.WriteLine($"{Capitalized(Heading(loop.BearingDegrees))}: sin ruta. {RideText.Outcome(loop)}");
                continue;
            }
            var file = await WriteGpxAsync(
                options.OutputDirectory,
                RideGpx.FileName(loop.Shape, targetKm, loop.BearingDegrees, chosen.Departure, Zone, discarded: true),
                GpxTrack.FromRoute(
                    best.Route, $"{RideText.Kind(loop.Shape)} {Km(best.Route.DistanceKm)} hacia el {Point(loop.BearingDegrees)}", departure,
                    RideText.LoopSummary(best)),
                cancellationToken);
            output.WriteLine(
                $"{RideText.Kind(loop.Shape)} {Heading(loop.BearingDegrees)}: no válida. La más cercana: {RideText.LoopSummary(best)}. " +
                $"Falla: {RideText.Problems(best.Problems)}.");
            output.WriteLine($"   GPX (descartada): {file}");
        }

        output.WriteLine();
        if (plan.Loops.LastOrDefault(r => r.EngineFailure is not null) is { } failed && plan.Loops.Count < plan.Bearings.Count)
        {
            output.WriteLine(
                $"BRouter dejó de responder ({failed.EngineFailure}); no se han probado los rumbos que faltaban.");
        }
        output.WriteLine($"{plan.Loops.Sum(r => r.Requests)} peticiones al motor de rutas.");
        if (plan.Loops.Select(r => r.Best?.Route.Attribution).FirstOrDefault(a => a is not null) is { } attribution)
        {
            output.WriteLine(attribution);
        }
        output.WriteLine(windAttribution);
        if (ride.Rain is not null)
        {
            output.WriteLine(Core.Rain.Opera.OperaRadarSource.AttributionText);
        }
        return chosen.Ranked.Count > 0 ? 0 : 1;
    }

    /// <summary>Writes each report at once, on the generator's thread, so lines come out as each bearing finishes.</summary>
    private sealed class LineProgress(Action<LoopResult> write) : IProgress<LoopResult>
    {
        public void Report(LoopResult value) => write(value);
    }
}
