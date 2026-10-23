using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using Xunit.Abstractions;

namespace VientoHud.Core.Tests.Routing;

public class RouteProfileTests(ITestOutputHelper output)
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    // A straight road north with a track point every 100 m and the elevation given by a function of the km.
    private static Route Road(double km, Func<double, double> elevationAt, double pointEveryKm = 0.1)
    {
        var steps = (int)Math.Round(km / pointEveryKm);
        var points = Enumerable.Range(0, steps + 1)
            .Select(s => Leon.Destination(0, km * s / steps))
            .Select(p => new RoutePoint(p, elevationAt(Leon.DistanceKmTo(p))))
            .ToList();
        return new Route(points, [], km, null, "Test");
    }

    [Fact]
    public void A_steady_climb_is_one_stretch_with_its_grade_and_climb()
    {
        // 5 % for 4 km: 200 m up.
        var profile = RouteProfile.From(Road(4, km => 800 + km * 50))!;

        var stretch = Assert.Single(profile.Stretches);
        Assert.Equal(GradeBand.Moderate, stretch.Band);
        Assert.Equal(0, stretch.StartKm);
        Assert.Equal(4, stretch.EndKm, 6);
        Assert.Equal(5, stretch.GradePercent, 1);
        Assert.Equal(200, profile.AscentMeters, 0);
        Assert.Equal(0, profile.DescentMeters, 0);
        Assert.Equal(800, profile.MinElevationMeters, 0);
        Assert.Equal(1000, profile.MaxElevationMeters, 0);
        Assert.Equal(5, profile.MaxGradePercent, 1);
        Assert.Equal(4, profile.LengthKm, 6);
        Assert.Equal(41, profile.Points.Count);
    }

    [Fact]
    public void Cuts_the_route_where_the_band_changes()
    {
        // 2 km flat, 2 km at 8 %, 2 km down at 8 %, 2 km flat.
        var profile = RouteProfile.From(Road(8, km => km switch
        {
            < 2 => 800,
            < 4 => 800 + (km - 2) * 80,
            < 6 => 960 - (km - 4) * 80,
            _ => 800,
        }))!;

        // The four pieces, in order: the smoothing blurs each corner over a couple of hundred metres, but the short
        // in-between bands that appear there join their neighbours (MinStretchKm).
        Assert.Equal(
            [GradeBand.Flat, GradeBand.Steep, GradeBand.Downhill, GradeBand.Flat],
            profile.Stretches.Select(s => s.Band));
        Assert.Equal(2, profile.Stretches[1].StartKm, 0);
        Assert.Equal(4, profile.Stretches[1].EndKm, 0);
        Assert.Equal(8, profile.Stretches[1].GradePercent, 0);
        Assert.Equal(GradeBand.Steep, profile.StretchAt(3).Band);
        Assert.Equal(GradeBand.Downhill, profile.StretchAt(5).Band);
        Assert.Equal(160, profile.AscentMeters, 0);
        Assert.Equal(160, profile.DescentMeters, 0);
        Assert.Equal(880, profile.ElevationAt(3), 0);
        Assert.Equal(8, profile.MaxGradePercent, 0);
    }

    [Fact]
    public void Wobbles_of_a_few_metres_do_not_count_as_climb()
    {
        // Flat road whose elevations jump ±4 m from point to point, as a 90 m grid does.
        var profile = RouteProfile.From(Road(10, km => 800 + (Math.Round(km * 10) % 2 == 0 ? 4 : -4)))!;

        Assert.Equal(0, profile.AscentMeters);
        Assert.Equal(0, profile.DescentMeters);
        // Flat all along: at the very ends the smoothing has fewer points and a step may tip over, but a band too
        // short to show joins its neighbour.
        var stretch = Assert.Single(profile.Stretches);
        Assert.Equal(GradeBand.Flat, stretch.Band);
        Assert.InRange(stretch.GradePercent, -1, 1);
    }

    [Fact]
    public void A_real_bump_counts_once_it_rises_more_than_the_hysteresis()
    {
        // Flat, then a 30 m bump over 1 km, then flat.
        var profile = RouteProfile.From(Road(5, km => km is >= 2 and <= 3 ? 800 + 30 * Math.Sin((km - 2) * Math.PI) : 800))!;

        Assert.Equal(30, profile.AscentMeters, 0);
        Assert.Equal(30, profile.DescentMeters, 0);
        Assert.Equal(GradeBand.Moderate, profile.StretchAt(2.25).Band);
        Assert.Equal(GradeBand.Downhill, profile.StretchAt(2.75).Band);
    }

    [Fact]
    public void Without_elevations_there_is_no_profile()
    {
        var route = new Route([new RoutePoint(Leon), new RoutePoint(Leon.Destination(0, 1), 800)], [], 1, null, "Test");

        Assert.Null(RouteProfile.From(route));
    }

    [Fact]
    public void Rejects_bad_input()
    {
        var track = new[] { Leon, Leon.Destination(0, 1) };

        Assert.Throws<ArgumentException>(() => RouteProfile.From(track, [800]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From(track, [800, double.NaN]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From([Leon], [800]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From([Leon, Leon], [800, 810]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteProfile.From(track, [800, 810], stepKm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteProfile.From(track, [800, 810], smoothingKm: -1));
    }

    [Fact]
    public void Can_be_built_from_elevations_sampled_elsewhere_and_without_smoothing()
    {
        var track = Enumerable.Range(0, 11).Select(i => Leon.Destination(90, i * 0.1)).ToArray();
        var elevations = Enumerable.Range(0, 11).Select(i => 800.0 + i * 3).ToArray();

        var profile = RouteProfile.From(track, elevations, smoothingKm: 0);

        Assert.Equal(30, profile.AscentMeters, 0);
        Assert.Equal(3, profile.MaxGradePercent, 1);
        Assert.All(profile.Points, p => Assert.Equal(800 + p.Km * 30, p.ElevationMeters, 1));
    }

    // Judge of step 1 (phase 8): our climb against BRouter's own "filtered ascend" on the recorded routes. The two
    // filters differ, so they need not match exactly; this records how far apart they are.
    [Theory]
    [InlineData("LeonNorth")]
    [InlineData("LeonEastRepeats")]
    [InlineData("LeonSouthFastbike")]
    [InlineData("TresCantosSouthwestOffset")]
    public async Task Climb_of_a_recorded_route_is_close_to_the_engines(string name)
    {
        var recorded = (RecordedRoute)typeof(RecordedRoutes).GetField(name)!.GetValue(null)!;
        var route = await recorded.LoadAsync();

        var profile = RouteProfile.From(route)!;

        output.WriteLine(
            $"{name}: {route.DistanceKm:0.0} km, BRouter {route.AscentMeters} m, profile up {profile.AscentMeters:0} m / " +
            $"down {profile.DescentMeters:0} m, {profile.MinElevationMeters:0}-{profile.MaxElevationMeters:0} m, " +
            $"max {profile.MaxGradePercent:0.0} %, {profile.Stretches.Count} stretches");
        Assert.Equal(route.DistanceKm, profile.LengthKm, 0);
        Assert.InRange(profile.AscentMeters, route.AscentMeters!.Value * 0.8, route.AscentMeters.Value * 1.25);
        // A loop drops what it climbs.
        Assert.InRange(profile.DescentMeters, profile.AscentMeters - 5, profile.AscentMeters + 5);
        Assert.Equal(0, profile.Stretches[0].StartKm);
        Assert.Equal(profile.LengthKm, profile.Stretches[^1].EndKm, 6);
        Assert.All(profile.Stretches.Zip(profile.Stretches.Skip(1)), pair => Assert.Equal(pair.First.EndKm, pair.Second.StartKm));
        Assert.All(profile.Stretches, s => Assert.True(s.LengthKm >= RouteProfile.MinStretchKm - 1e-9, $"{s.Band} {s.LengthKm} km"));
        Assert.All(profile.Stretches, s => Assert.Equal(profile.ElevationAt(s.StartKm), s.StartElevationMeters, 6));
    }
}
