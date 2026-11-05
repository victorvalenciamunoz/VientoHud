using VientoHud.Core.Rain.Verification;

namespace VientoHud.Core.Tests.Rain;

public class RainScoresTests
{
    private static IEnumerable<RainOutcome> Repeat(double probability, int rained, int dry) =>
        Enumerable.Repeat(new RainOutcome(probability, true), rained)
            .Concat(Enumerable.Repeat(new RainOutcome(probability, false), dry));

    [Fact]
    public void Counts_each_word_and_how_often_it_rained()
    {
        // "no" 10 times (1 rained), "possible" 4 (2 rained), "likely" 6 (5 rained).
        var scores = RainScores.Of(Repeat(0.05, 1, 9).Concat(Repeat(0.3, 2, 2)).Concat(Repeat(0.8, 5, 1)));

        Assert.Equal(20, scores.Cases);
        Assert.Equal(8, scores.Rained);
        Assert.Equal((10, 1), (scores.SaidNo, scores.SaidNoRained));
        Assert.Equal((4, 2), (scores.SaidPossible, scores.SaidPossibleRained));
        Assert.Equal((6, 5), (scores.SaidLikely, scores.SaidLikelyRained));
        Assert.Equal(5.0 / 8, scores.RainAnnounced);
        Assert.Equal(5.0 / 6, scores.LikelyRained);
        Assert.Equal(0.1, scores.NoRained);
    }

    [Fact]
    public void Brier_is_zero_when_perfect_and_a_quarter_when_always_half()
    {
        Assert.Equal(0, RainScores.Of(Repeat(1, 3, 0).Concat(Repeat(0, 0, 3))).Brier);
        Assert.Equal(0.25, RainScores.Of(Repeat(0.5, 3, 3)).Brier, tolerance: 1e-12);
        Assert.Equal(1, RainScores.Of(Repeat(0, 2, 0).Concat(Repeat(1, 0, 2))).Brier);
    }

    [Fact]
    public void Reliability_shows_how_often_it_rained_in_each_band_of_probability()
    {
        var scores = RainScores.Of(Repeat(0.1, 1, 9).Concat(Repeat(0.45, 2, 2)).Concat(Repeat(1, 4, 0)));

        Assert.Equal(5, scores.Reliability.Count);
        Assert.Equal(10, scores.Reliability[0].Cases);
        Assert.Equal(0.1, scores.Reliability[0].MeanProbability, tolerance: 1e-12);
        Assert.Equal(0.1, scores.Reliability[0].RainedShare, tolerance: 1e-12);
        Assert.Equal(0, scores.Reliability[1].Cases);
        Assert.True(double.IsNaN(scores.Reliability[1].RainedShare));
        Assert.Equal((4, 0.5), (scores.Reliability[2].Cases, scores.Reliability[2].RainedShare));
        Assert.Equal((4, 1.0), (scores.Reliability[4].Cases, scores.Reliability[4].RainedShare));
        Assert.Equal(18, scores.Reliability.Sum(b => b.Cases));
    }

    [Fact]
    public void No_cases_gives_no_shares()
    {
        var scores = RainScores.Of([]);

        Assert.Equal(0, scores.Cases);
        Assert.Null(scores.RainAnnounced);
        Assert.Null(scores.LikelyRained);
        Assert.Null(scores.NoRained);
        Assert.True(double.IsNaN(scores.Brier));
    }

    [Fact]
    public void Probabilities_must_be_between_zero_and_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RainOutcome(1.2, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RainOutcome(double.NaN, true));
    }
}
