using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class DepartureChoiceTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    // 6 km north, 2 east, 6 south and 2 west back to León (16 km).
    private static Route Rectangle()
    {
        var corners = new[]
        {
            Leon, Leon.Destination(0, 6), Leon.Destination(0, 6).Destination(90, 2), Leon.Destination(90, 2), Leon,
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
    public async Task Waits_for_the_rain_overhead_to_move_away()
    {
        // A band of rain 30 km wide over León now, moving east at 30 km/h: its back edge passes León in ~30 min.
        var radar = Radar(Leon, (east, _) => east is > -15 and < 15);
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
        var dry = new RainOutlook(Now, Radar(Leon, (_, _) => false), null, null, null);

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
        var radar = Radar(Leon, (east, _) => east < -30);
        var outlook = new RainOutlook(Now, radar, new RainMotion(Now, Now, 30, 0, 0.8, 0.4), null, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((10, 270, null)), [Rectangle()], DepartureChoice.Times(Now), outlook, averageSpeedKmh: 20);

        Assert.False(advice.Wait);
        Assert.Equal(0, advice.Chosen.Best!.LikelyRainKm);
    }

    [Fact]
    public async Task A_later_departure_does_not_look_drier_just_because_less_is_known()
    {
        // La Bañeza out and back (96 km, ~4 h): showers only far from home (more than 30 km out) from 2 to 3 h from
        // now. Leaving now meets them on the far part; leaving 2 h later reaches the far part beyond the 3 h counted, so
        // its rain there is not known: it must not win for that.
        var ride = RecordedRides.LaBanezaOutAndBack();
        var home = ride.Points[0].Position;
        var quarter = TimeSpan.FromMinutes(15);
        var model = new ModelRainField(ModelRainField.GridAround(home, 60, 5)
            .Select(p => new RainForecastSeries(p, p, "fake", quarter, Enumerable.Range(1, 24)
                .Select(i => Now + i * quarter)
                .Select(end => new RainForecastSample(end,
                    home.DistanceKmTo(p) > 30 && end > Now.AddHours(2) && end <= Now.AddHours(3) ? 3 : 0))
                .ToList()))
            .ToList());
        var outlook = new RainOutlook(Now, null, null, model, null);

        var advice = await DepartureChoice.ChooseAsync(
            Wind((10, 270, null)), [ride], [Now, Now.AddHours(2)], outlook);

        var later = advice.Options[1].Best!;
        Assert.True(advice.First.Best!.LikelyRainKm >= 5);
        Assert.True(later.LikelyRainKm < advice.First.Best.LikelyRainKm - 5);
        Assert.True(later.Rain!.UncountedKm > 50);
        Assert.False(advice.Wait);
    }

    [Fact]
    public async Task Waits_to_avoid_dangerous_gusts()
    {
        // 60 km/h gusts from the east in the first two hours (across the north-south sides), calm after.
        var dry = new RainOutlook(Now, Radar(Leon, (_, _) => false), null, null, null);

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
