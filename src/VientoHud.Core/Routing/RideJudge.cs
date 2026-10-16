using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>How a day's choice held up against the wind that really blew (judge of phase 3, DECISIONS 024).</summary>
/// <param name="Chosen">The loop the forecast put first, as it would have been ridden.</param>
/// <param name="ForecastScore">Its score with the forecast.</param>
/// <param name="ObservedScore">Its score with the observed wind.</param>
/// <param name="ObservedBest">The best score with the observed wind among all the loops the forecast ranked.</param>
/// <param name="ChoseBest">Whether the chosen loop was also the best with the observed wind (ties count).</param>
/// <param name="Candidates">How many loops there were to choose from.</param>
/// <param name="BlindScore">
/// Score with the observed wind of the loop chosen without looking at the wind (phase 2: bearings every 120° from north,
/// the generator's best); null when that gave no valid loop.
/// </param>
public sealed record RideVerdict(
    RideOption Chosen,
    RideScore ForecastScore,
    RideScore ObservedScore,
    RideScore ObservedBest,
    bool ChoseBest,
    int Candidates,
    RideScore? BlindScore)
{
    /// <summary>Wind advantage lost against the best loop, with the observed wind (0 when the best was chosen).</summary>
    public double RegretKmh => Math.Max(0, ObservedBest.WindAdvantageKmh - ObservedScore.WindAdvantageKmh);

    /// <summary>Wind advantage gained against not looking at the wind, with the observed wind.</summary>
    public double? GainOverBlindKmh => BlindScore is null ? null : ObservedScore.WindAdvantageKmh - BlindScore.WindAdvantageKmh;
}

/// <summary>Scores past choices again with the wind that really blew (judge of phase 3).</summary>
public static class RideJudge
{
    /// <summary>The score of a ride over the same legs (same times) with another wind, e.g. the observed one.</summary>
    public static RideScore Rescore(
        IReadOnlyList<RouteLeg> legs,
        WindField wind,
        double maxCrossGustKmh = RideRanking.DefaultMaxCrossGustKmh) =>
        RideRanking.Score(RouteWind.Along(legs, wind), maxCrossGustKmh);

    /// <summary>
    /// Judges a day: <paramref name="ranked"/> are the loops as the forecast ranked them, best first;
    /// <paramref name="blind"/> the loop chosen without the wind, if any, timed like them.
    /// </summary>
    public static RideVerdict Judge(
        IReadOnlyList<RideOption> ranked,
        IReadOnlyList<RouteLeg>? blind,
        WindField observed,
        double maxCrossGustKmh = RideRanking.DefaultMaxCrossGustKmh)
    {
        if (ranked.Count == 0)
        {
            throw new ArgumentException("There is no loop to judge.", nameof(ranked));
        }

        var observedScores = ranked
            .Select(o => Rescore(o.Winds.Select(w => w.Leg).ToList(), observed, maxCrossGustKmh))
            .ToList();
        var best = observedScores.Min()!;
        return new RideVerdict(
            ranked[0],
            ranked[0].Score,
            observedScores[0],
            best,
            observedScores[0].CompareTo(best) <= 0,
            ranked.Count,
            blind is null ? null : Rescore(blind, observed, maxCrossGustKmh));
    }
}
