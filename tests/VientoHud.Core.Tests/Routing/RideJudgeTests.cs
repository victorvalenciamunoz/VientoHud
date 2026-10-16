using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideJudgeTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    // A route through the corners, with a point every 100 m along each side.
    private static Route Track(params Coordinate[] corners)
    {
        var points = new List<RoutePoint> { new(corners[0]) };
        foreach (var (from, to) in corners.Zip(corners.Skip(1)))
        {
            var km = from.DistanceKmTo(to);
            var steps = Math.Max(1, (int)Math.Round(km / 0.1));
            for (var s = 1; s <= steps; s++)
            {
                points.Add(new RoutePoint(s == steps ? to : from.Destination(from.BearingTo(to), km * s / steps)));
            }
        }
        var length = points.Zip(points.Skip(1), (a, b) => a.Position.DistanceKmTo(b.Position)).Sum();
        return new Route(points, [], length, null, "Test");
    }

    // A tall rectangle: 6 km towards bearing, 2 km to the side, 6 km back and 2 km home.
    private static Route Rectangle(double bearing)
    {
        var far = Leon.Destination(bearing, 6);
        var farSide = far.Destination(bearing + 90, 2);
        return Track(Leon, far, farSide, farSide.Destination(bearing + 180, 6), Leon);
    }

    private static IReadOnlyList<WindSample> Steady(double from, string model) =>
        Enumerable.Range(0, 4).Select(h => new WindSample(Eight.AddHours(h), 20, 30, from, model)).ToList();

    private static WindField Observed(double from) => new([new WindFieldPoint(Leon, Steady(from, "METAR TEST"))]);

    // The north and the south rectangles, ranked with a forecast wind from the north: the north one first.
    private static async Task<IReadOnlyList<RideOption>> RankedForNorthWindAsync() =>
        RideRanking.BestWayRound(await RideRanking.RankAsync(
            new FakeWindForecastProvider(Steady(0, "fake")), [Rectangle(0), Rectangle(180)], Eight));

    [Fact]
    public async Task Confirms_the_choice_when_the_wind_blew_as_forecast()
    {
        var ranked = await RankedForNorthWindAsync();

        var verdict = RideJudge.Judge(ranked, null, Observed(0));

        Assert.Equal(0, verdict.Chosen.RouteIndex);
        Assert.True(verdict.ChoseBest);
        Assert.Equal(0, verdict.RegretKmh);
        Assert.Equal(2, verdict.Candidates);
        Assert.Equal(verdict.ForecastScore.WindAdvantageKmh, verdict.ObservedScore.WindAdvantageKmh, precision: 3);
        Assert.Null(verdict.GainOverBlindKmh);
    }

    [Fact]
    public async Task Counts_what_was_lost_when_the_wind_turned_round()
    {
        var ranked = await RankedForNorthWindAsync();

        var verdict = RideJudge.Judge(ranked, null, Observed(180));

        Assert.False(verdict.ChoseBest);
        Assert.True(verdict.ObservedScore.WindAdvantageKmh < 0);
        Assert.Equal(
            verdict.ObservedBest.WindAdvantageKmh - verdict.ObservedScore.WindAdvantageKmh, verdict.RegretKmh, precision: 9);
        Assert.True(verdict.RegretKmh > 20, $"{verdict.RegretKmh}");
    }

    [Fact]
    public async Task Compares_the_choice_with_a_loop_chosen_without_the_wind()
    {
        var ranked = await RankedForNorthWindAsync();
        var blind = RouteLegs.Split(Rectangle(180), Eight);

        var verdict = RideJudge.Judge(ranked, blind, Observed(0));

        Assert.True(verdict.GainOverBlindKmh > 20, $"{verdict.GainOverBlindKmh}");
    }

    [Fact]
    public async Task Rescores_the_same_legs_with_another_wind()
    {
        var ranked = await RankedForNorthWindAsync();
        var legs = ranked[0].Winds.Select(w => w.Leg).ToList();

        var same = RideJudge.Rescore(legs, Observed(0));
        var turned = RideJudge.Rescore(legs, Observed(180));

        Assert.Equal(ranked[0].Score.WindAdvantageKmh, same.WindAdvantageKmh, precision: 3);
        Assert.Equal(-same.WindAdvantageKmh, turned.WindAdvantageKmh, precision: 3);
    }

    [Fact]
    public void Has_nothing_to_judge_without_loops() =>
        Assert.Throws<ArgumentException>(() => RideJudge.Judge([], null, Observed(0)));
}
