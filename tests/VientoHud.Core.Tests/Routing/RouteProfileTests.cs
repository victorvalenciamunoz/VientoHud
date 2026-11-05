using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using Xunit.Abstractions;

namespace VientoHud.Core.Tests.Routing;

public partial class RouteProfileTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;

    // A straight road north with a track point every 100 m and the elevation given by a function of the km. The
    // elevation takes the nominal km of each point, not the distance measured back from the origin: that one may land
    // a hair either side of a step like "from km 2" depending on the platform's floating point.
    private static Route Road(double km, Func<double, double> elevationAt, double pointEveryKm = 0.1)
    {
        var steps = (int)Math.Round(km / pointEveryKm);
        var points = Enumerable.Range(0, steps + 1)
            .Select(s => km * s / steps)
            .Select(at => new RoutePoint(Origin.Destination(0, at), elevationAt(at)))
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
        // The stretch takes in the blurred corners, so its average is a little under 8 %.
        Assert.InRange(profile.Stretches[1].GradePercent, 7, 8);
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
        // Flat, then a 60 m bump over 2 km (up 1 km at 6 % on average, down 1 km), then flat.
        var profile = RouteProfile.From(Road(6, km => km is >= 2 and <= 4 ? 800 + 60 * Math.Sin((km - 2) * Math.PI / 2) : 800))!;

        Assert.Equal(60, profile.AscentMeters, 0);
        Assert.Equal(60, profile.DescentMeters, 0);
        // The climb's steps start gentle, get steep and ease off: joined, the stretch takes its average grade.
        Assert.Equal(GradeBand.Moderate, profile.StretchAt(2.5).Band);
        Assert.Equal(GradeBand.Downhill, profile.StretchAt(3.5).Band);
        Assert.Equal(GradeBand.Flat, profile.StretchAt(1).Band);
        Assert.Equal(GradeBand.Flat, profile.StretchAt(5).Band);
    }

    [Fact]
    public void The_smoothing_does_not_depend_on_a_hair_of_route_length()
    {
        // 60 steps either way, a hair longer or shorter than 100 m: half the 300 m smoothing lands either side of one
        // and a half steps, and it used to round one way or the other (the same route gave other stretches on Linux).
        static double Bump(double km) => km is >= 2 and <= 4 ? 800 + 60 * Math.Sin((km - 2) * Math.PI / 2) : 800;
        var longer = RouteProfile.From(Road(6.0004, Bump))!;
        var shorter = RouteProfile.From(Road(5.9996, Bump))!;

        Assert.Equal(longer.Stretches.Select(s => s.Band), shorter.Stretches.Select(s => s.Band));
        Assert.Equal(GradeBand.Moderate, shorter.StretchAt(2.5).Band);
    }

    [Fact]
    public void Without_elevations_there_is_no_profile()
    {
        var route = new Route([new RoutePoint(Origin), new RoutePoint(Origin.Destination(0, 1), 800)], [], 1, null, "Test");

        Assert.Null(RouteProfile.From(route));
    }

    [Fact]
    public void The_line_says_over_how_much_the_steepest_ramp_is_measured()
    {
        var profile = RouteProfile.From(Road(4, km => 800 + km * 50))!;

        Assert.Equal("Sube 200 m y baja 0 m, entre 800 y 1000 m; rampa máxima 5 % (en 300 m).", VientoHud.Core.Text.RideText.Profile(profile));
    }

    [Fact]
    public void Rejects_bad_input()
    {
        var track = new[] { Origin, Origin.Destination(0, 1) };

        Assert.Throws<ArgumentException>(() => RouteProfile.From(track, [800]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From(track, [800, double.NaN]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From([Origin], [800]));
        Assert.Throws<ArgumentException>(() => RouteProfile.From([Origin, Origin], [800, 810]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteProfile.From(track, [800, 810], stepKm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteProfile.From(track, [800, 810], smoothingKm: -1));
    }

    [Fact]
    public void Can_be_built_from_elevations_sampled_elsewhere_and_without_smoothing()
    {
        var track = Enumerable.Range(0, 11).Select(i => Origin.Destination(90, i * 0.1)).ToArray();
        var elevations = Enumerable.Range(0, 11).Select(i => 800.0 + i * 3).ToArray();

        var profile = RouteProfile.From(track, elevations, smoothingKm: 0);

        Assert.Equal(30, profile.AscentMeters, 0);
        Assert.Equal(3, profile.MaxGradePercent, 1);
        Assert.All(profile.Points, p => Assert.Equal(800 + p.Km * 30, p.ElevationMeters, 1));
    }
}
