using VientoHud.Core.Tests.Support;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class DirectionFanTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;

    [Fact]
    public void A_sector_into_the_wind_as_wide_as_the_planner_looks_and_as_far_as_an_out_and_back_goes()
    {
        var wind = new StartDirectionAdvice(StartAdviceKind.HeadIntoWind, 270, 270, 18, 18, 1, 3);

        var ring = DirectionFan.For(wind, Origin, 60)!;

        // Start, 25 points on the arc, start again.
        Assert.Equal(DirectionFan.ArcPoints + 3, ring.Count);
        Assert.Equal(Origin, ring[0]);
        Assert.Equal(Origin, ring[^1]);
        // 60 km: the far point is at 60 / 2 / 1.3 ≈ 23 km (DECISIONS 040).
        Assert.Equal(23.08, DirectionFan.ReachKm(60), 2);
        Assert.All(ring.Skip(1).Take(DirectionFan.ArcPoints + 1), p => Assert.Equal(23.08, Origin.DistanceKmTo(p), 1));
        // From 225° to 315° round 270°.
        Assert.Equal(225, Origin.BearingTo(ring[1]), 0);
        Assert.Equal(270, Origin.BearingTo(ring[1 + DirectionFan.ArcPoints / 2]), 0);
        Assert.Equal(315, Origin.BearingTo(ring[1 + DirectionFan.ArcPoints]), 0);
    }

    [Fact]
    public void Round_north_the_bearings_stay_in_range()
    {
        var wind = new StartDirectionAdvice(StartAdviceKind.HeadIntoWind, 10, 10, 18, 18, 1, 3);

        var ring = DirectionFan.For(wind, Origin, 60)!;

        Assert.Equal(325, Origin.BearingTo(ring[1]), 0);
        Assert.Equal(55, Origin.BearingTo(ring[1 + DirectionFan.ArcPoints]), 0);
    }

    [Fact]
    public void No_sector_when_the_wind_gives_no_direction()
    {
        Assert.Null(DirectionFan.For(new StartDirectionAdvice(StartAdviceKind.Calm, null, 90, 2, 3, 0.7, 3), Origin, 60));
        Assert.Null(DirectionFan.For(new StartDirectionAdvice(StartAdviceKind.Variable, null, 90, 5, 15, 0.3, 3), Origin, 60));
    }
}
