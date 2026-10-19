using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Location;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;
using static VientoHud.ConsoleApp.Display;

namespace VientoHud.ConsoleApp;

/// <summary>
/// Plans loops for the wind and the rain, says whether to leave now or wait, writes one GPX per valid loop in its better
/// way round, ranked, with the summary and points that warn on the way, and prints why. Formatting only: the loops, the
/// wind, the rain, the ranking and the departure come from Core.
/// </summary>
internal sealed class RouteCommand(
    RidePlanner planner,
    IWindForecastProvider winds,
    IRadarSource radar,
    OpenMeteoRainForecastSource rainForecasts,
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
            new LineProgress(r => output.WriteLine($"  {Kind(r)} {Heading(r.BearingDegrees)}: {Outcome(r)}")),
            cancellationToken: cancellationToken);
        // The progress lines come after the planner has looked at the wind; say what it saw first.
        output.WriteLine();
        output.WriteLine(Describe(plan.Wind));
        output.WriteLine(options.Bearing is null
            ? $"Rumbos buscados: {string.Join(", ", plan.Bearings.Select(Heading))}." +
              (plan.Bearings.Count > options.Count ? " Se amplió el abanico: contra el viento no salía ninguna vuelta válida." : "")
            : $"Rumbos buscados (a partir del indicado con --bearing): {string.Join(", ", plan.Bearings.Select(Heading))}.");

        // The valid loops, ridden at each departure up to 2 h later with the wind and the rain (DECISIONS 035-036).
        var found = Enumerable.Range(0, plan.Loops.Count).Where(i => plan.Loops[i].Found).ToList();
        var routes = found.Select(i => plan.Loops[i].Best!.Route).ToList();
        var departures = DepartureChoice.Times(departure);
        RainOutlook? outlook = null;
        string? noRain = null;
        if (routes.Count > 0)
        {
            var reach = routes.SelectMany(r => r.Points).Max(p => p.Position.DistanceKmTo(start));
            var last = routes.Max(r => RouteLegs.Split(r, departures[^1], options.SpeedKmh)[^1].EndTime);
            try
            {
                outlook = await RainOutlook.ForecastAsync(
                    radar, rainForecasts, rainForecasts, start, reach, now < departure ? now : departure, last,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is RadarException or RainForecastException)
            {
                noRain = ex.Message;
            }
        }
        var advice = await DepartureChoice.ChooseAsync(
            winds, routes, departures, outlook, options.SpeedKmh, cancellationToken: cancellationToken);
        var chosen = advice.Chosen;

        output.WriteLine();
        var recommendation = Recommendation(advice, options.At is null);
        output.WriteLine(recommendation);
        if (noRain is not null)
        {
            output.WriteLine($"Sin previsión de lluvia: {noRain}");
        }
        if (plan.TownsFailure is not null)
        {
            output.WriteLine($"Sin mapa de pueblos: {plan.TownsFailure} Las rutas no los evitan.");
        }

        var stamp = Local(chosen.Departure, "yyyyMMdd-HHmm");
        Directory.CreateDirectory(options.OutputDirectory);
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
            var loop = plan.Loops[found[option.RouteIndex]];
            var outAndBack = loop.Shape == RouteShape.OutAndBack;
            var turn = outAndBack ? "ida y vuelta" : option.Route.TurnsClockwise() ? "sentido horario" : "sentido antihorario";
            var arrival = option.Winds[^1].Leg.EndTime;
            var lines = new List<string>
            {
                $"{Capitalized(Heading(loop.BearingDegrees))}, {turn}: {Advantage(option.Score)}",
                string.Join(" · ", WindStretches.Describe(option.Winds).Select(Stretch)),
            };
            if (option.Score.GustyStretches.Count > 0)
            {
                lines.Add($"¡Ojo! Rachas de lado de más de {Kmh(RideRanking.DefaultMaxCrossGustKmh)}: " +
                          string.Join(", ", option.Score.GustyStretches.Select(s =>
                              $"km {s.StartKm.ToString("0", Spanish)}-{s.EndKm.ToString("0", Spanish)} (hasta {Kmh(s.MaxCrossGustKmh)})")) + ".");
            }
            else if (!option.Score.GustsKnown)
            {
                lines.Add("Sin previsión de rachas para alguna hora: no se puede avisar de las de lado.");
            }
            lines.AddRange(RainLines(option, noRain));
            lines.Add($"Ciudad: {Km(option.Urban.UrbanKm)} ({Percent(option.Urban.UrbanFraction)}) por calles o travesías.");
            lines.Add($"{Summary(loop.Best!)}. Llegada hacia las {Local(arrival, "HH:mm")}.");

            var alerts = RideAlerts.For(option);
            var name = $"Sal {Local(chosen.Departure, "HH:mm")} · {option.Route.DistanceKm.ToString("0", Spanish)} km {Point(loop.BearingDegrees)}" +
                       (outAndBack ? " ida y vuelta" : "");
            var description = string.Join("\n",
                new[] { recommendation, $"{i + 1}.ª opción. {(outAndBack ? "Ida y vuelta" : "Vuelta")} {Km(option.Route.DistanceKm)}{(outAndBack ? "" : $", {turn}")}." }
                    .Concat(lines)
                    .Concat(Attributions(option.Route, outlook, plan.TownsAttribution)));
            var file = await WriteGpxAsync(
                options.OutputDirectory, $"{i + 1}-{FileKind(loop)}-{targetKm:0}km-{Point(loop.BearingDegrees)}-{stamp}.gpx",
                option.Route, name, chosen.Departure, description, alerts.Select(Waypoint).ToList(), cancellationToken);

            output.WriteLine($"{i + 1}. {lines[0]}");
            foreach (var line in lines.Skip(1))
            {
                output.WriteLine($"   {line}");
            }
            output.WriteLine($"   GPX: {file}{(alerts.Count switch { 0 => "", 1 => " (1 aviso en la ruta)", var n => $" ({n} avisos en la ruta)" })}");
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
                options.OutputDirectory, $"{FileKind(loop)}-{targetKm:0}km-{Point(loop.BearingDegrees)}-{stamp}-descartada.gpx",
                best.Route, $"{Kind(loop)} {Km(best.Route.DistanceKm)} hacia el {Point(loop.BearingDegrees)}", departure,
                Summary(best), [], cancellationToken);
            output.WriteLine(
                $"{Kind(loop)} {Heading(loop.BearingDegrees)}: no válida. La más cercana: {Summary(best)}. " +
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
        if (outlook is not null)
        {
            output.WriteLine(OperaRadarSourceAttribution);
        }
        if (plan.TownsAttribution is not null)
        {
            output.WriteLine(plan.TownsAttribution);
        }
        return chosen.Ranked.Count > 0 ? 0 : 1;
    }

    private static async Task<string> WriteGpxAsync(
        string directory, string name, Route route, string title, DateTimeOffset time, string description,
        IReadOnlyList<GpxWaypoint> waypoints, CancellationToken cancellationToken)
    {
        var file = Path.GetFullPath(Path.Combine(directory, name));
        await using var stream = File.Create(file);
        GpxWriter.Write(GpxTrack.FromRoute(route, title, time, description, waypoints), stream);
        await stream.FlushAsync(cancellationToken);
        return file;
    }

    private const string OperaRadarSourceAttribution = VientoHud.Core.Rain.Opera.OperaRadarSource.AttributionText;

    /// <summary>The first line: leave now or wait, and why.</summary>
    private static string Recommendation(DepartureAdvice advice, bool fromNow)
    {
        if (advice.Chosen.Best is null)
        {
            return "No hay ninguna vuelta válida.";
        }
        var first = fromNow ? "ahora" : $"a las {Local(advice.First.Departure, "HH:mm")}";
        if (!advice.Wait)
        {
            return $"Sal {first} ({Local(advice.Chosen.Departure, "HH:mm")}).";
        }
        var reasons = new List<string>();
        if (advice.GustyKmSaved > 0)
        {
            reasons.Add($"evitas {Km(advice.GustyKmSaved)} con rachas de lado peligrosas");
        }
        if (advice.RainKmSaved > 0)
        {
            reasons.Add($"te ahorras {Km(advice.RainKmSaved)} de lluvia probable");
        }
        return $"Mejor espera: sal a las {Local(advice.Chosen.Departure, "HH:mm")} en vez de {first}; " +
               $"{string.Join(" y ", reasons)}.";
    }

    /// <summary>What the rain does on the ride: stretches with likely rain, possible rain and the warning for the area.</summary>
    private static IEnumerable<string> RainLines(RideOption option, string? noRain)
    {
        if (option.Rain is not { } rain)
        {
            yield return noRain is null ? "Lluvia: sin previsión." : "Lluvia: sin previsión (no se pudo pedir).";
            yield break;
        }
        var likely = RideAlerts.For(option, int.MaxValue).Where(a => a.Kind == RideAlertKind.Rain).ToList();
        var counted = rain.UncountedKm > 0 ? $" en los primeros {Km(rain.CountedKm)}" : "";
        if (likely.Count == 0 && rain.PossibleKm == 0)
        {
            yield return $"Lluvia: no se espera{counted}.";
        }
        else
        {
            var parts = likely.Select(a =>
                $"probable en km {a.StartKm.ToString("0", Spanish)}-{a.EndKm.ToString("0", Spanish)} hacia las {Local(a.Time, "HH:mm")} ({Percent(a.Value)})")
                .ToList();
            if (rain.PossibleKm > 0)
            {
                parts.Add($"puede en {Km(rain.PossibleKm)}");
            }
            yield return $"Lluvia{counted}: {string.Join("; ", parts)}.";
        }
        if (rain.UncountedKm > 0)
        {
            yield return rain.AreaWarning is { } warning
                ? $"Desde el km {rain.CountedKm.ToString("0", Spanish)} (más de 3 h): posibles chubascos en la zona desde las " +
                  $"{Local(warning.From, "HH:mm")} (hasta {Percent(warning.MaxProbability)})."
                : $"Desde el km {rain.CountedKm.ToString("0", Spanish)} (más de 3 h): sin aviso de chubascos en la zona.";
        }
    }

    private static IEnumerable<string> Attributions(Route route, RainOutlook? outlook, string? towns)
    {
        if (route.Attribution is { } attribution)
        {
            yield return attribution;
        }
        yield return OpenMeteoRainForecastSource.Attribution;
        if (outlook is not null)
        {
            yield return OperaRadarSourceAttribution;
        }
        if (towns is not null)
        {
            yield return towns;
        }
    }

    /// <summary>A point on the way for the GPX: short name for the GPS screen, details in the description.</summary>
    private static GpxWaypoint Waypoint(RideAlert alert)
    {
        var km = $"km {alert.StartKm.ToString("0", Spanish)}";
        var stretch = $"km {alert.StartKm.ToString("0", Spanish)}-{alert.EndKm.ToString("0", Spanish)}";
        return alert.Kind switch
        {
            RideAlertKind.Gusts => new GpxWaypoint(alert.Position, $"Racha lado {alert.Value.ToString("0", Spanish)}",
                $"{stretch}: rachas de lado de hasta {Kmh(alert.Value)} hacia las {Local(alert.Time, "HH:mm")}."),
            RideAlertKind.Rain => new GpxWaypoint(alert.Position, $"Lluvia ~{Local(alert.Time, "HH:mm")}",
                $"{stretch}: lluvia probable ({Percent(alert.Value)}) hacia las {Local(alert.Time, "HH:mm")}."),
            _ => new GpxWaypoint(alert.Position, "Posibles chubascos",
                $"Desde el {km} (hacia las {Local(alert.Time, "HH:mm")}): posibles chubascos en la zona (hasta {Percent(alert.Value)})."),
        };
    }

    private static string Percent(double fraction) => $"{(fraction * 100).ToString("0", Spanish)} %";

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

    private static string Kind(LoopResult r) => r.Shape == RouteShape.OutAndBack ? "Ida y vuelta" : "Vuelta";

    // Hammerhead shows the file name, not the one inside the GPX (DECISIONS 037).
    private static string FileKind(LoopResult r) => r.Shape == RouteShape.OutAndBack ? "ida-y-vuelta" : "vuelta";

    private static string Outcome(LoopResult r) => r switch
    {
        { Found: true, Best: { } best } => $"{Km(best.Route.DistanceKm)} ({r.Requests} peticiones)",
        { EngineFailure: { } failure } => $"BRouter dejó de responder: {failure}",
        { Best: { } best } => $"no válida ({r.Requests} peticiones; la más cercana falla por {Problems(best.Problems)})",
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
