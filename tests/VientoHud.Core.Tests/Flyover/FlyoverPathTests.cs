using System.Text.Json;
using VientoHud.Core.Flyover;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Flyover;

public partial class FlyoverPathTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;

    private static double AngleBetween(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);

    // A route through the corners, a point every 100 m, at 800 m climbing 2 m every 100 m.
    private static Route Track(params Coordinate[] corners)
    {
        var points = new List<Coordinate> { corners[0] };
        foreach (var (from, to) in corners.Zip(corners.Skip(1)))
        {
            var km = from.DistanceKmTo(to);
            var steps = Math.Max(1, (int)Math.Round(km / 0.1));
            for (var s = 1; s <= steps; s++)
            {
                points.Add(from.Destination(from.BearingTo(to), km * s / steps));
            }
        }
        var routePoints = points.Select((p, i) => new RoutePoint(p, 800 + i * 2.0)).ToList();
        return new Route(routePoints, [], points.Count / 10.0, null, "Test");
    }

    [Fact]
    public void Follows_a_straight_road_facing_ahead_from_start_to_end()
    {
        var route = Track(Origin, Origin.Destination(0, 10));

        var path = FlyoverPath.Along(route, RouteProfile.From(route));

        Assert.Equal(201, path.Frames.Count);
        Assert.Equal(0, path.Frames[0].AtKm);
        Assert.Equal(10, path.Frames[^1].AtKm, 3);
        Assert.Equal(0, Origin.DistanceKmTo(path.Frames[0].Center), 3);
        Assert.Equal(0, route.Points[^1].Position.DistanceKmTo(path.Frames[^1].Center), 3);
        Assert.All(path.Frames, f => Assert.True(AngleBetween(0, f.BearingDegrees) < 0.5, $"km {f.AtKm}: {f.BearingDegrees}°"));
        // 2 m every 100 m is 2 %.
        Assert.All(path.Frames, f => Assert.Equal(2, f.GradePercent!.Value, 0));
        Assert.Equal(800 + 5 * 20, path.Frames[100].ElevationMeters!.Value, 0);
        Assert.Equal(10, path.LengthKm, 3);
    }

    [Fact]
    public void Turns_the_view_gradually_round_a_corner()
    {
        // 5 km east, then 5 km north: a right angle at km 5.
        var corner = Origin.Destination(90, 5);
        var route = Track(Origin, corner, corner.Destination(0, 5));

        var path = FlyoverPath.Along(route, null);

        Assert.True(AngleBetween(90, path.FrameAt(0.1).BearingDegrees) < 1);
        Assert.True(AngleBetween(0, path.FrameAt(0.9).BearingDegrees) < 1);
        // Never more than a few degrees from one frame (50 m) to the next.
        var turns = path.Frames.Zip(path.Frames.Skip(1), (a, b) => AngleBetween(a.BearingDegrees, b.BearingDegrees)).ToList();
        Assert.True(turns.Max() < 6, $"max turn {turns.Max():0.0}° between frames");
        // The turn is spread over a couple of km, not done at the corner.
        var turning = path.Frames.Where(f => AngleBetween(90, f.BearingDegrees) is > 5 and < 85).ToList();
        Assert.InRange(turning.Max(f => f.AtKm) - turning.Min(f => f.AtKm), 1, 3);
        Assert.All(path.Frames, f => Assert.Null(f.ElevationMeters));
    }

    [Fact]
    public void Turns_round_at_the_far_point_of_an_out_and_back()
    {
        var route = Track(Origin, Origin.Destination(90, 5)).ThereAndBack();

        var path = FlyoverPath.Along(route, null);

        Assert.True(AngleBetween(90, path.FrameAt(0.2).BearingDegrees) < 1);
        Assert.True(AngleBetween(270, path.FrameAt(0.8).BearingDegrees) < 1);
        var turns = path.Frames.Zip(path.Frames.Skip(1), (a, b) => AngleBetween(a.BearingDegrees, b.BearingDegrees)).ToList();
        Assert.True(turns.Max() <= FlyoverPath.MaxTurnDegreesPerKm * path.Frames[1].AtKm + 1e-6, $"max turn {turns.Max():0.0}° between frames");
    }

    [Theory]
    [InlineData(10, 40)]
    [InlineData(50, 75)]
    [InlineData(150, 150)]
    public void Takes_a_second_and_a_half_per_km_within_limits(double km, double seconds)
    {
        var path = FlyoverPath.Along(Track(Origin, Origin.Destination(0, km)), null, stepKm: 0.5);

        Assert.Equal(seconds, path.Duration.TotalSeconds, 0);
    }

    [Fact]
    public void Finds_the_frame_of_a_point_of_the_flight()
    {
        var path = FlyoverPath.Along(Track(Origin, Origin.Destination(0, 10)), null);

        Assert.Equal(0, path.FrameAt(0).AtKm);
        Assert.Equal(5, path.FrameAt(0.5).AtKm, 3);
        Assert.Equal(10, path.FrameAt(1).AtKm, 3);
        Assert.Equal(10, path.FrameAt(7).AtKm, 3);
        Assert.Equal(0, path.FrameAt(double.NaN).AtKm);
    }

    [Fact]
    public void Writes_json_the_view_can_play()
    {
        var path = FlyoverPath.Along(Track(Origin, Origin.Destination(0, 2)), null);

        using var doc = JsonDocument.Parse(path.ToJson());
        var root = doc.RootElement;

        Assert.Equal(path.Frames.Count, root.GetProperty("frames").GetArrayLength());
        var first = root.GetProperty("frames")[0];
        Assert.Equal(Origin.Longitude, first[0].GetDouble(), 5);
        Assert.Equal(Origin.Latitude, first[1].GetDouble(), 5);
        Assert.Equal((long)path.Duration.TotalMilliseconds, root.GetProperty("durationMs").GetInt64());
        Assert.Equal(CameraView.Close.Zoom, root.GetProperty("views").GetProperty("close").GetProperty("zoom").GetDouble());
        Assert.Equal(CameraView.Far.PitchDegrees, root.GetProperty("views").GetProperty("far").GetProperty("pitch").GetDouble());
    }

    [Fact]
    public void Rejects_bad_settings()
    {
        var route = Track(Origin, Origin.Destination(0, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => FlyoverPath.Along(route, null, stepKm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlyoverPath.Along(route, null, lookAheadKm: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlyoverPath.Along(route, null, smoothingKm: -1));
    }
}
