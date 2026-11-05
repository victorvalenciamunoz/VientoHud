using VientoHud.Core.Tests.Support;
using VientoHud.Core.Rain;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideMarkingTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // 10 km north from the origin and, when it is an out and back, the same 10 km back south; 1 km legs, 2.4 min each.
    // The wind and the chance of rain for each leg come from the caller.
    private static RideOption Ride(bool back, Func<int, LocalWind> wind, Func<int, double> rain)
    {
        var count = back ? 20 : 10;
        Coordinate At(double km) => Origin.Destination(0, km <= 10 ? km : 20 - km);
        var legs = Enumerable.Range(0, count).Select(i => new RouteLeg(
            i, i, 1, At(i), At(i + 1), At(i + 0.5), i < 10 ? 0 : 180, 1, null,
            Eight.AddMinutes(2.4 * i), Eight.AddMinutes(2.4 * (i + 1)))).ToList();
        var winds = legs.Select(l => new LegWind(l, wind(l.Index))).ToList();
        var legRain = legs.Select(l => new LegRain(l, new PlaceRain(rain(l.Index), RainSourceKind.Radar))).ToList();
        var points = Enumerable.Range(0, count + 1).Select(k => new RoutePoint(At(k))).ToList();
        var route = new Route(points, [], count, null, "test");
        return new RideOption(0, route, false, winds, new RideScore(0, 0, [], true), new RideRain(legRain, null));
    }

    private static LocalWind FromNorth(int leg) => new(20, null, 0, "fake");

    [Fact]
    public void An_out_and_back_with_a_steady_wind_has_one_white_arrow_per_place_and_each_pass_its_own_color()
    {
        // Rain likely on the first 4 km only: the way out gets wet, the way back is dry.
        var marks = RideMarking.Along(Ride(back: true, FromNorth, leg => leg < 4 ? 0.8 : 0));

        // Looked at km 2,5 and 7,5 on the way out and in the same places on the way back: one arrow each, on the road,
        // pointing south (the wind blows from the north), white.
        Assert.Equal(2, marks.Arrows.Count);
        Assert.All(marks.Arrows, a => Assert.Null(a.Feel));
        Assert.All(marks.Arrows, a => Assert.Equal(180, a.Tail.BearingTo(a.Tip), 0));
        Assert.Equal(2.5, Origin.DistanceKmTo(Mid(marks.Arrows[0])), 1);
        // The marks of the way round: in the face going north, from behind coming back.
        Assert.Equal(8, marks.Passes.Count);
        Assert.All(marks.Passes.Where(p => p.AtKm < 10), p => Assert.Equal((WindFeel.Headwind, 0.0), (p.Feel, Math.Round(p.BearingDegrees))));
        Assert.All(marks.Passes.Where(p => p.AtKm > 10), p => Assert.Equal((WindFeel.Tailwind, 180.0), (p.Feel, Math.Round(p.BearingDegrees))));
        // One drop, by a mark of the way out, to its rider's right (east).
        var drop = Assert.Single(marks.Drops);
        Assert.Equal(90, drop.SideDegrees, 0);
        Assert.Contains(marks.Passes, p => p.AtKm < 5 && p.Road == drop.Road);
    }

    [Fact]
    public void Where_the_wind_turned_between_passes_each_pass_has_its_own_arrow_on_its_side()
    {
        // From the north going out; from the east coming back.
        var marks = RideMarking.Along(Ride(back: true, leg => leg < 10 ? FromNorth(leg) : new LocalWind(20, null, 90, "fake"), _ => 0));

        Assert.Equal(4, marks.Arrows.Count);
        // The way out's to the east of the road (its right going north), the way back's to the west.
        Assert.Equal(2, marks.Arrows.Count(a => Mid(a).Longitude > Origin.Longitude + 0.005 && Math.Round(a.Tail.BearingTo(a.Tip)) == 180));
        Assert.Equal(2, marks.Arrows.Count(a => Mid(a).Longitude < Origin.Longitude - 0.005 && Math.Round(a.Tail.BearingTo(a.Tip)) == 270));
        Assert.Empty(marks.Drops);
    }

    [Fact]
    public void A_road_ridden_once_has_its_white_arrow_on_the_road_and_none_where_the_wind_is_light()
    {
        var marks = RideMarking.Along(Ride(back: false, leg => new LocalWind(leg < 5 ? 20 : 2, null, 0, "fake"), _ => 0));

        var arrow = Assert.Single(marks.Arrows);
        Assert.Null(arrow.Feel);
        Assert.Equal(2.5, Origin.DistanceKmTo(Mid(arrow)), 1);
        Assert.Equal(0, Origin.BearingTo(Mid(arrow)), 0);
        Assert.Equal(4, marks.Passes.Count);
        Assert.Equal(RideMarks.None, RideMarking.Along(Ride(back: false, FromNorth, _ => 0) with { Winds = [] }));
    }

    private static Coordinate Mid(WindArrow a) =>
        new((a.Tail.Latitude + a.Tip.Latitude) / 2, (a.Tail.Longitude + a.Tip.Longitude) / 2);
}
