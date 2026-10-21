using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Routing;

public class RouteMarksTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    [Fact]
    public void One_mark_every_few_km_heading_along_the_track()
    {
        // 10 km east in two 5 km pieces, then 10 km north.
        var route = new Route(
            [new RoutePoint(Leon), new RoutePoint(Leon.Destination(90, 5)), new RoutePoint(Leon.Destination(90, 10)),
             new RoutePoint(Leon.Destination(90, 10).Destination(0, 10))],
            [], 20, null, "fake");

        var marks = RouteMarks.Along(route);

        // At km 1,25, 3,75, 6,25, 8,75 (east) and 11,25, 13,75, 16,25, 18,75 (north).
        Assert.Equal([1.25, 3.75, 6.25, 8.75, 11.25, 13.75, 16.25, 18.75], marks.Select(m => m.AtKm));
        Assert.All(marks.Take(4), m => Assert.Equal(90, m.BearingDegrees, 0));
        Assert.All(marks.Skip(4), m => Assert.Equal(0, m.BearingDegrees, 0));
        Assert.Equal(1.25, Leon.DistanceKmTo(marks[0].Position), 2);
        Assert.Equal(6.25, Leon.DistanceKmTo(marks[2].Position), 2);
        // Ridden the other way round, the marks point the other way.
        var back = RouteMarks.Along(route.Reversed());
        Assert.Equal(8, back.Count);
        Assert.Equal(180, back[0].BearingDegrees, 0);
        Assert.Equal(270, back[^1].BearingDegrees, 0);
    }

    [Fact]
    public void A_short_route_gets_no_mark_and_the_spacing_must_be_positive()
    {
        var route = new Route([new RoutePoint(Leon), new RoutePoint(Leon.Destination(90, 1))], [], 1, null, "fake");

        Assert.Empty(RouteMarks.Along(route));
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteMarks.Along(route, 0));
    }
}
