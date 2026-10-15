using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideRankingTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // A ride of 1 km straight legs, one per (bearing, wind speed, wind from, gust).
    private static IReadOnlyList<LegWind> Ride(params (double Bearing, double Speed, double From, double? Gust)[] legs) =>
        legs.Select((l, i) => new LegWind(
                new RouteLeg(i, i, 1, Leon, Leon.Destination(l.Bearing, 1), Leon, l.Bearing, 1, null, Eight, Eight.AddMinutes(2.4)),
                new LocalWind(l.Speed, l.Gust, l.From, "model")))
            .ToList();

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

    // 6 km north, 2 east, 6 south and 2 west back to León: a tall rectangle ridden clockwise on the map.
    private static Route Rectangle()
    {
        var north = Leon.Destination(0, 6);
        var northEast = north.Destination(90, 2);
        return Track(Leon, north, northEast, northEast.Destination(180, 6), Leon);
    }

    [Fact]
    public void Prefers_the_wind_in_the_face_first_and_behind_on_the_way_home()
    {
        // North into a 20 km/h north wind, then back south with it.
        var score = RideRanking.Score(Ride((0, 20, 0, null), (0, 20, 0, null), (180, 20, 0, null), (180, 20, 0, null)));

        Assert.Equal(20, score.FirstHalfHeadwindKmh, precision: 9);
        Assert.Equal(-20, score.SecondHalfHeadwindKmh, precision: 9);
        Assert.Equal(40, score.WindAdvantageKmh, precision: 9);
    }

    [Fact]
    public void Scores_the_other_way_round_negative()
    {
        var score = RideRanking.Score(Ride((180, 20, 0, null), (0, 20, 0, null)));

        Assert.Equal(-40, score.WindAdvantageKmh, precision: 9);
    }

    [Fact]
    public void Marks_the_stretches_with_gusts_across_over_the_limit()
    {
        // Going north with gusts from the east: all across. 45 and 50 km/h over the 40 km/h limit; 40 is not over.
        var score = RideRanking.Score(Ride(
            (0, 20, 90, 45), (0, 20, 90, 50), (0, 20, 90, 40), (0, 20, 90, 30), (0, 20, 270, 42)));

        Assert.Equal([new GustyStretch(0, 2, 50), new GustyStretch(4, 5, 42)], score.GustyStretches);
        Assert.Equal(3, score.GustyKm, precision: 9);
        Assert.True(score.GustsKnown);
    }

    [Fact]
    public void Ignores_gusts_along_the_road()
    {
        // 60 km/h gusts from the north on a ride north: in the face, not across.
        var score = RideRanking.Score(Ride((0, 30, 0, 60)));

        Assert.Empty(score.GustyStretches);
    }

    [Fact]
    public void Takes_the_limit_from_the_rider()
    {
        var winds = Ride((0, 20, 90, 35));

        Assert.Empty(RideRanking.Score(winds).GustyStretches);
        Assert.Single(RideRanking.Score(winds, maxCrossGustKmh: 30).GustyStretches);
    }

    [Fact]
    public void Says_when_the_gusts_are_unknown()
    {
        Assert.False(RideRanking.Score(Ride((0, 20, 90, 30), (0, 20, 90, null))).GustsKnown);
    }

    [Fact]
    public void Puts_a_ride_without_dangerous_gusts_before_a_better_one_for_the_wind()
    {
        var safe = RideRanking.Score(Ride((0, 10, 0, 20), (180, 10, 0, 20)));
        var windy = RideRanking.Score(Ride((0, 30, 0, 20), (180, 30, 45, 70)));
        var calm = RideRanking.Score(Ride((0, 5, 0, 10), (180, 5, 0, 10)));

        Assert.True(windy.WindAdvantageKmh > safe.WindAdvantageKmh);
        Assert.Equal([safe, calm, windy], new[] { windy, calm, safe }.Order());
    }

    [Fact]
    public void Reverses_a_route()
    {
        var route = new Route(
            [new(Leon), new(Leon.Destination(0, 1)), new(Leon.Destination(0, 3))],
            [new RouteSegment(0, 1, new Dictionary<string, string> { ["highway"] = "tertiary" }),
             new RouteSegment(1, 2, new Dictionary<string, string> { ["highway"] = "secondary" })],
            3,
            null,
            "Test");

        var reversed = route.Reversed();

        Assert.Equal(route.Points.Reverse(), reversed.Points);
        Assert.Equal(["secondary", "tertiary"], reversed.Segments.Select(s => s.Highway));
        Assert.Equal([0.0, 2.0], reversed.Segments.Select(s => s.StartKm));
        Assert.Equal(route.DistanceKm, reversed.DistanceKm);
    }

    [Fact]
    public async Task Rates_both_ways_of_a_loop_the_same_in_a_steady_wind()
    {
        // The halfway point is the same both ways, so with the same wind everywhere and all the time, the wind in the
        // face over each half is the same too: only where the route goes matters, not which way round.
        var provider = new FakeWindForecastProvider(
            Enumerable.Range(0, 3).Select(h => new WindSample(Eight.AddHours(h), 20, 30, 0, "fake")).ToList());

        var options = await RideRanking.RankAsync(provider, [Rectangle()], Eight);

        Assert.Equal(2, options.Count);
        Assert.Equal(options[0].Score.WindAdvantageKmh, options[1].Score.WindAdvantageKmh, precision: 3);
        // North first: 6 km in the face out of 8 in each half.
        Assert.Equal(15, options[0].Score.FirstHalfHeadwindKmh, precision: 1);
    }

    [Fact]
    public async Task Picks_the_way_round_that_meets_a_rising_wind_better()
    {
        // A north wind rising from 5 to 45 km/h in the hour. Clockwise rides north first (light wind in the face) and
        // south from km 8 to 14; the other way rides north from km 2 to 8 and south from km 10 to 16, when the wind is
        // stronger: a bit more in the face, and as much more from behind.
        var provider = new FakeWindForecastProvider(
            [new WindSample(Eight, 5, 10, 0, "fake"), new WindSample(Eight.AddHours(1), 45, 50, 0, "fake")]);

        var options = await RideRanking.RankAsync(provider, [Rectangle()], Eight);

        Assert.True(options[0].Reversed);
        Assert.True(options[0].Score.WindAdvantageKmh > options[1].Score.WindAdvantageKmh + 2,
            $"{options[0].Score.WindAdvantageKmh} vs {options[1].Score.WindAdvantageKmh}");
        Assert.Equal(Rectangle().Points[^1], options[0].Route.Points[0]);
    }

    [Fact]
    public async Task Asks_for_the_wind_once_for_every_route_and_both_ways()
    {
        var provider = new FakeWindForecastProvider(
            Enumerable.Range(0, 6).Select(h => new WindSample(Eight.AddHours(h), 20, 30, 225, "fake")).ToList());
        var routes = new[] { Rectangle(), RecordedRides.LaBanezaOutAndBack() };

        var options = await RideRanking.RankAsync(provider, routes, Eight);

        var request = Assert.Single(provider.Requests);
        Assert.Equal(RouteWind.SamplePoints(routes[0]).Count + RouteWind.SamplePoints(routes[1]).Count, request.Count);
        Assert.Equal(4, options.Count);
        Assert.Equal(options.Select(o => o.Score), options.Select(o => o.Score).Order());
        // From the south-west, La Bañeza (out south-west, back north-east) is the one with the wind in the face first.
        Assert.Equal(96, options[0].Winds.Count);
    }

    [Fact]
    public async Task Ranks_nothing_without_routes()
    {
        var provider = new FakeWindForecastProvider([]);

        Assert.Empty(await RideRanking.RankAsync(provider, [], Eight));
        Assert.Empty(provider.Requests);
    }
}
