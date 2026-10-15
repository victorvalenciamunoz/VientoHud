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

    /// <summary>Stretch positions rounded to the metre, so hand-computed values compare exactly.</summary>
    private static List<RepeatedStretch> Rounded(IEnumerable<RepeatedStretch> stretches) =>
        stretches.Select(r => r with
        {
            StartKm = Math.Round(r.StartKm, 3), EndKm = Math.Round(r.EndKm, 3),
            OtherPassStartKm = Math.Round(r.OtherPassStartKm, 3), OtherPassEndKm = Math.Round(r.OtherPassEndKm, 3),
        }).ToList();

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
        // Only the turn lies between the passes, but the road starts at home: the ride itself, not a spur.
        Assert.Equal(
            [new RepeatedStretch(0.5, 4.5, 5.5, 9.5, IsSpur: false), new RepeatedStretch(5.5, 9.5, 0.5, 4.5, IsSpur: false)],
            Rounded(analysis.Repeats));
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void Measures_repetition_on_a_track_that_does_not_end_where_it_starts()
    {
        // 5 km north and 4.5 km back: a recorded ride stopped before reaching home.
        var route = Track(9.5, (0, 0), (0, 5), (0, 0.5));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // 96 checked points: km 0.5-4.5 out (41) and km 5.5-9.5 back (41).
        Assert.Equal(82 / 96.0, analysis.RepeatedFraction, 6);
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void A_loop_that_never_rides_the_same_road_twice_repeats_nothing_not_even_where_it_ends_at_home()
    {
        var route = Track(20, (0, 0), (0, 5), (5, 5), (5, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.Equal(0, analysis.RepeatedFraction);
        Assert.Equal(0, analysis.SpurFraction);
        Assert.Empty(analysis.Repeats);
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
        // A balloon: the 20 km loop lies between the two passes of its string.
        Assert.Equal(
            [new RepeatedStretch(0.5, 1, 21, 21.5, IsSpur: false), new RepeatedStretch(21, 21.5, 0.5, 1, IsSpur: false)],
            Rounded(analysis.Repeats));
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void A_neck_between_a_loop_near_home_and_a_loop_far_away_is_not_a_spur()
    {
        // A lasso: 3 km out of home, a 3 km neck, a 20 km loop, the neck back, and 9 km home by other roads.
        var route = Track(
            38, (0, 0), (0, 3), (0, 6), (0, 11), (5, 11), (5, 6), (0, 6), (0, 3), (-3, 3), (-3, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // The neck, km 3-6 and km 26-29: 31 + 31 checked points of 381.
        Assert.Equal(62 / 381.0, analysis.RepeatedFraction, 6);
        Assert.Equal(
            [new RepeatedStretch(3, 6, 26, 29, IsSpur: false), new RepeatedStretch(26, 29, 3, 6, IsSpur: false)],
            Rounded(analysis.Repeats));
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void An_out_and_back_ride_may_leave_and_enter_town_by_other_streets()
    {
        // Out and back 8 km on one road, but the first 2 km out and the last 2 km home use parallel streets 200 m apart.
        var route = Track(20.4, (0, 0), (0, 2), (0, 10), (0, 2), (0.2, 2), (0.2, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // Repeated: km 2-9.5 out and km 10.5-18 back, 76 + 76 checked points of 205. It starts 2 km from home and
        // ends 2.4 km from home, within the 3 km allowed: the ride itself.
        Assert.Equal(152 / 205.0, analysis.RepeatedFraction, 6);
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void A_dead_end_ridden_in_and_out_in_the_middle_of_a_loop_is_a_spur()
    {
        // A 20 km square with a 2 km dead end at its far corner, ridden there and back: a star with one arm.
        var route = Track(24, (0, 0), (0, 5), (0, 7), (0, 5), (5, 5), (5, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // Repeated: km 5-6.5 into the spur and km 7.5-9 out of it, 16 + 16 checked points of 241.
        Assert.Equal(32 / 241.0, analysis.RepeatedFraction, 6);
        Assert.Equal(
            [new RepeatedStretch(5, 6.5, 7.5, 9, IsSpur: true), new RepeatedStretch(7.5, 9, 5, 6.5, IsSpur: true)],
            Rounded(analysis.Repeats));
        Assert.Equal(32 / 241.0, analysis.SpurFraction, 6);
        Assert.Equal(32 / 241.0 * 24, analysis.SpurKm, 6);
    }

    [Fact]
    public void A_dead_end_to_the_farthest_point_is_the_destination_not_a_spur()
    {
        // The balloon in the middle: 4 km out of home, one side of a 14 km loop, 4 km to a dead end (the farthest point
        // of the ride) and back, the other side of the loop and the same 4 km home.
        var route = Track(30, (0, 0), (0, 4), (3, 4), (3, 8), (3, 12), (3, 8), (0, 8), (0, 4), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.Contains(analysis.Repeats, r => r.StartKm is >= 11 and <= 11.2);
        Assert.All(analysis.Repeats, r => Assert.False(r.IsSpur));
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void Only_the_dead_end_to_the_farthest_point_is_the_destination()
    {
        // A star with two arms on a 40 km square: a 3 km dead end at the second corner and another at the far corner.
        var route = Track(
            52, (0, 0), (0, 10), (-3, 10), (0, 10), (10, 10), (10, 13), (10, 10), (10, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        // The first arm, km 10-12.5 in and km 13.5-16 out, is a spur: about 26 + 26 checked points of 521.
        Assert.Contains(analysis.Repeats, r => r.IsSpur && r.StartKm is >= 10 and <= 10.2);
        Assert.Contains(analysis.Repeats, r => !r.IsSpur && r.StartKm is >= 26 and <= 26.2);
        Assert.InRange(analysis.SpurFraction, 50 / 521.0, 53 / 521.0);
    }

    [Fact]
    public void A_spur_right_after_the_string_from_home_is_still_a_spur()
    {
        // 5 km out of home, a 3 km dead end ridden in and out, a 20 km loop, and the same 5 km home. The string and the
        // spur are back to back along the route, but their other passes are in different places.
        var route = Track(36, (0, 0), (0, 5), (3, 5), (0, 5), (0, 10), (-5, 10), (-5, 5), (0, 5), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.All(analysis.Repeats.Where(r => r.EndKm <= 5), r => Assert.False(r.IsSpur));
        Assert.Contains(analysis.Repeats, r => r.IsSpur && r.StartKm is >= 5 and <= 5.2 && Math.Abs(r.EndKm - 7.5) < 0.01);
        // The spur, km 5-7.5 in and km 8.5-11 out: about 26 + 26 checked points of 361.
        Assert.InRange(analysis.SpurFraction, 50 / 361.0, 53 / 361.0);
    }

    [Fact]
    public void A_short_loop_at_the_end_of_a_dead_end_is_still_a_spur()
    {
        // A 26 km loop with a 2 km dead end that ends in a 2 km block around a village instead of turning back. The far
        // corner of the loop, not the village, is the farthest point from home.
        var route = Track(
            32, (0, 0), (0, 5), (0, 7), (0.5, 7), (0.5, 7.5), (0, 7.5), (0, 7), (0, 5), (8, 5), (8, 0), (0, 0));

        var analysis = RouteAnalyzer.Analyze(route, []);

        Assert.NotEmpty(analysis.Repeats);
        Assert.All(analysis.Repeats, r => Assert.True(r.IsSpur));
        Assert.True(analysis.SpurFraction > 0.1, $"{analysis.SpurFraction:P1}");
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
    [InlineData(0, 0.03, 1, 5, 3)]
    [InlineData(0.1, 0, 1, 5, 3)]
    [InlineData(0.1, 0.03, -1, 5, 3)]
    [InlineData(0.1, 0.03, 1, -1, 3)]
    [InlineData(0.1, 0.03, 1, 5, -1)]
    public void Rejects_non_positive_repetition_settings(
        double spacing, double tolerance, double separation, double minLoop, double homeStretch)
    {
        var route = Track(1, (0, 0), (0, 1));
        var options = new RouteAnalysisOptions
        {
            SampleSpacingKm = spacing, RepeatToleranceKm = tolerance, MinRepeatSeparationKm = separation,
            MinLoopKm = minLoop, HomeStretchKm = homeStretch,
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
    public async Task A_star_from_fan_waypoints_repeats_mostly_on_spurs()
    {
        foreach (var recorded in new[] { RecordedRoutes.LeonEastRepeats, RecordedRoutes.TresCantosSouthwestOffset })
        {
            var analysis = RouteAnalyzer.Analyze(await recorded.LoadAsync(), recorded.Waypoints);

            // The arm that reaches farthest counts as the destination; the others are spurs.
            Assert.True(analysis.SpurFraction > 0.3, $"{recorded.File}: spurs {analysis.SpurFraction:P0}");
        }
    }

    [Fact]
    public void A_real_balloon_ride_repeats_half_the_way_but_on_no_spur()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.HumanesBalloon(), []);

        Assert.InRange(analysis.DistanceKm, 73, 74);
        Assert.InRange(analysis.RepeatedFraction, 0.45, 0.55);
        Assert.True(analysis.SpurFraction < 0.01, $"{analysis.SpurFraction:P1}");
    }

    [Fact]
    public void A_real_out_and_back_ride_repeats_almost_all_the_way_but_on_no_spur()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.LaBanezaOutAndBack(), []);

        Assert.InRange(analysis.DistanceKm, 95, 97);
        Assert.True(analysis.RepeatedFraction > 0.9, $"{analysis.RepeatedFraction:P1}");
        Assert.True(analysis.SpurFraction < 0.01, $"{analysis.SpurFraction:P1}");
    }

    [Fact]
    public void A_real_ride_with_the_balloon_in_the_middle_repeats_two_thirds_but_on_no_spur()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.HumanesBalloonInTheMiddle(), []);

        Assert.InRange(analysis.DistanceKm, 21, 22);
        Assert.InRange(analysis.RepeatedFraction, 0.6, 0.7);
        // The dead end to the farthest point, km 6-9.5 in and km 10.5-13.5 out, is the destination.
        Assert.Contains(analysis.Repeats, r => r.StartKm is > 6 and < 7 && !r.IsSpur);
        Assert.Equal(0, analysis.SpurFraction);
    }

    [Fact]
    public void A_real_lasso_ride_repeats_its_neck_but_on_no_spur()
    {
        var analysis = RouteAnalyzer.Analyze(RecordedRides.BoadillaLasso(), []);

        Assert.InRange(analysis.DistanceKm, 83, 85);
        // The 4.6 km neck, km 8-13 out and km 62-67 back, with the 50 km loop in between.
        Assert.Contains(analysis.Repeats, r => !r.IsSpur && r.StartKm < 9 && r.EndKm > 12 && r.OtherPassStartKm > 61);
        Assert.InRange(analysis.RepeatedFraction, 0.09, 0.13);
        Assert.True(analysis.SpurFraction < 0.02, $"{analysis.SpurFraction:P1}");
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
