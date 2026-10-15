using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;
using static VientoHud.ConsoleApp.Display;

namespace VientoHud.ConsoleApp;

/// <summary>
/// Plans loops for the wind, writes one GPX per valid loop in its better way round, ranked, and prints why. Formatting
/// only: the loops, the wind, the ranking and the distance come from Core.
/// </summary>
internal sealed class RouteCommand(
    RidePlanner planner,
    ILocationProvider device,
    TextWriter output,
    string engineName,
    string windAttribution)
{
    /// <summary>0 when at least one valid loop was written, 1 otherwise.</summary>
    public async Task<int> RunAsync(RouteOptions options, CancellationToken cancellationToken)
    {
        var (start, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var targetKm = options.Km ?? RideDistance.FromDuration(TimeSpan.FromHours(options.Hours!.Value), options.SpeedKmh);
        var now = DateTimeOffset.UtcNow;
        var departure = options.At is { } at ? DepartureTime.Next(at, now, TimeZoneInfo.Local) : now;

        output.WriteLine($"Vueltas de {Km(targetKm)} desde {Coordinates(start)} ({source})");
        output.WriteLine(
            $"Salida {Local(departure, "ddd dd/MM HH:mm")}{(options.At is null ? " (ahora)" : "")} a {Kmh(options.SpeedKmh)} " +
            $"de media: llegada hacia las {Local(departure.AddHours(targetKm / options.SpeedKmh), "HH:mm")}.");
        output.WriteLine($"Motor de rutas: BRouter en {engineName}.");
        output.WriteLine();

        var plan = await planner.PlanAsync(
            start, targetKm, departure, options.SpeedKmh, options.Count, options.Bearing,
            new LineProgress(r => output.WriteLine($"  {Heading(r.BearingDegrees)}: {Outcome(r)}")),
            cancellationToken);
        // The progress lines come after the planner has looked at the wind; say what it saw first.
        output.WriteLine();
        output.WriteLine(Describe(plan.Wind));
        output.WriteLine(options.Bearing is null
            ? $"Rumbos buscados: {string.Join(", ", plan.Bearings.Select(Heading))}." +
              (plan.Bearings.Count > options.Count ? " Se amplió el abanico: contra el viento no salía ninguna vuelta válida." : "")
            : $"Rumbos buscados (a partir del indicado con --bearing): {string.Join(", ", plan.Bearings.Select(Heading))}.");

        var stamp = Local(departure, "yyyyMMdd-HHmm");
        Directory.CreateDirectory(options.OutputDirectory);
        output.WriteLine();
        if (plan.Ranked.Count == 0)
        {
            output.WriteLine("No hay ninguna vuelta válida.");
        }
        else if (plan.Wind.Kind != StartAdviceKind.HeadIntoWind)
        {
            output.WriteLine("Con este viento el orden apenas importa.");
        }
        for (var i = 0; i < plan.Ranked.Count; i++)
        {
            var option = plan.Ranked[i];
            var loop = plan.Loops[option.RouteIndex];
            var turn = option.Route.TurnsClockwise() ? "sentido horario" : "sentido antihorario";
            var title = $"{i + 1}. Vuelta {Km(option.Route.DistanceKm)} hacia el {Point(loop.BearingDegrees)}, {turn}";
            var file = await WriteGpxAsync(
                options.OutputDirectory, $"{i + 1}-vuelta-{targetKm:0}km-{Point(loop.BearingDegrees)}-{stamp}.gpx",
                option.Route, title, departure, Summary(loop.Best!), cancellationToken);

            output.WriteLine($"{i + 1}. {Capitalized(Heading(loop.BearingDegrees))}, {turn}: {Advantage(option.Score)}");
            output.WriteLine($"   {string.Join(" · ", WindStretches.Describe(option.Winds).Select(Stretch))}");
            if (option.Score.GustyStretches.Count > 0)
            {
                output.WriteLine(
                    $"   ¡Ojo! Rachas de lado de más de {Kmh(RideRanking.DefaultMaxCrossGustKmh)}: " +
                    string.Join(", ", option.Score.GustyStretches.Select(s =>
                        $"km {s.StartKm.ToString("0", Spanish)}-{s.EndKm.ToString("0", Spanish)} (hasta {Kmh(s.MaxCrossGustKmh)})")) + ".");
            }
            else if (!option.Score.GustsKnown)
            {
                output.WriteLine("   Sin previsión de rachas para alguna hora: no se puede avisar de las de lado.");
            }
            output.WriteLine(
                $"   {Summary(loop.Best!)}. Llegada hacia las {Local(option.Winds[^1].Leg.EndTime, "HH:mm")}.");
            output.WriteLine($"   GPX: {file}");
        }

        // Bearings without a valid loop: the closest one is still written, marked as discarded (as in phase 2).
        foreach (var loop in plan.Loops.Where(l => !l.Found))
        {
            output.WriteLine();
            if (loop.Best is not { } best)
            {
                output.WriteLine($"{Capitalized(Heading(loop.BearingDegrees))}: sin ruta. {Outcome(loop)}");
                continue;
            }
            var file = await WriteGpxAsync(
                options.OutputDirectory, $"vuelta-{targetKm:0}km-{Point(loop.BearingDegrees)}-{stamp}-descartada.gpx",
                best.Route, $"Vuelta {Km(best.Route.DistanceKm)} hacia el {Point(loop.BearingDegrees)}", departure,
                Summary(best), cancellationToken);
            output.WriteLine(
                $"{Capitalized(Heading(loop.BearingDegrees))}: sin vuelta válida. La más cercana: {Summary(best)}. " +
                $"Falla: {Problems(best.Problems)}.");
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
        return plan.Ranked.Count > 0 ? 0 : 1;
    }

    private static async Task<string> WriteGpxAsync(
        string directory, string name, Route route, string title, DateTimeOffset time, string description,
        CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(Path.Combine(directory, name));
        await using var stream = File.Create(file);
        GpxWriter.Write(GpxTrack.FromRoute(route, title, time, description), stream);
        await stream.FlushAsync(cancellationToken);
        return file;
    }

    private static string Local(DateTimeOffset utc, string format) =>
        TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local).ToString(format, Spanish);

    private static string Heading(double bearing) => $"hacia el {Point(bearing)} ({bearing.ToString("0", Spanish)}°)";

    private static string Capitalized(string text) => char.ToUpper(text[0], Spanish) + text[1..];

    private static string Km(double km) => $"{km.ToString("0.#", Spanish)} km";

    private static string Signed(double kmh) => $"{kmh.ToString("+0;-0;0", Spanish)} km/h";

    private static string Describe(StartDirectionAdvice wind) => wind.Kind switch
    {
        StartAdviceKind.HeadIntoWind =>
            $"Viento durante la salida: del {Point(wind.MeanWindFromDegrees!.Value)} " +
            $"({wind.MeanWindFromDegrees.Value.ToString("0", Spanish)}°) a {Kmh(wind.MeanVectorSpeedKmh)} de media. " +
            "Se buscan vueltas contra el viento: ida de cara, vuelta a favor.",
        StartAdviceKind.Calm =>
            $"Viento flojo durante la salida (media {wind.MeanSpeedKmh.ToString("0.0", Spanish)} km/h): da igual hacia dónde.",
        StartAdviceKind.Variable =>
            $"Viento variable durante la salida (media {Kmh(wind.MeanSpeedKmh)}, cambia mucho de dirección): " +
            "no hay un rumbo claramente mejor.",
        _ => throw new ArgumentOutOfRangeException(nameof(wind), wind.Kind, null),
    };

    private static string Advantage(RideScore score) =>
        $"ventaja de viento {Signed(score.WindAdvantageKmh)} " +
        $"({Along(score.FirstHalfHeadwindKmh)} en la 1.ª mitad, {Along(score.SecondHalfHeadwindKmh)} en la 2.ª)";

    // Wind along the road as it feels: in the face or from behind.
    private static string Along(double headwindKmh) => Math.Round(headwindKmh) switch
    {
        > 0 => $"de cara {Kmh(headwindKmh)}",
        < 0 => $"a favor {Kmh(-headwindKmh)}",
        _ => "sin viento de cara ni a favor",
    };

    private static string Stretch(WindStretch s)
    {
        var feel = s.Feel switch
        {
            WindFeel.Headwind => "de cara",
            WindFeel.Tailwind => "a favor",
            WindFeel.FromRight => "de lado por la derecha",
            WindFeel.FromLeft => "de lado por la izquierda",
            _ => "flojo",
        };
        return $"km {s.StartKm.ToString("0", Spanish)}-{s.EndKm.ToString("0", Spanish)}: {feel} {Kmh(s.MeanKmh)}";
    }

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
