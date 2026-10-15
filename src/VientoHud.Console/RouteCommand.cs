using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using static VientoHud.ConsoleApp.Display;

namespace VientoHud.ConsoleApp;

/// <summary>
/// Generates loops towards several bearings, writes one GPX per bearing and prints a summary. Formatting only: the
/// loops, their checks and the distance come from Core.
/// </summary>
internal sealed class RouteCommand(LoopGenerator generator, ILocationProvider device, TextWriter output, string engineName)
{
    /// <summary>0 when at least one valid loop was written, 1 otherwise.</summary>
    public async Task<int> RunAsync(RouteOptions options, CancellationToken cancellationToken)
    {
        var (start, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var targetKm = options.Km ?? RideDistance.FromDuration(TimeSpan.FromHours(options.Hours!.Value), options.SpeedKmh);
        var bearings = LoopGenerator.SpreadBearings(options.Count, options.Bearing ?? 0);

        output.WriteLine($"Vueltas de {Km(targetKm)} desde {Coordinates(start)} ({source})");
        if (options.Hours is { } hours)
        {
            output.WriteLine($"({hours.ToString("0.#", Spanish)} h a {Kmh(options.SpeedKmh)} de media)");
        }
        output.WriteLine($"Rumbos: {string.Join(", ", bearings.Select(Heading))}. Motor de rutas: BRouter en {engineName}.");
        output.WriteLine();

        var results = await generator.GenerateManyAsync(
            start, bearings, targetKm, new LineProgress(r => output.WriteLine($"  {Heading(r.BearingDegrees)}: {Outcome(r)}")),
            cancellationToken);

        var created = DateTimeOffset.UtcNow;
        var stamp = TimeZoneInfo.ConvertTime(created, TimeZoneInfo.Local).ToString("yyyyMMdd-HHmm", Spanish);
        Directory.CreateDirectory(options.OutputDirectory);
        output.WriteLine();
        var rank = 0;
        foreach (var result in results)
        {
            rank++;
            if (result.Best is not { } best)
            {
                output.WriteLine($"{rank}. {Heading(result.BearingDegrees)}: sin ruta. {Outcome(result)}");
                continue;
            }

            var file = Path.GetFullPath(Path.Combine(
                options.OutputDirectory,
                $"vuelta-{targetKm:0}km-{Point(result.BearingDegrees)}-{stamp}{(result.Found ? "" : "-descartada")}.gpx"));
            var title = $"Vuelta {Km(best.Route.DistanceKm)} hacia el {Point(result.BearingDegrees)}";
            await using (var stream = File.Create(file))
            {
                GpxWriter.Write(GpxTrack.FromRoute(best.Route, title, created, Summary(best)), stream);
            }

            output.WriteLine(result.Found
                ? $"{rank}. {Heading(result.BearingDegrees)}: {Summary(best)}"
                : $"{rank}. {Heading(result.BearingDegrees)}: sin vuelta válida. La más cercana: {Summary(best)}. " +
                  $"Falla: {Problems(best.Problems)}.");
            output.WriteLine($"   {(result.Found ? "GPX" : "GPX (descartada)")}: {file}");
        }

        output.WriteLine();
        if (results.LastOrDefault(r => r.EngineFailure is not null) is { } failed && results.Count < bearings.Count)
        {
            output.WriteLine(
                $"BRouter dejó de responder ({failed.EngineFailure}); no se han probado los rumbos que faltaban.");
        }
        output.WriteLine($"{results.Sum(r => r.Requests)} peticiones al motor de rutas.");
        if (results.Select(r => r.Best?.Route.Attribution).FirstOrDefault(a => a is not null) is { } attribution)
        {
            output.WriteLine(attribution);
        }
        return results.Any(r => r.Found) ? 0 : 1;
    }

    private static string Heading(double bearing) => $"hacia el {Point(bearing)} ({bearing.ToString("0", Spanish)}°)";

    private static string Km(double km) => $"{km.ToString("0.#", Spanish)} km";

    private static string Outcome(LoopResult r) => r switch
    {
        { Found: true, Best: { } best } => $"vuelta de {Km(best.Route.DistanceKm)} ({r.Requests} peticiones)",
        { EngineFailure: { } failure } => $"BRouter dejó de responder: {failure}",
        { Best: { } best } => $"sin vuelta válida ({r.Requests} peticiones; la más cercana falla por {Problems(best.Problems)})",
        _ => $"BRouter no pudo unir los puntos en ningún rumbo cercano ({r.Requests} peticiones)",
    };

    /// <summary>One line: distance, climb and what kind of roads; also the GPX description.</summary>
    private static string Summary(LoopCandidate c)
    {
        var a = c.Analysis;
        var parts = new List<string>
        {
            $"{Km(c.Route.DistanceKm)} ({(c.DistanceError * 100).ToString("+0;-0;0", Spanish)} %)",
        };
        if (c.Route.AscentMeters is { } ascent)
        {
            parts.Add($"desnivel {ascent.ToString("0", Spanish)} m");
        }
        parts.Add($"nacionales (trunk) {Km(a.TrunkKm)}");
        if (a.PavedTrackKm >= 0.05)
        {
            parts.Add($"caminos asfaltados {Km(a.PavedTrackKm)}");
        }
        parts.Add($"repite {(a.RepeatedFraction * 100).ToString("0", Spanish)} %");
        return string.Join(", ", parts);
    }

    private static readonly (LoopProblems Problem, string Name)[] ProblemNames =
    [
        (LoopProblems.Distance, "distancia"),
        (LoopProblems.Spurs, "ramales (forma de estrella)"),
        (LoopProblems.TurnBacks, "medias vueltas"),
        (LoopProblems.Forbidden, "tramos prohibidos"),
        (LoopProblems.Bearing, "se aleja del rumbo"),
    ];

    private static string Problems(LoopProblems problems) =>
        string.Join(", ", ProblemNames.Where(p => problems.HasFlag(p.Problem)).Select(p => p.Name));

    /// <summary>Writes each report at once, on the generator's thread, so lines come out as each bearing finishes.</summary>
    private sealed class LineProgress(Action<LoopResult> write) : IProgress<LoopResult>
    {
        public void Report(LoopResult value) => write(value);
    }
}
