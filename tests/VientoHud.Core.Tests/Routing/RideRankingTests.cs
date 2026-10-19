using VientoHud.Core.Rain;
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
    public void Tells_which_way_round_a_route_turns()
    {
        Assert.True(Rectangle().TurnsClockwise());
        Assert.False(Rectangle().Reversed().TurnsClockwise());
    }

    [Fact]
    public void Keeps_each_route_once_in_its_better_way_round()
    {
        var score = RideRanking.Score(Ride((0, 10, 0, null)));
        RideOption Option(int index, bool reversed) => new(index, Rectangle(), reversed, [], score);
        var ranked = new[] { Option(1, true), Option(0, false), Option(1, false), Option(0, true) };

        var best = RideRanking.BestWayRound(ranked);

        Assert.Equal([(1, true), (0, false)], best.Select(o => (o.RouteIndex, o.Reversed)));
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

    // An option with the wind advantage, km of dangerous gusts across and km of likely rain given.
    private static RideOption Option(int index, double advantage, double gustyKm = 0, int likelyRainKm = 0, double urbanKm = 0)
    {
        var legs = Enumerable.Range(0, Math.Max(1, likelyRainKm))
            .Select(i => new RouteLeg(i, i, 1, Leon, Leon, Leon, 0, 1, null, Eight, Eight.AddMinutes(2.4)))
            .Select(l => new LegRain(l, new PlaceRain(likelyRainKm == 0 ? 0 : 0.9, RainSourceKind.Radar)))
            .ToList();
        GustyStretch[] gusts = gustyKm > 0 ? [new GustyStretch(0, gustyKm, 50)] : [];
        // The rectangle (16 km) with its first `urbanKm` on residential streets.
        var rectangle = Rectangle();
        RouteSegment[] segments = urbanKm > 0
            ?
            [
                new RouteSegment(0, urbanKm, new Dictionary<string, string> { ["highway"] = "residential" }),
                new RouteSegment(urbanKm, rectangle.DistanceKm - urbanKm, new Dictionary<string, string> { ["highway"] = "tertiary" }),
            ]
            : [new RouteSegment(0, rectangle.DistanceKm, new Dictionary<string, string> { ["highway"] = "tertiary" })];
        return new RideOption(index, rectangle with { Segments = segments }, false, [],
            new RideScore(advantage, 0, gusts, true), new RideRain(legs, null));
    }

    [Fact]
    public void Less_town_comes_before_a_better_wind()
    {
        var urban = Option(1, advantage: 20, urbanKm: 12);
        var rural = Option(2, advantage: 5);

        Assert.Equal(12, urban.Urban.UrbanKm);
        Assert.Equal([rural, urban], RideRanking.Order([urban, rural]));
    }

    [Fact]
    public void Less_than_ten_km_of_town_apart_is_a_tie_and_the_wind_decides()
    {
        var urban = Option(1, advantage: 20, urbanKm: 8);
        var rural = Option(2, advantage: 5);

        Assert.Equal([urban, rural], RideRanking.Order([rural, urban]));
    }

    [Fact]
    public void Rain_comes_before_town()
    {
        var dryInTown = Option(1, advantage: 0, urbanKm: 14);
        var wetInTheCountry = Option(2, advantage: 0, likelyRainKm: 8);

        Assert.Equal([dryInTown, wetInTheCountry], RideRanking.Order([wetInTheCountry, dryInTown]));
    }

    [Fact]
    public void Rain_comes_before_the_wind_and_gusts_before_the_rain()
    {
        var windy = Option(1, advantage: 20, likelyRainKm: 12);
        var dry = Option(2, advantage: 5);
        var gusty = Option(3, advantage: 30, gustyKm: 2);

        var ordered = RideRanking.Order([windy, gusty, dry]);

        Assert.Equal([dry, windy, gusty], ordered);
    }

    [Fact]
    public void Less_than_five_km_of_rain_apart_is_a_tie_and_the_wind_decides()
    {
        var wetterButWindier = Option(1, advantage: 15, likelyRainKm: 4);
        var drier = Option(2, advantage: 5, likelyRainKm: 0);

        Assert.Equal([wetterButWindier, drier], RideRanking.Order([drier, wetterButWindier]));
    }

    [Fact]
    public void Ties_in_rain_are_groups_from_the_driest_not_pairs()
    {
        // 0, 4 and 8 km: 0 and 4 tie, 8 does not tie with 0, so it goes after both, even with the best wind.
        var zero = Option(1, advantage: 1, likelyRainKm: 0);
        var four = Option(2, advantage: 10, likelyRainKm: 4);
        var eight = Option(3, advantage: 30, likelyRainKm: 8);

        Assert.Equal([four, zero, eight], RideRanking.Order([eight, zero, four]));
    }

    [Fact]
    public void Without_rain_the_order_is_the_wind_one()
    {
        var options = new[] { Option(1, 3), Option(2, 9, gustyKm: 1), Option(3, 7) }
            .Select(o => o with { Rain = null }).ToList();

        Assert.Equal(options.Select(o => o.Score).Order(), RideRanking.Order(options).Select(o => o.Score));
    }

    [Fact]
    public async Task Ranks_with_the_rain_on_each_leg()
    {
        // Wind from the north favours the rectangle that goes north first, but it rains north of León: the one going
        // south comes first.
        var provider = new FakeWindForecastProvider(
            Enumerable.Range(0, 3).Select(h => new WindSample(Eight.AddHours(h), 20, null, 0, "fake")).ToList());
        var north = Rectangle();
        var south = Track(Leon, Leon.Destination(180, 6), Leon.Destination(180, 6).Destination(90, 2), Leon.Destination(90, 2), Leon);
        var outlook = new RainOutlook(Eight, RainNorthOfLeon(), null, null, null);

        var dryRanking = await RideRanking.RankAsync(provider, [north, south], Eight);
        var wetRanking = await RideRanking.RankAsync(provider, [north, south], Eight, rain: outlook);

        Assert.Equal(0, dryRanking[0].RouteIndex);
        Assert.Equal(1, wetRanking[0].RouteIndex);
        Assert.True(wetRanking.First(o => o.RouteIndex == 0).LikelyRainKm >= 5);
        Assert.Equal(0, wetRanking[0].LikelyRainKm);
    }

    // A radar frame of 41 × 41 km around León with rain (5 mm/h) more than 2 km north of it.
    private static RainGrid RainNorthOfLeon()
    {
        var opera = LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");
        var (x, y) = opera.Forward(Leon);
        var rates = new float[41 * 41];
        for (var r = 0; r < 41; r++)
        {
            for (var c = 0; c < 41; c++)
            {
                rates[r * 41 + c] = 20 - r > 2 ? 5 : 0;
            }
        }
        return new RainGrid(Eight, opera, x - 20_500, y + 20_500, 1000, 41, 41, rates);
    }

    [Fact]
    public async Task Ranks_nothing_without_routes()
    {
        var provider = new FakeWindForecastProvider([]);

        Assert.Empty(await RideRanking.RankAsync(provider, [], Eight));
        Assert.Empty(provider.Requests);
    }
}
