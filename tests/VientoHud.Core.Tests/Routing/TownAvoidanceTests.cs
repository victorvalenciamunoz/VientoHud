using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Towns;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class TownAvoidanceTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private static TownMap Towns(params (Coordinate Centre, double RadiusKm)[] towns) =>
        TownMap.From(FakeBuiltUpSource.Grid(Leon, 30, towns), new TownMapOptions { JoinCells = 0, MinAreaKm2 = 0 });

    [Fact]
    public void A_waypoint_in_a_town_moves_just_out_of_it_keeping_its_distance_from_home()
    {
        var waypoint = Leon.Destination(90, 10);
        var towns = Towns((waypoint, 1));
        IReadOnlyList<Coordinate> waypoints = [Leon, Leon.Destination(0, 10), waypoint, Leon];

        var moved = TownAvoidance.MoveOut(waypoints, towns);

        Assert.Equal(waypoints[0], moved[0]);
        Assert.Equal(waypoints[1], moved[1]);
        Assert.Equal(waypoints[3], moved[3]);
        Assert.False(towns.Contains(moved[2]));
        // Out of a town of 1 km radius with 300 m to spare (cells of ~200 m), on the first ring that is clear, to one side.
        Assert.InRange(waypoint.DistanceKmTo(moved[2]), 1.0, 1.75);
        Assert.Equal(10, Leon.DistanceKmTo(moved[2]), 0.5);
    }

    [Fact]
    public void Home_in_a_town_stays_and_so_does_a_waypoint_too_deep_in_one()
    {
        var far = Leon.Destination(180, 12);
        var towns = Towns((Leon, 2), (far, 6));

        var moved = TownAvoidance.MoveOut([Leon, far], towns);

        Assert.Equal([Leon, far], moved);
    }

    [Fact]
    public async Task The_generator_never_asks_for_a_waypoint_in_a_town()
    {
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5));
        // A town on each of the fan's points and on the far point of the out and back.
        var radius = LoopWaypoints.InitialRadiusKm(60);
        var fan = LoopWaypoints.Fan(Leon, 90, radius);
        var towns = Towns([.. fan.Skip(1).SkipLast(1).Select(p => (p, 0.8)), (Leon.Destination(90, 60 / 2.0 / 1.3), 0.8)]);
        var generator = new LoopGenerator(engine).WithTowns(towns);

        var loop = await generator.GenerateAsync(Leon, 90, 60);
        var outAndBack = await generator.GenerateAsync(Leon, 90, 60, RouteShape.OutAndBack);

        Assert.True(loop.Found && outAndBack.Found);
        Assert.All(engine.Requests.SelectMany(w => w.Skip(1)).Where(p => p != Leon), p => Assert.False(towns.Contains(p)));
        Assert.Same(towns, generator.Towns);
    }

    [Fact]
    public async Task The_planner_moves_waypoints_out_of_towns_and_credits_the_map()
    {
        var engine = new FakeRouteEngine(w => FakeRouteEngine.Straight(w, roadFactor: 1.5));
        var radius = LoopWaypoints.InitialRadiusKm(60);
        var towns = new FakeBuiltUpSource(LoopWaypoints.Fan(Leon, 194, radius).Skip(1).SkipLast(1).Select(p => (p, 0.8)).ToList());
        var planner = new RidePlanner(new LoopGenerator(engine), Wind(15, 194), towns);

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        // Asked once, far enough for an out and back (0.4 of the distance plus 5 km).
        Assert.Equal((Leon, 29.0), Assert.Single(towns.Requests));
        var map = TownMap.From(FakeBuiltUpSource.Grid(Leon, 29, LoopWaypoints.Fan(Leon, 194, radius).Skip(1).SkipLast(1).Select(p => (p, 0.8)).ToList()));
        Assert.All(engine.Requests.SelectMany(w => w).Where(p => p != Leon), p => Assert.False(map.Contains(p)));
        Assert.Equal("fake towns", plan.TownsAttribution);
        Assert.Null(plan.TownsFailure);
        Assert.NotEmpty(plan.Ranked);
    }

    [Fact]
    public async Task Without_a_town_map_the_planner_makes_the_routes_as_before_and_says_why()
    {
        var planner = new RidePlanner(new LoopGenerator(new FakeRouteEngine(w => FakeRouteEngine.Straight(w))), Wind(15, 194),
            new FakeBuiltUpSource([], fail: true));

        var plan = await planner.PlanAsync(Leon, 60, Eight);

        Assert.Equal("no towns today", plan.TownsFailure);
        Assert.Null(plan.TownsAttribution);
        Assert.Equal(6, plan.Ranked.Count);
    }

    private static FakeWindForecastProvider Wind(double speed, double from) =>
        new(Enumerable.Range(0, 8).Select(h => new WindSample(Eight.AddHours(h), speed, speed + 10, from, "fake")).ToList());
}
