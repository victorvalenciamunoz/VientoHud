using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class WindStretchesTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // 1 km legs going north, each with its wind (speed, direction FROM, gust).
    private static IReadOnlyList<LegWind> North(params (double Speed, double From, double? Gust)[] winds) =>
        winds.Select((w, i) => new LegWind(
                new RouteLeg(i, i, 1, Leon, Leon.Destination(0, 1), Leon, 0, 1, null, Eight, Eight.AddMinutes(2.4)),
                new LocalWind(w.Speed, w.Gust, w.From, "model")))
            .ToList();

    private static (double, double, double?)[] Times(int n, double speed, double from, double? gust = null) =>
        Enumerable.Repeat((speed, from, gust), n).ToArray();

    [Fact]
    public void Names_how_the_wind_feels_on_each_stretch()
    {
        // Going north: from the north is in the face, from the south behind, from the east on the right.
        var stretches = WindStretches.Describe(North(
            [.. Times(6, 12, 0), .. Times(5, 10, 90, 30), .. Times(7, 8, 180), .. Times(5, 3, 270), .. Times(5, 9, 270)]));

        Assert.Equal(
            [
                new WindStretch(0, 6, WindFeel.Headwind, 12, null),
                new WindStretch(6, 11, WindFeel.FromRight, 10, 30),
                new WindStretch(11, 18, WindFeel.Tailwind, 8, null),
                new WindStretch(18, 23, WindFeel.Light, 3, null),
                new WindStretch(23, 28, WindFeel.FromLeft, 9, null),
            ],
            stretches.Select(s => s with { MeanKmh = Math.Round(s.MeanKmh, 6) }));
    }

    [Fact]
    public void Folds_short_stretches_into_the_longer_neighbour()
    {
        // 8 km in the face, 2 km from the right, 4 km behind: the 2 km join the 8; then the 4 behind are still
        // short and join them too.
        var stretches = WindStretches.Describe(North([.. Times(8, 12, 0), .. Times(2, 10, 90), .. Times(4, 8, 180)]));

        var only = Assert.Single(stretches);
        Assert.Equal((0, 14, WindFeel.Headwind), (only.StartKm, only.EndKm, only.Feel));
        // The average in the face over all of it: (8 × 12 + 2 × 0 + 4 × −8) / 14.
        Assert.Equal((8 * 12 - 4 * 8) / 14.0, only.MeanKmh, precision: 6);
    }

    [Fact]
    public void Joins_two_stretches_with_the_same_feel_once_the_gap_between_them_goes()
    {
        var stretches = WindStretches.Describe(North([.. Times(6, 12, 0), .. Times(1, 10, 180), .. Times(6, 12, 0)]));

        var only = Assert.Single(stretches);
        Assert.Equal((0, 13, WindFeel.Headwind), (only.StartKm, only.EndKm, only.Feel));
    }

    [Fact]
    public void Keeps_a_ride_shorter_than_the_minimum_as_one_stretch()
    {
        var stretches = WindStretches.Describe(North([.. Times(2, 12, 0), .. Times(1, 10, 180)]));

        Assert.Single(stretches);
    }

    [Fact]
    public void Describes_nothing_for_no_legs() => Assert.Empty(WindStretches.Describe([]));

    [Fact]
    public void Keeps_the_strongest_gust_across()
    {
        var stretches = WindStretches.Describe(North([(10, 90, 25), (10, 90, 38), (10, 90, 30), (10, 90, 20), (10, 90, null)]));

        Assert.Equal(38, Assert.Single(stretches).MaxCrossGustKmh);
    }
}
