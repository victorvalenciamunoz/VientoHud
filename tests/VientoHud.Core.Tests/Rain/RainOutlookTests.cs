using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class RainOutlookTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    // 201 × 201 cells of 1 km centred on León, at `time`; rain (5 mm/h) where `wet` says, by km east and north of León.
    private static RainGrid Radar(DateTimeOffset time, Func<double, double, bool> wet)
    {
        var (x, y) = Opera.Forward(Leon);
        double left = x - 100_500, top = y + 100_500;
        var rates = new float[201 * 201];
        for (var r = 0; r < 201; r++)
        {
            for (var c = 0; c < 201; c++)
            {
                rates[r * 201 + c] = wet(c - 100, 100 - r) ? 5 : 0;
            }
        }
        return new RainGrid(time, Opera, left, top, 1000, 201, 201, rates);
    }

    private static readonly RainGrid AllWet = Radar(Now, (_, _) => true);
    private static readonly RainGrid AllDry = Radar(Now, (_, _) => false);

    // A model field on a 5 km grid around León with rain where and when `wet` says.
    private static ModelRainField Model(Func<Coordinate, DateTimeOffset, bool> wet, double radiusKm = 30) =>
        new(ModelRainField.GridAround(Leon, radiusKm, 5).Select(p => new RainForecastSeries(p, p, "fake", Quarter,
            Enumerable.Range(1, 6 * 4).Select(i => Now + i * Quarter)
                .Select(end => new RainForecastSample(end, wet(p, end) ? 2 : 0)).ToList())).ToList());

    private static RainProbabilitySeries Fallback(double chance) => new(Leon, Leon, "fake", TimeSpan.FromHours(1),
        Enumerable.Range(1, 8).Select(i => new RainProbabilitySample(Now.AddHours(i), chance)).ToList());

    [Fact]
    public void Up_to_two_hours_the_radar_decides()
    {
        var outlook = new RainOutlook(Now, AllWet, null, Model((_, _) => false), Fallback(0));

        var rain = outlook.At(Leon, Now.AddMinutes(90));

        Assert.Equal(RainSourceKind.Radar, rain.Source);
        Assert.Equal(1, rain.Probability);
    }

    [Fact]
    public void From_two_to_three_hours_radar_and_model_mix()
    {
        var outlook = new RainOutlook(Now, AllWet, null, Model((_, _) => false), Fallback(0));

        var halfway = outlook.At(Leon, Now.AddMinutes(150));
        var end = outlook.At(Leon, Now.AddHours(3));

        Assert.Equal(RainSourceKind.Blend, halfway.Source);
        Assert.Equal(0.5, halfway.Probability!.Value, 9);
        Assert.Equal(0, end.Probability!.Value, 9);
    }

    [Fact]
    public void Beyond_three_hours_nothing_is_counted_place_by_place()
    {
        var outlook = new RainOutlook(Now, AllWet, null, Model((_, _) => true), Fallback(1));

        var rain = outlook.At(Leon, Now.AddHours(3).AddMinutes(1));

        Assert.Equal(RainSourceKind.BeyondHorizon, rain.Source);
        Assert.Null(rain.Probability);
        Assert.Equal(Now.AddHours(3), outlook.CountedUntil);
    }

    [Fact]
    public void Without_radar_the_model_and_without_model_the_fallback()
    {
        var noRadar = new RainOutlook(Now, null, null, Model((_, _) => true), Fallback(0));
        var noModel = new RainOutlook(Now, AllDry, null, null, Fallback(0.6));
        var nothing = new RainOutlook(Now, null, null, null, null);

        Assert.Equal(new PlaceRain(1, RainSourceKind.Model), noRadar.At(Leon, Now.AddMinutes(30)));
        // In the mix, the fallback takes the model's place: 2.5 h → half radar (0), half fallback (0.6).
        Assert.Equal(0.3, noModel.At(Leon, Now.AddMinutes(150)).Probability!.Value, 9);
        Assert.Equal(new PlaceRain(null, RainSourceKind.Unknown), nothing.At(Leon, Now.AddMinutes(30)));
    }

    [Fact]
    public void The_radar_moves_the_rain_towards_the_place()
    {
        // Rain from 40 km west of León and beyond, moving east at 40 km/h: dry now, wet in 1 h 30.
        var radar = Radar(Now, (east, _) => east < -40);
        var motion = new RainMotion(Now.AddMinutes(-30), Now, 40, 0, 0.8, 0.4);
        var outlook = new RainOutlook(Now, radar, motion, null, null);

        Assert.Equal(RainLikelihood.No, outlook.At(Leon, Now).Likelihood);
        Assert.Equal(RainLikelihood.Likely, outlook.At(Leon, Now.AddMinutes(90)).Likelihood);
    }

    [Fact]
    public void Warns_for_the_area_beyond_three_hours()
    {
        // Showers somewhere in the area from 3 h 30 on.
        var model = Model((p, t) => t > Now.AddMinutes(210) && p.Latitude > Leon.Latitude + 0.1);
        var outlook = new RainOutlook(Now, AllDry, null, model, Fallback(0));

        var warning = outlook.AreaWarning(Now.AddHours(3), Now.AddHours(5));
        var dry = new RainOutlook(Now, AllDry, null, Model((_, _) => false), Fallback(0)).AreaWarning(Now.AddHours(3), Now.AddHours(5));

        Assert.NotNull(warning);
        Assert.Equal(Now.AddMinutes(225), warning.From);
        Assert.Equal(RainSourceKind.Model, warning.Source);
        Assert.InRange(warning.MaxProbability, 0.2, 0.5);
        Assert.Null(dry);
    }

    [Fact]
    public void After_the_model_the_warning_comes_from_the_fallback()
    {
        // The model ends at 6 h; the fallback says 40 % at 7 h.
        var outlook = new RainOutlook(Now, AllDry, null, Model((_, _) => false), Fallback(0.4));

        var warning = outlook.AreaWarning(Now.AddHours(6).AddMinutes(15), Now.AddHours(7));

        Assert.Equal(RainSourceKind.Fallback, warning!.Source);
        Assert.Equal(0.4, warning.MaxProbability);
    }

    [Fact]
    public async Task Asks_each_source_once_and_the_model_only_beyond_two_hours()
    {
        var latest = Radar(Now.AddMinutes(-5), (east, _) => east > 10);
        RainGrid[] frames = [Radar(Now.AddMinutes(-35), (east, _) => east > 4), Radar(Now.AddMinutes(-25), (east, _) => east > 6),
            Radar(Now.AddMinutes(-15), (east, _) => east > 8), latest];
        var radar = new FakeRadarSource(TimeSpan.FromMinutes(5), frames);
        var model = new FakeRainForecastSource((_, _) => 0);

        var shortRide = await RainOutlook.ForecastAsync(radar, model, model, Leon, 20, Now, Now.AddMinutes(100));
        var longRide = await RainOutlook.ForecastAsync(radar, model, model, Leon, 20, Now, Now.AddHours(4));

        Assert.Equal(latest.Time, shortRide.Radar!.Time);
        // 2 cells (km) east every 10 min: 12 km/h towards the east.
        Assert.Equal(12, shortRide.Motion!.EastKmh, tolerance: 1);
        Assert.Null(shortRide.Model);
        Assert.Single(model.ModelRequests);
        Assert.Equal(1, model.ProbabilityRequests);
        Assert.NotNull(longRide.Model);
        Assert.NotNull(longRide.Fallback);
        // Reach 20 km + area 15 km, every 5 km: 15 × 15 points.
        Assert.Equal(225, model.ModelRequests[0].Count);
    }

    [Fact]
    public async Task Far_rides_ask_for_at_most_225_points_and_a_place_outside_the_model_uses_the_fallback()
    {
        var radar = new FakeRadarSource(TimeSpan.FromMinutes(5), AllDry);
        var outside = new FakeRainForecastSource((_, _) => 0, _ => 0.7) { ModelFails = true };

        var outlook = await RainOutlook.ForecastAsync(radar, outside, outside, Leon, 80, Now, Now.AddHours(4));

        Assert.True(outside.ModelRequests[0].Count <= 225);
        Assert.Null(outlook.Model);
        Assert.Equal(0.7, outlook.Fallback!.ProbabilityAt(Now.AddHours(2))!.Value);
        Assert.Equal(RainSourceKind.Blend, outlook.At(Leon, Now.AddMinutes(150)).Source);
    }

    [Fact]
    public async Task Rain_along_a_real_route_leg_by_leg_and_a_warning_after_three_hours()
    {
        // León north loop (17 km), rain over the north of the city now and still; the ride starts 2 h 50 from now,
        // so it crosses the 3 h line: the first legs are counted, the rest only warned about.
        var route = await RecordedRoutes.LeonNorth.LoadAsync();
        var radar = Radar(Now, (_, north) => north > 1.5);
        var model = Model((p, _) => p.Latitude > Leon.Latitude + 0.013);
        var outlook = new RainOutlook(Now, radar, null, model, Fallback(0));
        var legs = RouteLegs.Split(route, Now.AddMinutes(170), averageSpeedKmh: 20);

        var rain = RouteRain.Along(legs, outlook);

        Assert.Equal(legs.Count, rain.Legs.Count);
        Assert.Contains(rain.Legs, l => l.Rain.Source == RainSourceKind.Blend);
        Assert.Contains(rain.Legs, l => l.Rain.Source == RainSourceKind.BeyondHorizon);
        Assert.True(rain.UncountedKm > 0);
        Assert.NotNull(rain.AreaWarning);
        Assert.Equal(rain.LikelyKm + rain.PossibleKm + rain.UncountedKm,
            rain.Legs.Where(l => l.Rain.Likelihood is not RainLikelihood.No).Sum(l => l.Leg.LengthKm), 9);
    }

    [Fact]
    public void A_dry_ride_has_no_rain_and_no_warning()
    {
        var outlook = new RainOutlook(Now, AllDry, null, Model((_, _) => false), Fallback(0));
        var start = Leon;
        var legs = new[]
        {
            new RouteLeg(0, 0, 1, start, start.Destination(0, 1), start.Destination(0, 0.5), 0, 1, null, Now, Now.AddMinutes(3)),
        };

        var rain = RouteRain.Along(legs, outlook);

        Assert.Equal(0, rain.LikelyKm);
        Assert.Null(rain.FirstRain);
        Assert.Null(rain.AreaWarning);
    }
}
