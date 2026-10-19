using VientoHud.Core.Rain;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideAdvisorTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    // The same wind for the next 12 hours.
    private static FakeWindForecastProvider Wind(double speed, double from) =>
        new(Enumerable.Range(0, 12).Select(h => new WindSample(Now.AddHours(h), speed, speed + 10, from, "fake")).ToList());

    // A radar frame of 201 × 201 km around León at `time`, dry.
    private static RainGrid DryRadar(DateTimeOffset time)
    {
        var (x, y) = Opera.Forward(Leon);
        return new RainGrid(time, Opera, x - 100_500, y + 100_500, 1000, 201, 201, new float[201 * 201]);
    }

    private static RideAdvisor Advisor(
        IRouteEngine roads, FakeWindForecastProvider wind, IRadarSource radar, FakeRainForecastSource model) =>
        new(new RidePlanner(new LoopGenerator(roads), wind), wind, new RainOutlookSource(radar, model, model));

    // Every fan of waypoints makes a valid route of exactly the target length.
    private static FakeRouteEngine Roads() => new(w => FakeRouteEngine.Straight(w, roadFactor: 1.5));

    [Fact]
    public async Task Plans_the_routes_asks_for_the_rain_once_and_ranks_them_at_every_departure()
    {
        var radar = new FakeRadarSource(TimeSpan.FromMinutes(5), DryRadar(Now.AddMinutes(-5)));
        var model = new FakeRainForecastSource((_, _) => 0);
        var reports = new List<LoopResult>();

        var ride = await Advisor(Roads(), Wind(15, 194), radar, model).AdviseAsync(
            new RideRequest(Leon, 60, Now), Now, new SynchronousProgress<LoopResult>(reports.Add));

        // A loop and an out and back for each of the three bearings into the wind (DECISIONS 041), each reported.
        Assert.Equal(6, ride.Plan.Loops.Count);
        Assert.Equal(6, reports.Count);
        Assert.Equal(6, ride.FoundLoops.Count);
        // Leave now or every quarter of an hour up to 2 h later (DECISIONS 036); dry and the same wind: leave now.
        Assert.Equal(9, ride.Departure.Options.Count);
        Assert.False(ride.Departure.Wait);
        Assert.Equal(6, ride.Departure.Chosen.Ranked.Count);
        // Each ranked option points back to the loop it came from (ridden the other way round, a reversed copy).
        Assert.All(ride.Departure.Chosen.Ranked, o =>
        {
            var route = ride.LoopOf(o).Best!.Route;
            if (o.Reversed)
            {
                Assert.Equal(route.Points[^2].Position, o.Route.Points[1].Position);
            }
            else
            {
                Assert.Same(route, o.Route);
            }
        });
        Assert.Equal((194.0, RouteShape.OutAndBack),
            (ride.LoopOf(ride.Departure.Chosen.Best!).BearingDegrees, ride.LoopOf(ride.Departure.Chosen.Best!).Shape));
        // The rain, asked once: 60 km at 25 km/h and up to 2 h later goes beyond the radar, so AROME too.
        Assert.NotNull(ride.Rain);
        Assert.Null(ride.RainFailure);
        Assert.Single(model.ModelRequests);
        Assert.Equal(1, model.ProbabilityRequests);
        Assert.Equal(Now, ride.Rain!.Now);
    }

    [Fact]
    public async Task Without_the_rain_still_ranks_the_routes_and_says_why()
    {
        var model = new FakeRainForecastSource((_, _) => 0);

        var ride = await Advisor(Roads(), Wind(15, 194), new FailingRadarSource(), model)
            .AdviseAsync(new RideRequest(Leon, 60, Now), Now);

        Assert.Null(ride.Rain);
        Assert.Equal("Radar down", ride.RainFailure);
        Assert.Equal(6, ride.Departure.Chosen.Ranked.Count);
    }

    [Fact]
    public async Task Asks_for_no_rain_when_no_route_is_valid()
    {
        // Every route runs on a motorway: forbidden.
        var roads = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5, highway: "motorway"));
        var radar = new FakeRadarSource(TimeSpan.FromMinutes(5), DryRadar(Now.AddMinutes(-5)));
        var model = new FakeRainForecastSource((_, _) => 0);

        var ride = await Advisor(roads, Wind(15, 194), radar, model).AdviseAsync(new RideRequest(Leon, 60, Now, Count: 1), Now);

        Assert.Empty(ride.FoundLoops);
        Assert.Null(ride.Departure.Chosen.Best);
        Assert.Null(ride.Rain);
        Assert.Null(ride.RainFailure);
        Assert.Empty(radar.Requested);
        Assert.Empty(model.ModelRequests);
    }

    [Fact]
    public async Task For_a_later_departure_the_rain_is_forecast_from_now()
    {
        var radar = new FakeRadarSource(TimeSpan.FromMinutes(5), DryRadar(Now.AddMinutes(-5)));
        var model = new FakeRainForecastSource((_, _) => 0);

        var ride = await Advisor(Roads(), Wind(15, 194), radar, model)
            .AdviseAsync(new RideRequest(Leon, 60, Now.AddHours(3)), Now);

        Assert.Equal(Now, ride.Rain!.Now);
        Assert.Equal(Now.AddHours(3), ride.Departure.First.Departure);
    }

    [Fact]
    public async Task Now_must_be_in_UTC()
    {
        var model = new FakeRainForecastSource((_, _) => 0);
        var advisor = Advisor(Roads(), Wind(15, 194), new FailingRadarSource(), model);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            advisor.AdviseAsync(new RideRequest(Leon, 60, Now), Now.ToOffset(TimeSpan.FromHours(2))));
    }
}
