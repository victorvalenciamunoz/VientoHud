using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class RouteAnalyzerTests
{
    private const double KmPerDegree = 6371.0088 * Math.PI / 180;
    private static readonly Coordinate Origin = new(40, -3.7);

    /// <summary>A position <paramref name="eastKm"/> east and <paramref name="northKm"/> north of the origin.</summary>
    private static Coordinate At(double eastKm, double northKm) => new(
        Origin.Latitude + northKm / KmPerDegree,
        Origin.Longitude + eastKm / (KmPerDegree * Math.Cos(Origin.Latitude * Math.PI / 180)));

    private static RouteSegment Road(double startKm, double lengthKm, string highway = "tertiary", string? surface = "asphalt")
    {
        var tags = new Dictionary<string, string> { ["highway"] = highway };
        if (surface is not null)
        {
            tags["surface"] = surface;
        }
        return new RouteSegment(startKm, lengthKm, tags);
    }

    /// <summary>A route along <paramref name="points"/> (km east, km north), all on one paved road.</summary>
    private static Route Track(double lengthKm, params (double East, double North)[] points) => new(
        points.Select(p => new RoutePoint(At(p.East, p.North))).ToList(),
        [Road(0, lengthKm)],
        lengthKm,
        null,
        "test");

    [Fact]
    public void Out_and_back_on_one_road_is_repeated_except_near_the_start_and_the_turn()
    {
        // 5 km north and back on the same road, drawn with only its ends: most checked points are km from any track point.
        var route = Track(10, (0, 0), (0, 5), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // 101 checked points (every 100 m). Within 1 km along the loop of the turn (km 4.6-5.4) and of the start
        // (km 9.6-0.4, the loop is closed) the other pass is too close along the track to count:
        // 41 repeated on the way out (km 0.5-4.5) and 41 on the way back (km 5.5-9.5).
        Assert.Equal(82 / 101.0, analysis.RepeatedFraction, 6);
        Assert.Equal(82 / 101.0 * 10, analysis.RepeatedKm, 6);
        // All of it is the stem: km 0-4.5 out and km 5.5-10 back (the turn, ridden once, is not).
        Assert.Equal(0.9, analysis.StemFraction, 6);
        Assert.Equal(9, analysis.StemKm, 6);
        Assert.Equal(0, analysis.RepeatedOffStemFraction);
    }

    [Fact]
    public void Measures_repetition_on_a_track_that_does_not_end_where_it_starts()
    {
        // 5 km north and 4.5 km back: a recorded ride stopped before reaching home.
        var route = Track(9.5, (0, 0), (0, 5), (0, 0.5));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // 96 checked points: km 0.5-4.5 out (41) and km 5.5-9.5 back (41). Stem: 4.5 km out and 4 km back.
        Assert.Equal(82 / 96.0, analysis.RepeatedFraction, 6);
        Assert.Equal(8.5, analysis.StemKm, 6);
        Assert.Equal(0, analysis.RepeatedOffStemFraction);
    }

    [Fact]
    public void A_loop_that_never_rides_the_same_road_twice_repeats_nothing_not_even_where_it_ends_at_home()
    {
        var route = Track(20, (0, 0), (0, 5), (5, 5), (5, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.Equal(0, analysis.RepeatedFraction);
        Assert.Equal(0, analysis.StemFraction);
        Assert.Equal(0, analysis.RepeatedOffStemFraction);
    }

    [Fact]
    public void Leaving_and_coming_home_by_the_same_road_counts_as_repeated()
    {
        // 1 km out of home, a 20 km square, and the same 1 km back.
        var route = Track(22, (0, 0), (0, 1), (0, 6), (5, 6), (5, 1), (0, 1), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // Separation is measured around the loop, so the 0.5 km nearest home do not count:
        // km 0.5-1 on the way out and km 21-21.5 on the way back, 6 + 6 checked points of 221.
        Assert.Equal(12 / 221.0, analysis.RepeatedFraction, 6);
        // A balloon: the stem includes the stretch next to home.
        Assert.Equal(2, analysis.StemKm, 6);
        Assert.Equal(0, analysis.RepeatedOffStemFraction);
    }

    [Fact]
    public void The_stem_may_start_after_leaving_town_by_other_streets()
    {
        // Out and back 8 km on one road, but the first 2 km out and the last 2 km home use parallel streets 200 m apart.
        var route = Track(20.4, (0, 0), (0, 2), (0, 10), (0, 2), (0.2, 2), (0.2, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // Repeated: km 2-9.5 out and km 10.5-18 back, 76 + 76 checked points of 205. The stem starts 2 km from each
        // end and runs to km 9.5 out and from km 10.5 home: 9.5 + 9.9 km.
        Assert.Equal(152 / 205.0, analysis.RepeatedFraction, 6);
        Assert.Equal(19.4, analysis.StemKm, 6);
        Assert.Equal(0, analysis.RepeatedOffStemFraction);
    }

    [Fact]
    public void An_out_and_back_spur_in_the_middle_of_a_loop_is_repetition_off_the_stem()
    {
        // A 20 km square with a 2 km dead end at its far corner, ridden there and back: a star with one arm.
        var route = Track(24, (0, 0), (0, 5), (0, 7), (0, 5), (5, 5), (5, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // Repeated: km 5-6.5 into the spur and km 7.5-9 out of it, 16 + 16 checked points of 241.
        Assert.Equal(32 / 241.0, analysis.RepeatedFraction, 6);
        Assert.Equal(0, analysis.StemFraction);
        Assert.Equal(32 / 241.0, analysis.RepeatedOffStemFraction, 6);
    }

    [Fact]
    public void Measures_how_close_the_route_passes_to_each_waypoint_along_its_line()
    {
        // The track's points are 10 km apart; the waypoint is 2 km east of the line between them.
        var route = Track(10, (0, 0), (0, 10));

        var analysis = RouteAnalyzer.Analyze(route, [At(0, 0), At(2, 5), At(0, 10.5)]);

        Assert.Equal(3, analysis.WaypointOffsetsKm.Count);
        Assert.Equal(0, analysis.WaypointOffsetsKm[0], 6);
        Assert.Equal(2, analysis.WaypointOffsetsKm[1], 6);
        Assert.Equal(0.5, analysis.WaypointOffsetsKm[2], 6);
        Assert.Equal(2, analysis.MaxWaypointOffsetKm, 6);
    }

    [Fact]
    public void Adds_up_km_per_road_type_and_surface_and_by_category()
    {
        var route = new Route(
            [new RoutePoint(At(0, 0)), new RoutePoint(At(0, 9.8))],
            [
                Road(0, 3), Road(3, 1, "track", "gravel"), Road(4, 2), Road(6, 0.5, "track", "asphalt"),
                Road(6.5, 0.3, "trunk"), Road(6.8, 2, "residential", null), new RouteSegment(8.8, 1, new Dictionary<string, string>()),
            ],
            9.8,
            null,
            "test");

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.Equal(
            [
                new RoadUsage("tertiary", "asphalt", RoadCategory.Road, 5),
                new RoadUsage("residential", null, RoadCategory.Road, 2),
                new RoadUsage("track", "gravel", RoadCategory.Forbidden, 1),
                new RoadUsage(null, null, RoadCategory.Unknown, 1),
                new RoadUsage("track", "asphalt", RoadCategory.PavedTrack, 0.5),
                new RoadUsage("trunk", "asphalt", RoadCategory.Forbidden, 0.3),
            ],
            analysis.Roads);
        Assert.Equal(1.3, analysis.ForbiddenKm, 9);
        Assert.Equal(0.5, analysis.PavedTrackKm, 9);
        Assert.Equal(1, analysis.UnknownKm, 9);
    }

    [Theory]
    [InlineData(0, 0.03, 1, 3)]
    [InlineData(0.1, 0, 1, 3)]
    [InlineData(0.1, 0.03, -1, 3)]
    [InlineData(0.1, 0.03, 1, -1)]
    public void Rejects_non_positive_repetition_settings(double spacing, double tolerance, double separation, double stemStart)
    {
        var route = Track(1, (0, 0), (0, 1));
        var options = new RouteAnalysisOptions
        {
            SampleSpacingKm = spacing, RepeatToleranceKm = tolerance, MinRepeatSeparationKm = separation,
            MaxStemStartKm = stemStart,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => RouteAnalyzer.Analyze(route, [], options));
    }

    [Fact]
    public void Works_across_the_180_degree_meridian()
    {
        // Out and back over the date line, near Fiji: 2 x 5 km.
        var start = new Coordinate(-16.5, 179.98);
        var east = new Coordinate(-16.5, -179.973);
        var route = new Route([new RoutePoint(start), new RoutePoint(east), new RoutePoint(start)], [Road(0, 10)], 10, null, "test");

        var analysis = RouteAnalyzer.Analyze(route, [new Coordinate(-16.5, -179.99)]);

        Assert.Equal(0, analysis.WaypointOffsetsKm[0], 6);
        Assert.InRange(analysis.RepeatedFraction, 0.75, 0.85);
    }

    // Recorded routes (Fixtures/BRouter/README.md).

    [Fact]
    public async Task Routes_with_the_strict_profile_have_no_forbidden_km()
    {
        foreach (var recorded in new[] { RecordedRoutes.LeonNorth, RecordedRoutes.LeonEastRepeats, RecordedRoutes.TresCantosSouthwestOffset })
        {
            var analysis = RouteAnalyzer.Analyze(await recorded.LoadAsync(), recorded.Waypoints);

            Assert.Equal(0, analysis.ForbiddenKm);
            Assert.Equal(0, analysis.UnknownKm);
        }
    }

    [Fact]
    public async Task Finds_the_tracks_paths_and_trunk_road_of_a_route_with_the_stock_profile()
    {
        var recorded = RecordedRoutes.LeonSouthFastbike;
        var route = await recorded.LoadAsync();

        var analysis = RouteAnalyzer.Analyze(route, recorded.Waypoints);

        var forbidden = analysis.Roads.Where(r => r.Category == RoadCategory.Forbidden).ToList();
        Assert.Equal(
            ["path/", "track/", "track/ground", "trunk/asphalt", "trunk_link/"],
            forbidden.Select(r => $"{r.Highway}/{r.Surface}").Order(StringComparer.Ordinal));
        Assert.Equal(2.528, analysis.ForbiddenKm, 3);
        Assert.Equal(0.324, analysis.PavedTrackKm, 3);
        // Every km of the route is in some category.
        Assert.Equal(route.DistanceKm, analysis.Roads.Sum(r => r.Km), 3);
    }

    [Fact]
    public async Task Finds_the_waypoint_the_route_could_not_reach()
    {
        // The second fan point falls inside Monte de El Pardo, which has no open roads.
        var recorded = RecordedRoutes.TresCantosSouthwestOffset;
        var route = await recorded.LoadAsync();

        var analysis = RouteAnalyzer.Analyze(route, recorded.Waypoints);

        Assert.InRange(analysis.WaypointOffsetsKm[2], 2.6, 2.8);
        Assert.Equal(analysis.WaypointOffsetsKm[2], analysis.MaxWaypointOffsetKm);
        Assert.All(analysis.WaypointOffsetsKm.Where((_, i) => i != 2), o => Assert.True(o < 1.3, $"offset {o} km"));
        // The line can only be closer than the nearest track point.
        for (var i = 0; i < recorded.Waypoints.Count; i++)
        {
            var nearestPoint = route.Points.Min(p => p.Position.DistanceKmTo(recorded.Waypoints[i]));
            Assert.True(analysis.WaypointOffsetsKm[i] <= nearestPoint + 0.001, $"waypoint {i}");
        }
    }

    [Fact]
    public async Task Tells_a_loop_that_doubles_back_from_one_that_does_not()
    {
        var east = RouteAnalyzer.Analyze(await RecordedRoutes.LeonEastRepeats.LoadAsync(), RecordedRoutes.LeonEastRepeats.Waypoints);
        var north = RouteAnalyzer.Analyze(await RecordedRoutes.LeonNorth.LoadAsync(), RecordedRoutes.LeonNorth.Waypoints);

        Assert.InRange(east.RepeatedFraction, 0.80, 0.88);
        Assert.InRange(north.RepeatedFraction, 0.04, 0.10);
    }

    [Fact]
    public async Task A_star_from_fan_waypoints_repeats_off_the_stem()
    {
        foreach (var recorded in new[] { RecordedRoutes.LeonEastRepeats, RecordedRoutes.TresCantosSouthwestOffset })
        {
            var analysis = RouteAnalyzer.Analyze(await recorded.LoadAsync(), recorded.Waypoints);

            // Coming home along a road already ridden out of town is a short stem; the rest are spurs.
            Assert.True(analysis.StemFraction < 0.25, $"{recorded.File}: stem {analysis.StemFraction:P0}");
            Assert.True(analysis.RepeatedOffStemFraction > 0.5, $"{recorded.File}: off stem {analysis.RepeatedOffStemFraction:P0}");
        }
    }

    [Fact]
    public void A_real_balloon_ride_repeats_half_the_way_but_only_on_its_stem()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.HumanesBalloon(), []);

        Assert.InRange(analysis.DistanceKm, 73, 74);
        Assert.InRange(analysis.RepeatedFraction, 0.45, 0.55);
        Assert.InRange(analysis.StemKm, 37, 40);
        Assert.True(analysis.RepeatedOffStemFraction < 0.01, $"{analysis.RepeatedOffStemFraction:P1}");
    }

    [Fact]
    public void A_real_out_and_back_ride_is_all_stem()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.LaBanezaOutAndBack(), []);

        Assert.InRange(analysis.DistanceKm, 95, 97);
        Assert.True(analysis.StemFraction > 0.9, $"{analysis.StemFraction:P1}");
        Assert.True(analysis.RepeatedOffStemFraction < 0.01, $"{analysis.RepeatedOffStemFraction:P1}");
    }

    [Fact]
    public async Task Repetition_barely_depends_on_the_spacing_or_the_direction_of_travel()
    {
        var recorded = RecordedRoutes.TresCantosSouthwestOffset;
        var route = await recorded.LoadAsync();
        var reversed = new Route(route.Points.Reverse().ToList(), route.Segments, route.DistanceKm, null, route.Attribution);

        var every100m = RouteAnalyzer.Analyze(route, recorded.Waypoints).RepeatedFraction;
        var every50m = RouteAnalyzer.Analyze(route, recorded.Waypoints, new RouteAnalysisOptions { SampleSpacingKm = 0.05 }).RepeatedFraction;
        var backwards = RouteAnalyzer.Analyze(reversed, recorded.Waypoints).RepeatedFraction;

        Assert.Equal(every100m, every50m, 0.01);
        Assert.Equal(every100m, backwards, 0.01);
    }
}
