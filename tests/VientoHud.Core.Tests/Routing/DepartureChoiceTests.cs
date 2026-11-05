using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public partial class DepartureChoiceTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    // 6 km north, 2 east, 6 south and 2 west back to the origin (16 km).
    private static Route Rectangle()
    {
        var corners = new[]
        {
            Origin, Origin.Destination(0, 6), Origin.Destination(0, 6).Destination(90, 2), Origin.Destination(90, 2), Origin,
        };
        var points = new List<RoutePoint> { new(corners[0]) };
        foreach (var (from, to) in corners.Zip(corners.Skip(1)))
        {
            var km = from.DistanceKmTo(to);
            var steps = (int)Math.Round(km / 0.1);
            for (var s = 1; s <= steps; s++)
            {
                points.Add(new RoutePoint(s == steps ? to : from.Destination(from.BearingTo(to), km * s / steps)));
            }
        }
        return new Route(points, [], 16, null, "Test");
    }

    // The same wind every hour, or one per hour from `Now`.
    private static FakeWindForecastProvider Wind(params (double Speed, double From, double? Gust)[] hours) =>
        new(Enumerable.Range(0, 10)
            .Select(h => hours[Math.Min(h, hours.Length - 1)])
            .Select((w, h) => new WindSample(Now.AddHours(h), w.Speed, w.Gust, w.From, "fake"))
            .ToList());

    // A radar frame of 201 × 201 km around `center` with rain where `wet` says (km east and north of the centre).
    private static RainGrid Radar(Coordinate center, Func<double, double, bool> wet)
    {
        var (x, y) = Opera.Forward(center);
        var rates = new float[201 * 201];
        for (var r = 0; r < 201; r++)
        {
            for (var c = 0; c < 201; c++)
            {
                rates[r * 201 + c] = wet(c - 100, 100 - r) ? 5 : 0;
            }
        }
        return new RainGrid(Now, Opera, x - 100_500, y + 100_500, 1000, 201, 201, rates);
    }

    [Fact]
    public void Departures_every_quarter_of_an_hour_for_two_hours()
    {
        var times = DepartureChoice.Times(Now);

        Assert.Equal(9, times.Count);
        Assert.Equal(Now, times[0]);
        Assert.Equal(Now.AddHours(2), times[^1]);
        Assert.Single(DepartureChoice.Times(Now, TimeSpan.Zero));
    }

    [Fact]
    public void Later_departures_fall_on_whole_quarters_of_an_hour()
    {
        var times = DepartureChoice.Times(Now.AddMinutes(14));

        Assert.Equal(Now.AddMinutes(14), times[0]);
        // 9:15 is a minute after 9:14: skipped.
        Assert.Equal(Now.AddMinutes(30), times[1]);
        Assert.Equal(Now.AddMinutes(45), times[2]);
        Assert.Equal(Now.AddHours(2), times[^1]);
        Assert.Equal(8, times.Count);
        Assert.Equal(Now.AddMinutes(15), DepartureChoice.Times(Now.AddMinutes(8))[1]);
    }

    [Fact]
    public async Task Waits_for_the_rain_overhead_to_move_away()
    {
        // A band of rain 30 km wide over the origin now, moving east at 30 km/h: its back edge passes the origin in ~30 min.
        var radar = Radar(Origin, (east, _) => east is > -15 and < 15);
        var motion = new RainMotion(Now.AddMinutes(-30), Now, 30, 0, 0.8, 0.4);
        var outlook = new RainOutlook(Now, radar, motion, null, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((10, 270, null)), [Rectangle()], DepartureChoice.Times(Now), outlook, averageSpeedKmh: 20);

        Assert.True(advice.Wait);
        Assert.True(advice.RainKmSaved >= RideRanking.RainTieKm);
        Assert.True(advice.First.Best!.LikelyRainKm >= 5);
        Assert.Equal(0, advice.Chosen.Best!.LikelyRainKm);
        Assert.InRange((advice.Chosen.Departure - Now).TotalMinutes, 15, 75);
        Assert.Equal(9, advice.Options.Count);
    }

    [Fact]
    public async Task Does_not_wait_for_a_better_wind_alone()
    {
        // Dry; the wind turns to the north later, which suits the rectangle (north first) better than now.
        var dry = new RainOutlook(Now, Radar(Origin, (_, _) => false), null, null, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((20, 180, null), (20, 0, null)), [Rectangle()], DepartureChoice.Times(Now), dry, averageSpeedKmh: 20);

        Assert.False(advice.Wait);
        Assert.Equal(Now, advice.Chosen.Departure);
        Assert.True(advice.Options[^1].Best!.Score.WindAdvantageKmh > advice.First.Best!.Score.WindAdvantageKmh);
    }

    [Fact]
    public async Task Does_not_wait_for_rain_that_is_coming()
    {
        // Rain 30 km west moving east at 30 km/h: leaving now beats it.
        var radar = Radar(Origin, (east, _) => east < -30);
        var outlook = new RainOutlook(Now, radar, new RainMotion(Now, Now, 30, 0, 0.8, 0.4), null, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((10, 270, null)), [Rectangle()], DepartureChoice.Times(Now), outlook, averageSpeedKmh: 20);

        Assert.False(advice.Wait);
        Assert.Equal(0, advice.Chosen.Best!.LikelyRainKm);
    }

    [Fact]
    public async Task Waits_to_avoid_dangerous_gusts()
    {
        // 60 km/h gusts from the east in the first two hours (across the north-south sides), calm after.
        var dry = new RainOutlook(Now, Radar(Origin, (_, _) => false), null, null, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((25, 90, 60), (25, 90, 60), (5, 90, 8)), [Rectangle()], DepartureChoice.Times(Now), dry, averageSpeedKmh: 20);

        Assert.True(advice.First.Best!.Score.GustyKm > 0);
        Assert.True(advice.Wait);
        Assert.Equal(0, advice.Chosen.Best!.Score.GustyKm);
        Assert.Equal(advice.First.Best.Score.GustyKm, advice.GustyKmSaved, 9);
    }

    [Fact]
    public async Task Without_loops_there_is_nothing_to_choose()
    {
        var provider = Wind((10, 0, null));

        var advice = await DepartureChoice.ChooseAsync(provider, [], DepartureChoice.Times(Now), null);

        Assert.False(advice.Wait);
        Assert.Null(advice.Chosen.Best);
        Assert.Empty(provider.Requests);
        await Assert.ThrowsAsync<ArgumentException>(() => DepartureChoice.ChooseAsync(provider, [Rectangle()], [], null));
        Assert.Throws<ArgumentException>(() =>
            DepartureChoice.Choose(null!, [Rectangle()], [Now.AddHours(1), Now], null));
    }
}
