using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class RainMotionTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly DateTimeOffset At6 = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
    private const int Size = 201;

    // A blob of rain: centre (in cells, fractional allowed), radius in cells and peak rate in mm/h.
    private sealed record Blob(double Row, double Column, double Radius, double Peak);

    // Several blobs of different sizes and strengths, so that only the right shift matches.
    private static readonly Blob[] Storm =
    [
        new(60, 70, 12, 20), new(90, 120, 8, 6), new(130, 60, 15, 3), new(110, 95, 5, 40), new(150, 140, 10, 2),
        new(45, 150, 6, 10),
    ];

    private static RainGrid Grid(
        DateTimeOffset time,
        IEnumerable<Blob> blobs,
        double moveEastCells = 0,
        double moveSouthCells = 0,
        Func<int, int, bool>? noData = null)
    {
        var rates = new float[Size * Size];
        foreach (var blob in blobs)
        {
            var centerRow = blob.Row + moveSouthCells;
            var centerColumn = blob.Column + moveEastCells;
            for (var r = 0; r < Size; r++)
            {
                for (var c = 0; c < Size; c++)
                {
                    var d2 = (r - centerRow) * (r - centerRow) + (c - centerColumn) * (c - centerColumn);
                    var rate = blob.Peak * Math.Exp(-d2 / (2 * blob.Radius * blob.Radius / 4));
                    rates[r * Size + c] = (float)Math.Max(rates[r * Size + c], rate < 0.05 ? 0 : rate);
                }
            }
        }
        if (noData is not null)
        {
            for (var r = 0; r < Size; r++)
            {
                for (var c = 0; c < Size; c++)
                {
                    if (noData(r, c))
                    {
                        rates[r * Size + c] = float.NaN;
                    }
                }
            }
        }
        return new RainGrid(time, Opera, 0, 0, 1000, Size, Size, rates);
    }

    private static RainMotion Motion(RainGrid earlier, RainGrid later, RainMotionOptions? options = null)
    {
        var estimate = RainMotions.Estimate(earlier, later, options);
        Assert.Null(estimate.Failure);
        return estimate.Motion!;
    }

    [Fact]
    public void Follows_rain_moving_a_whole_number_of_cells()
    {
        // 6 km east and 4 km north in 30 min: 12 km/h east, 8 km/h north.
        var motion = Motion(Grid(At6.AddMinutes(-30), Storm), Grid(At6, Storm, moveEastCells: 6, moveSouthCells: -4));

        Assert.Equal(12, motion.EastKmh, tolerance: 0.3);
        Assert.Equal(8, motion.NorthKmh, tolerance: 0.3);
        Assert.Equal(14.4, motion.SpeedKmh, tolerance: 0.4);
        Assert.Equal(56.3, motion.TowardsDegrees, tolerance: 2);
        Assert.Equal(At6.AddMinutes(-30), motion.From);
        Assert.Equal(At6, motion.To);
        Assert.True(motion.Correlation > 0.99);
        Assert.True(motion.StillCorrelation < motion.Correlation - 0.2);
    }

    [Theory]
    [InlineData(2.5, 0)]
    [InlineData(-1.5, 3.25)]
    [InlineData(0.4, -0.3)]
    public void Finds_fractions_of_a_cell(double east, double south)
    {
        var motion = Motion(Grid(At6.AddMinutes(-30), Storm), Grid(At6, Storm, east, south));

        // Within a quarter of a cell (0.5 km/h in 30 min).
        Assert.Equal(east * 2, motion.EastKmh, tolerance: 0.5);
        Assert.Equal(-south * 2, motion.NorthKmh, tolerance: 0.5);
    }

    [Fact]
    public void Follows_fast_rain_with_the_coarse_search()
    {
        // 35 km towards the south-west in 30 min: ~70 km/h.
        var motion = Motion(Grid(At6.AddMinutes(-30), Storm), Grid(At6, Storm, moveEastCells: -25, moveSouthCells: 25));

        Assert.Equal(-50, motion.EastKmh, tolerance: 1);
        Assert.Equal(-50, motion.NorthKmh, tolerance: 1);
        Assert.Equal(225, motion.TowardsDegrees, tolerance: 2);
    }

    [Fact]
    public void Still_rain_does_not_move()
    {
        var motion = Motion(Grid(At6.AddMinutes(-15), Storm), Grid(At6, Storm));

        Assert.True(motion.SpeedKmh < 0.5);
    }

    [Fact]
    public void Uses_the_time_between_frames()
    {
        // The same 6 km east is 24 km/h in 15 min.
        var motion = Motion(Grid(At6.AddMinutes(-15), Storm), Grid(At6, Storm, moveEastCells: 6));

        Assert.Equal(24, motion.EastKmh, tolerance: 0.5);
    }

    [Fact]
    public void Leaves_out_cells_without_data()
    {
        // The western quarter of the earlier frame and a band of the later one have no data.
        var motion = Motion(
            Grid(At6.AddMinutes(-30), Storm, noData: (_, c) => c < 50),
            Grid(At6, Storm, moveEastCells: 5, moveSouthCells: 5, noData: (r, _) => r is > 170 and < 185));

        Assert.Equal(10, motion.EastKmh, tolerance: 0.5);
        Assert.Equal(-10, motion.NorthKmh, tolerance: 0.5);
    }

    [Fact]
    public void What_counts_is_the_shape_not_the_amount()
    {
        // The same rain, moved and three times stronger: still a near-perfect match.
        var stronger = Storm.Select(b => b with { Peak = b.Peak * 3 });

        var motion = Motion(Grid(At6.AddMinutes(-30), Storm), Grid(At6, stronger, moveEastCells: 6));

        Assert.Equal(12, motion.EastKmh, tolerance: 0.5);
        Assert.True(motion.Correlation > 0.9);
    }

    [Fact]
    public void Too_little_rain_gives_no_motion()
    {
        Blob[] shower = [new(100, 100, 3, 5)];

        var estimate = RainMotions.Estimate(Grid(At6.AddMinutes(-30), shower), Grid(At6, shower, moveEastCells: 4));

        Assert.Null(estimate.Motion);
        Assert.Equal(RainMotionFailure.TooLittleRain, estimate.Failure);
    }

    [Fact]
    public void Unrelated_frames_give_no_clear_match()
    {
        Blob[] other = [new(30, 170, 10, 5), new(170, 30, 14, 12), new(170, 170, 6, 30)];

        var estimate = RainMotions.Estimate(Grid(At6.AddMinutes(-30), Storm), Grid(At6, other));

        // Whatever matches best is weak or at the edge of the search.
        Assert.Null(estimate.Motion);
        Assert.True(estimate.Failure is RainMotionFailure.NoClearMatch or RainMotionFailure.TooFast);
    }

    [Fact]
    public void Rain_faster_than_the_limit_is_not_followed()
    {
        // 20 km in 30 min with a limit of 20 km/h (10 cells).
        var estimate = RainMotions.Estimate(
            Grid(At6.AddMinutes(-30), Storm), Grid(At6, Storm, moveEastCells: 20),
            new RainMotionOptions { MaxSpeedKmh = 20 });

        Assert.Null(estimate.Motion);
        Assert.NotNull(estimate.Failure);
    }

    [Fact]
    public void Four_frames_ten_minutes_apart_give_six_pairs_and_their_median()
    {
        // 2 cells east every 10 min: 12 km/h.
        var frames = Enumerable.Range(0, 4)
            .Select(i => Grid(At6.AddMinutes(-30 + 10 * i), Storm, moveEastCells: 2 * i))
            .Reverse()
            .ToList();

        var combined = RainMotions.Combine(frames);

        Assert.Null(combined.Failure);
        Assert.Equal(6, combined.PairsTried);
        Assert.Equal(6, combined.PairsWithMotion);
        Assert.Equal(12, combined.Motion!.EastKmh, tolerance: 0.5);
        Assert.Equal(0, combined.Motion.NorthKmh, tolerance: 0.5);
        Assert.True(combined.SpreadKmh < 1);
        Assert.Equal(At6.AddMinutes(-30), combined.Motion.From);
        Assert.Equal(At6, combined.Motion.To);
    }

    [Fact]
    public void One_odd_pair_does_not_move_the_median()
    {
        // The rain moves 2 cells east every 10 min, but the last frame jumps 6 cells south instead.
        RainGrid[] frames =
        [
            Grid(At6.AddMinutes(-30), Storm),
            Grid(At6.AddMinutes(-20), Storm, moveEastCells: 2),
            Grid(At6.AddMinutes(-10), Storm, moveEastCells: 4),
            Grid(At6, Storm, moveEastCells: 4, moveSouthCells: 6),
        ];

        var combined = RainMotions.Combine(frames);

        // Three of the six pairs involve the odd frame; the median stays nearer the true motion than the odd one.
        Assert.NotNull(combined.Motion);
        Assert.Equal(12, combined.Motion!.EastKmh, tolerance: 4);
        Assert.True(combined.Motion.NorthKmh > -12);
    }

    [Fact]
    public void Pairs_that_disagree_give_no_motion()
    {
        // Back and forth: east, then west, then far north.
        RainGrid[] frames =
        [
            Grid(At6.AddMinutes(-30), Storm),
            Grid(At6.AddMinutes(-20), Storm, moveEastCells: 8),
            Grid(At6.AddMinutes(-10), Storm, moveEastCells: -8),
            Grid(At6, Storm, moveSouthCells: -12),
        ];

        var combined = RainMotions.Combine(frames);

        Assert.Null(combined.Motion);
        Assert.Equal(RainMotionFailure.PairsDisagree, combined.Failure);
        Assert.True(combined.SpreadKmh > 10);
    }

    [Fact]
    public void Without_enough_pairs_the_most_common_reason_is_given()
    {
        Blob[] shower = [new(100, 100, 3, 5)];
        RainGrid[] frames = [Grid(At6.AddMinutes(-20), shower), Grid(At6.AddMinutes(-10), shower), Grid(At6, shower)];

        var combined = RainMotions.Combine(frames);

        Assert.Null(combined.Motion);
        Assert.Equal(RainMotionFailure.TooLittleRain, combined.Failure);
        Assert.Equal(3, combined.PairsTried);
        Assert.Equal(0, combined.PairsWithMotion);
    }

    [Fact]
    public void Frames_closer_than_ten_minutes_are_not_paired()
    {
        RainGrid[] close = [Grid(At6.AddMinutes(-5), Storm), Grid(At6, Storm)];

        Assert.Throws<ArgumentException>(() => RainMotions.Combine(close));
        Assert.Throws<ArgumentException>(() => RainMotions.Combine([Grid(At6, Storm)]));
    }

    [Fact]
    public void Similarity_is_highest_at_the_true_shift()
    {
        var earlier = Grid(At6.AddMinutes(-30), Storm);
        var later = Grid(At6, Storm, moveEastCells: 6, moveSouthCells: -4);

        var right = RainMotions.Similarity(earlier, later, eastKm: 6, northKm: 4);
        var still = RainMotions.Similarity(earlier, later, 0, 0);
        var wrong = RainMotions.Similarity(earlier, later, eastKm: -6, northKm: -4);

        Assert.True(right > 0.99);
        Assert.True(still < right && wrong < still);
    }

    [Fact]
    public void Almost_still_below_three_km_per_hour()
    {
        Assert.True(new RainMotion(At6, At6, 2, 2, 1, 1).IsAlmostStill);
        Assert.False(new RainMotion(At6, At6, 3, 0, 1, 1).IsAlmostStill);
    }

    [Fact]
    public void Frames_must_share_the_grid_and_come_in_order()
    {
        var earlier = Grid(At6.AddMinutes(-30), Storm);
        var later = Grid(At6, Storm);
        var shifted = new RainGrid(At6, Opera, 1000, 0, 1000, Size, Size, new float[Size * Size]);

        Assert.Throws<ArgumentException>(() => RainMotions.Estimate(earlier, shifted));
        Assert.Throws<ArgumentException>(() => RainMotions.Estimate(later, earlier));
        Assert.Throws<ArgumentException>(() => RainMotions.Estimate(Grid(At6.AddHours(-3), Storm), later));
    }

    [Fact]
    public void Follows_the_real_rain_over_madrid_on_october_3()
    {
        // OPERA rain rate at 5:30 and 6:00 UTC: showers over Madrid. Every pair of frames that morning (5:30 to 6:15,
        // both products) gives 14-25 km/h towards 22-34° (NNE): the band moves north-north-east.
        static RainGrid Read(string name)
        {
            using var stream = File.OpenRead(Fixture.FullPath($"Opera/{name}"));
            return OdimComposite.ReadAround(stream, new Coordinate(40.4168, -3.7038), 150);
        }

        var motion = Motion(Read("OPERA@20261003T0530@0@RATE.h5"), Read("OPERA@20261003T0600@0@RATE.h5"));

        Assert.InRange(motion.SpeedKmh, 12, 26);
        Assert.InRange(motion.TowardsDegrees, 15, 45);
        Assert.True(motion.Correlation > motion.StillCorrelation + 0.15);
    }
}
