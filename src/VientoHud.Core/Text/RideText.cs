using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.Core.Text;

/// <summary>
/// The sentences the console and the app show about a ride, in Spanish, so that both say exactly the same
/// (DECISIONS 037, phase 7). Layout (what goes on which line, indentation) is up to the caller.
/// </summary>
public static class RideText
{
    /// <summary>The first line: leave now or wait, and why. <paramref name="fromNow"/>: the first departure is "ahora".</summary>
    public static string Recommendation(DepartureAdvice advice, bool fromNow, TimeZoneInfo zone)
    {
        if (advice.Chosen.Best is null)
        {
            return "No hay ninguna vuelta válida.";
        }
        var first = fromNow ? "ahora" : $"a las {Local(advice.First.Departure, zone, "HH:mm")}";
        if (!advice.Wait)
        {
            return $"Sal {first} ({Local(advice.Chosen.Departure, zone, "HH:mm")}).";
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
        return $"Mejor espera: sal a las {Local(advice.Chosen.Departure, zone, "HH:mm")} en vez de {first}; " +
               $"{string.Join(" y ", reasons)}.";
    }

    /// <summary>The wind at the start over the ride, and what it says about where to head.</summary>
    public static string Wind(StartDirectionAdvice wind) => wind.Kind switch
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

    /// <summary>The headline of the app: "Hacia el O", or that it does not matter.</summary>
    public static string DirectionHeadline(StartDirectionAdvice wind) => wind.Kind switch
    {
        StartAdviceKind.HeadIntoWind => $"Hacia el {Point(wind.StartBearingDegrees!.Value)}",
        StartAdviceKind.Calm => "Da igual hacia dónde",
        StartAdviceKind.Variable => "Sin rumbo claro",
        _ => throw new ArgumentOutOfRangeException(nameof(wind), wind.Kind, null),
    };

    public static string Advantage(RideScore score) =>
        $"ventaja de viento {Signed(score.WindAdvantageKmh)} " +
        $"({Along(score.FirstHalfHeadwindKmh)} en la 1.ª mitad, {Along(score.SecondHalfHeadwindKmh)} en la 2.ª)";

    /// <summary>Wind along the road as it feels: in the face or from behind.</summary>
    public static string Along(double headwindKmh) => Math.Round(headwindKmh) switch
    {
        > 0 => $"de cara {Kmh(headwindKmh)}",
        < 0 => $"a favor {Kmh(-headwindKmh)}",
        _ => "sin viento de cara ni a favor",
    };

    public static string Stretch(WindStretch s)
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

    /// <summary>The wind stretch by stretch: "km 0-10: de cara 12 km/h · km 10-25: flojo 4 km/h".</summary>
    public static string Stretches(IReadOnlyList<LegWind> winds) =>
        string.Join(" · ", WindStretches.Describe(winds).Select(Stretch));

    /// <summary>The dangerous gusts across the road, or that they could not be looked at; null when there are none.</summary>
    public static string? Gusts(RideScore score)
    {
        if (score.GustyStretches.Count > 0)
        {
            return $"¡Ojo! Rachas de lado de más de {Kmh(RideRanking.DefaultMaxCrossGustKmh)}: " +
                   string.Join(", ", score.GustyStretches.Select(s =>
                       $"km {s.StartKm.ToString("0", Spanish)}-{s.EndKm.ToString("0", Spanish)} (hasta {Kmh(s.MaxCrossGustKmh)})")) + ".";
        }
        return score.GustsKnown ? null : "Sin previsión de rachas para alguna hora: no se puede avisar de las de lado.";
    }

    /// <summary>What the rain does on the ride: stretches with likely rain, possible rain and the warning for the area.</summary>
    /// <param name="rainFailure">Why the rain could not be asked for, when it could not.</param>
    /// <param name="rainPending">The rain was not asked for yet (the app shows the wind first).</param>
    public static IEnumerable<string> RainLines(RideOption option, string? rainFailure, TimeZoneInfo zone, bool rainPending = false)
    {
        if (option.Rain is not { } rain)
        {
            yield return rainPending ? "Lluvia: pidiendo la previsión…"
                : rainFailure is null ? "Lluvia: sin previsión." : "Lluvia: sin previsión (no se pudo pedir).";
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
                $"probable en km {a.StartKm.ToString("0", Spanish)}-{a.EndKm.ToString("0", Spanish)} hacia las {Local(a.Time, zone, "HH:mm")} ({Percent(a.Value)})")
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
                  $"{Local(warning.From, zone, "HH:mm")} (hasta {Percent(warning.MaxProbability)})."
                : $"Desde el km {rain.CountedKm.ToString("0", Spanish)} (más de 3 h): sin aviso de chubascos en la zona.";
        }
    }

    public static string Urban(UrbanMeasure urban) =>
        $"Ciudad: {Km(urban.UrbanKm)} ({Percent(urban.UrbanFraction)}) por calles o travesías.";

    public static string Arrival(RideOption option, TimeZoneInfo zone) =>
        $"Llegada hacia las {Local(option.Winds[^1].Leg.EndTime, zone, "HH:mm")}.";

    /// <summary>
    /// The lines every ride shows after its heading: the wind stretch by stretch, the gusts if any, the rain and the
    /// town. The caller adds the heading before and the summary and arrival after.
    /// </summary>
    public static IReadOnlyList<string> Lines(RideOption option, string? rainFailure, TimeZoneInfo zone, bool rainPending = false)
    {
        var lines = new List<string> { Stretches(option.Winds) };
        if (Gusts(option.Score) is { } gusts)
        {
            lines.Add(gusts);
        }
        lines.AddRange(RainLines(option, rainFailure, zone, rainPending));
        lines.Add(Urban(option.Urban));
        return lines;
    }

    /// <summary>"ida y vuelta", "sentido horario" or "sentido antihorario".</summary>
    public static string Turn(RideOption option, RouteShape shape) =>
        shape == RouteShape.OutAndBack ? "ida y vuelta" : option.Route.TurnsClockwise() ? "sentido horario" : "sentido antihorario";

    public static string Kind(RouteShape shape) => shape == RouteShape.OutAndBack ? "Ida y vuelta" : "Vuelta";

    /// <summary>One line on a generated loop: distance, climb and what kind of roads; also the GPX description.</summary>
    public static string LoopSummary(LoopCandidate c)
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

    /// <summary>One line on a traced ride: distance, climb, roads and how far it passes from the points given.</summary>
    public static string TracedSummary(TracedRide ride) => TracedSummary(ride.Route, ride.Analysis);

    /// <summary>As <see cref="TracedSummary(TracedRide)"/>, for a route not yet judged for the weather.</summary>
    public static string TracedSummary(TracedRoute traced) => TracedSummary(traced.Route, traced.Analysis);

    private static string TracedSummary(Route route, RouteAnalysis a)
    {
        var parts = new List<string> { Km(route.DistanceKm) };
        if (route.AscentMeters is { } ascent)
        {
            parts.Add($"desnivel {ascent.ToString("0", Spanish)} m");
        }
        parts.Add($"nacionales (trunk) {Km(a.TrunkKm)}");
        if (a.PavedTrackKm >= 0.05)
        {
            parts.Add($"caminos asfaltados {Km(a.PavedTrackKm)}");
        }
        if (a.ForbiddenKm >= 0.05)
        {
            parts.Add($"¡tramos prohibidos (autovía o sin asfalto) {Km(a.ForbiddenKm)}!");
        }
        if (a.MaxWaypointOffsetKm >= 0.5)
        {
            parts.Add($"pasa a {Km(a.MaxWaypointOffsetKm)} de un punto (no hay asfalto más cerca)");
        }
        return string.Join(", ", parts);
    }

    /// <summary>What the generator got for one bearing, for the progress lines.</summary>
    public static string Outcome(LoopResult r) => r switch
    {
        { Found: true, Best: { } best } => $"{Km(best.Route.DistanceKm)} ({r.Requests} peticiones)",
        { EngineFailure: { } failure } => $"BRouter dejó de responder: {failure}",
        { Best: { } best } => $"no válida ({r.Requests} peticiones; la más cercana falla por {Problems(best.Problems)})",
        _ => $"BRouter no pudo unir los puntos en ningún rumbo cercano ({r.Requests} peticiones)",
    };

    private static readonly (LoopProblems Problem, string Name)[] ProblemNames =
    [
        (LoopProblems.Distance, "distancia"),
        (LoopProblems.Spurs, "ramales (forma de estrella)"),
        (LoopProblems.TurnBacks, "medias vueltas"),
        (LoopProblems.Forbidden, "tramos prohibidos"),
        (LoopProblems.Bearing, "se aleja del rumbo"),
    ];

    public static string Problems(LoopProblems problems) =>
        string.Join(", ", ProblemNames.Where(p => problems.HasFlag(p.Problem)).Select(p => p.Name));

    /// <summary>The credit lines the data licences ask for, for the GPX description.</summary>
    public static IEnumerable<string> Attributions(Route route, RainOutlook? rain)
    {
        if (route.Attribution is { } attribution)
        {
            yield return attribution;
        }
        yield return OpenMeteoRainForecastSource.Attribution;
        if (rain is not null)
        {
            yield return OperaRadarSource.AttributionText;
        }
    }
}
