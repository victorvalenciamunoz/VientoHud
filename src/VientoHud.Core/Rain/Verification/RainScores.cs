namespace VientoHud.Core.Rain.Verification;

/// <summary>A forecast chance of rain and whether it rained.</summary>
/// <param name="Probability">Forecast probability in [0, 1] (a model without spread gives 0 or 1).</param>
public sealed record RainOutcome(double Probability, bool Rained)
{
    public double Probability { get; } = double.IsFinite(Probability) && Probability is >= 0 and <= 1
        ? Probability
        : throw new ArgumentOutOfRangeException(nameof(Probability), Probability, "Probability must be in [0, 1].");
}

/// <summary>How often it rained among the forecasts with probability in [From, To) (the last bin includes 1).</summary>
public sealed record ReliabilityBin(double From, double To, int Cases, double MeanProbability, double RainedShare);

/// <summary>How a rain forecast did against what happened (DECISIONS 031).</summary>
/// <param name="Cases">Forecasts compared.</param>
/// <param name="Rained">Cases in which it rained.</param>
/// <param name="SaidNo">Forecasts "no" (below 20 %), and how many of them rained.</param>
/// <param name="SaidLikely">Forecasts "likely" (above 50 %), and how many of them rained.</param>
/// <param name="Brier">
/// Mean of (probability − outcome)², outcome 1 if it rained: 0 is perfect, 0.25 is always saying 50 %. One number for
/// how good the percentages are.
/// </param>
public sealed record RainScores(
    int Cases,
    int Rained,
    int SaidNo,
    int SaidNoRained,
    int SaidPossible,
    int SaidPossibleRained,
    int SaidLikely,
    int SaidLikelyRained,
    double Brier,
    IReadOnlyList<ReliabilityBin> Reliability)
{
    /// <summary>Of the times it rained, the share said "likely": how much of the rain it announced.</summary>
    public double? RainAnnounced => Rained == 0 ? null : (double)SaidLikelyRained / Rained;

    /// <summary>When it said "likely", the share in which it rained.</summary>
    public double? LikelyRained => SaidLikely == 0 ? null : (double)SaidLikelyRained / SaidLikely;

    /// <summary>When it said "no", the share in which it rained anyway.</summary>
    public double? NoRained => SaidNo == 0 ? null : (double)SaidNoRained / SaidNo;

    /// <summary>Scores of a set of outcomes. Reliability in bins of 20 %.</summary>
    public static RainScores Of(IEnumerable<RainOutcome> outcomes)
    {
        var list = outcomes.ToList();
        int Count(RainLikelihood likelihood, bool? rained = null) => list.Count(o =>
            RainLikelihoods.Classify(o.Probability) == likelihood && (rained is null || o.Rained == rained));

        var bins = new List<ReliabilityBin>();
        for (var i = 0; i < 5; i++)
        {
            double from = i * 0.2, to = (i + 1) * 0.2;
            var inBin = list.Where(o => o.Probability >= from - 1e-12 && (o.Probability < to - 1e-12 || (i == 4 && o.Probability <= 1)))
                .ToList();
            bins.Add(new ReliabilityBin(
                from, to, inBin.Count,
                inBin.Count == 0 ? double.NaN : inBin.Average(o => o.Probability),
                inBin.Count == 0 ? double.NaN : inBin.Count(o => o.Rained) / (double)inBin.Count));
        }

        return new RainScores(
            list.Count,
            list.Count(o => o.Rained),
            Count(RainLikelihood.No), Count(RainLikelihood.No, rained: true),
            Count(RainLikelihood.Possible), Count(RainLikelihood.Possible, rained: true),
            Count(RainLikelihood.Likely), Count(RainLikelihood.Likely, rained: true),
            list.Count == 0 ? double.NaN : list.Average(o => Math.Pow(o.Probability - (o.Rained ? 1 : 0), 2)),
            bins);
    }
}
