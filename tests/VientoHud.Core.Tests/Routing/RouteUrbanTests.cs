using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class RouteUrbanTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    private static readonly Dictionary<string, string> Road = new() { ["highway"] = "tertiary", ["surface"] = "asphalt" };
    private static readonly Dictionary<string, string> Street = new() { ["highway"] = "residential", ["surface"] = "asphalt" };
    private static readonly Dictionary<string, string> Crossing = new() { ["highway"] = "crossing" };
    private static readonly Dictionary<string, string> Signals = new() { ["highway"] = "traffic_signals" };

    // 10 km due north from León, a point every 100 m, with the stretches and nodes given.
    private static Route Straight(IReadOnlyList<RouteSegment> segments, IReadOnlyList<RouteNode> nodes)
    {
        var points = Enumerable.Range(0, 101).Select(i => new RoutePoint(Leon.Destination(0, i * 0.1))).ToList();
        return new Route(points, segments, 10, null, "test", nodes);
    }

    [Fact]
    public void A_km_with_two_crossings_or_signals_is_in_town()
    {
        // km 0: two crossings; km 3: crossing and signals; km 6: one crossing only.
        var route = Straight([new RouteSegment(0, 10, Road)],
            [new(0.2, Crossing), new(0.7, Crossing), new(3.1, Crossing), new(3.5, Signals), new(6.4, Crossing)]);

        var urban = RouteUrban.Measure(route);

        Assert.Equal(2, urban.UrbanKm);
        Assert.Equal([(0.0, 1.0), (3.0, 4.0)], urban.UrbanStretches);
        Assert.Equal(0.2, urban.UrbanFraction, 9);
    }

    [Fact]
    public void A_km_mostly_on_streets_is_in_town_and_neighbours_join()
    {
        // Streets from km 4.3 to 6.8: km 4 has 0.7 of street, km 5 all, km 6 0.8.
        var route = Straight(
            [new RouteSegment(0, 4.3, Road), new RouteSegment(4.3, 2.5, Street), new RouteSegment(6.8, 3.2, Road)], []);

        var urban = RouteUrban.Measure(route);

        Assert.Equal([(4.0, 7.0)], urban.UrbanStretches);
        Assert.Equal(3, urban.UrbanKm);
    }

    [Fact]
    public void Crossing_tags_on_their_own_count_too()
    {
        var route = Straight([new RouteSegment(0, 10, Road)],
            [new(8.1, new Dictionary<string, string> { ["crossing"] = "uncontrolled" }), new(8.6, Crossing)]);

        Assert.Equal([(8.0, 9.0)], RouteUrban.Measure(route).UrbanStretches);
    }

    [Fact]
    public void Coming_back_near_home_in_the_middle_counts()
    {
        // A figure of eight: 6 km north, back to León, 6 km south and back: 24 km, passing home at km 12.
        var points = new List<RoutePoint>();
        foreach (var (bearing, outward) in new[] { (0.0, true), (0.0, false), (180.0, true), (180.0, false) })
        {
            for (var i = 0; i <= 60; i++)
            {
                points.Add(new RoutePoint(Leon.Destination(bearing, (outward ? i : 60 - i) * 0.1)));
            }
        }
        var route = new Route(points, [new RouteSegment(0, 24, Road)], 24, null, "test");

        var urban = RouteUrban.Measure(route);

        // Within 3 km of home from km 9 to 15, all beyond the first and last 5 km.
        Assert.Equal(6, urban.BackHomeKm, tolerance: 0.2);
    }

    [Fact]
    public async Task Reads_nodes_from_brouter_and_finds_town_on_the_leon_loop()
    {
        // Recorded León loop (17 km, radius 3 km around the city): a lot of it is in town.
        var route = await RecordedRoutes.LeonNorth.LoadAsync();

        var urban = RouteUrban.Measure(route);

        Assert.NotEmpty(route.Nodes);
        Assert.Contains(route.Nodes, n => n.Tags.GetValueOrDefault("highway") == "crossing");
        Assert.True(route.Nodes.Zip(route.Nodes.Skip(1)).All(p => p.First.AtKm <= p.Second.AtKm));
        Assert.True(urban.UrbanFraction > 0.3);
    }

    [Fact]
    public void Nodes_turn_round_with_the_route()
    {
        var route = Straight([new RouteSegment(0, 10, Road)], [new(0.2, Crossing), new(0.7, Crossing)]);

        Assert.Equal([9.3, 9.8], route.Reversed().Nodes.Select(n => Math.Round(n.AtKm, 9)));
        Assert.Equal([0.2, 0.7, 19.3, 19.8], route.ThereAndBack().Nodes.Select(n => Math.Round(n.AtKm, 9)));
    }
}
