using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class WindArrowsTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_arrow_points_where_the_wind_goes_with_a_swept_back_head()
    {
        // Wind from the west: the arrow points east, 3 km long, centred on León.
        var arrow = WindArrows.At(Leon, 270, 18, 3);

        Assert.Equal(90, Leon.BearingTo(arrow.Tip), 0);
        Assert.Equal(270, Leon.BearingTo(arrow.Tail), 0);
        Assert.Equal(1.5, Leon.DistanceKmTo(arrow.Tip), 2);
        Assert.Equal(3, arrow.Tail.DistanceKmTo(arrow.Tip), 2);
        // The arms go back from the tip at 150° to either side, 0,9 km long.
        Assert.Equal(0.9, arrow.Tip.DistanceKmTo(arrow.HeadLeft), 2);
        Assert.Equal(0.9, arrow.Tip.DistanceKmTo(arrow.HeadRight), 2);
        Assert.Equal(240, arrow.Tip.BearingTo(arrow.HeadLeft), 0);
        Assert.Equal(300, arrow.Tip.BearingTo(arrow.HeadRight), 0);
        Assert.Equal(18, arrow.SpeedKmh);
        Assert.Null(arrow.Feel);
    }

    [Fact]
    public void At_home_the_mean_wind_or_nothing_when_it_cancels_out()
    {
        var steady = new StartDirectionAdvice(StartAdviceKind.HeadIntoWind, 270, 270, 18, 18, 1, 3);
        var cancelled = new StartDirectionAdvice(StartAdviceKind.Variable, null, null, 0, 15, 0, 3);

        Assert.Equal(90, Leon.BearingTo(WindArrows.AtHome(steady, Leon)!.Tip), 0);
        Assert.Null(WindArrows.AtHome(cancelled, Leon));
    }

    [Fact]
    public void Along_a_ride_one_arrow_every_five_km_with_the_wind_of_the_leg_as_it_feels()
    {
        // 12 legs of 1 km heading north; wind from the north for the first 6 km, from the south after.
        var winds = Enumerable.Range(0, 12).Select(i => new LegWind(
                new RouteLeg(i, i, 1, Leon, Leon.Destination(0, 1), Leon.Destination(0, i + 0.5), 0, 1, null, Eight, Eight.AddMinutes(2.4)),
                new LocalWind(20, null, i < 6 ? 0 : 180, "fake")))
            .ToList();

        var arrows = WindArrows.AlongRide(winds);

        // At km 2,5 and 7,5: legs 2 and 7.
        Assert.Equal(2, arrows.Count);
        Assert.Equal(WindFeel.Headwind, arrows[0].Feel);
        Assert.Equal(WindFeel.Tailwind, arrows[1].Feel);
        // In the face from the north: the arrow points south; from behind: north.
        Assert.Equal(180, arrows[0].Tail.BearingTo(arrows[0].Tip), 0);
        Assert.Equal(0, arrows[1].Tail.BearingTo(arrows[1].Tip), 0);
        Assert.Equal(1.6, arrows[0].Tail.DistanceKmTo(arrows[0].Tip), 2);
        Assert.Equal(2.5, Leon.DistanceKmTo(Midpoint(arrows[0])), 1);
        Assert.Empty(WindArrows.AlongRide([]));
    }

    private static Coordinate Midpoint(WindArrow a) =>
        new((a.Tail.Latitude + a.Tip.Latitude) / 2, (a.Tail.Longitude + a.Tip.Longitude) / 2);
}
