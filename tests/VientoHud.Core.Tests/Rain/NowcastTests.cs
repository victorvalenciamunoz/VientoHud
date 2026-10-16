using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class NowcastTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly DateTimeOffset At6 = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
    private const int Size = 201;

    // 201 × 201 cells of 1 km; the point is the centre of the middle cell.
    private static readonly Coordinate Point = Grid().CenterOf(100, 100);

    // Discs of rain (5 mm/h): centre row and column, radius in km. No data where noData says.
    private static RainGrid Grid(
        (double Row, double Column, double Radius)[]? discs = null, Func<int, int, bool>? noData = null)
    {
        var rates = new float[Size * Size];
        for (var r = 0; r < Size; r++)
        {
            for (var c = 0; c < Size; c++)
            {
                if (noData?.Invoke(r, c) == true)
                {
                    rates[r * Size + c] = float.NaN;
                    continue;
                }
                foreach (var (row, column, radius) in discs ?? [])
                {
                    if ((r - row) * (r - row) + (c - column) * (c - column) <= radius * radius)
                    {
                        rates[r * Size + c] = 5;
                    }
                }
            }
        }
        return new RainGrid(At6, Opera, 0, 0, 1000, Size, Size, rates);
    }

    private static RainMotion East(double kmh) => new(At6.AddMinutes(-30), At6, kmh, 0, 0.8, 0.5);

    [Fact]
    public void Rain_coming_from_the_west_arrives_when_it_gets_here()
    {
        // A storm 30 km across whose eastern edge is 20 km west, moving east at 30 km/h: here in ~40 min.
        var grid = Grid([(100, 65, 15)]);

        var nowcast = Nowcast.AtPoint(grid, East(30), Point, At6);

        Assert.Equal(19, nowcast.Steps.Count);
        Assert.Equal(RainLikelihood.No, nowcast.Steps[0].Likelihood);
        Assert.InRange((nowcast.RainArrives!.Value - At6).TotalMinutes, 35, 45);
        Assert.Null(nowcast.RainStops);
        Assert.Equal(RainLikelihood.Likely, nowcast.At(At6.AddMinutes(60)).Likelihood);
    }

    [Fact]
    public void Rain_overhead_moving_away_stops()
    {
        // Overhead, 20 km across, moving east at 40 km/h: its western edge passes in ~15 min.
        var grid = Grid([(100, 100, 10)]);

        var nowcast = Nowcast.AtPoint(grid, East(40), Point, At6);

        Assert.Equal(RainLikelihood.Likely, nowcast.Steps[0].Likelihood);
        Assert.Equal(1, nowcast.Steps[0].Probability);
        Assert.Null(nowcast.RainArrives);
        Assert.InRange((nowcast.RainStops!.Value - At6).TotalMinutes, 15, 30);
    }

    [Fact]
    public void Rain_passing_to_the_north_never_comes()
    {
        var grid = Grid([(60, 60, 8)]);

        var nowcast = Nowcast.AtPoint(grid, East(40), Point, At6);

        Assert.All(nowcast.Steps, s => Assert.Equal(RainLikelihood.No, s.Likelihood));
        Assert.Null(nowcast.RainArrives);
    }

    [Fact]
    public void The_edge_of_the_rain_gives_a_middle_probability()
    {
        // The point sits right on the western edge of a wide area of rain.
        var grid = Grid([(100, 140, 40)]);

        var nowcast = Nowcast.AtPoint(grid, motion: null, Point, At6);

        Assert.InRange(nowcast.Steps[^1].Probability!.Value, 0.3, 0.7);
    }

    [Fact]
    public void Without_motion_the_rain_stays_where_it_is()
    {
        var grid = Grid([(100, 100, 10)]);

        var wet = Nowcast.AtPoint(grid, null, Point, At6);
        var dry = Nowcast.AtPoint(Grid([(100, 140, 10)]), null, Point, At6);

        Assert.All(wet.Steps, s => Assert.Equal(RainLikelihood.Likely, s.Likelihood));
        Assert.All(dry.Steps, s => Assert.Equal(RainLikelihood.No, s.Likelihood));
        Assert.Null(wet.Motion);
    }

    [Fact]
    public void Counts_from_now_not_from_the_frame()
    {
        // The frame is 10 min old: "in 30 min" is 40 min after the frame.
        var grid = Grid([(100, 65, 15)]);

        var fresh = Nowcast.AtPoint(grid, East(30), Point, At6);
        var old = Nowcast.AtPoint(grid, East(30), Point, At6.AddMinutes(10));

        Assert.Equal(At6.AddMinutes(10), old.IssuedAt);
        Assert.Equal(At6, old.FrameTime);
        Assert.Equal(At6.AddMinutes(10), old.Steps[0].Time);
        Assert.Equal(fresh.At(At6.AddMinutes(40)).Probability, old.At(At6.AddMinutes(40)).Probability);
        Assert.Equal(fresh.RainArrives, old.RainArrives);
    }

    [Fact]
    public void Rain_from_where_the_radar_has_no_data_is_unknown()
    {
        // Everything west of column 80 has no data; the rain over the point in 60 min comes from 30 km west.
        var grid = Grid(noData: (_, c) => c < 80);

        var nowcast = Nowcast.AtPoint(grid, East(30), Point, At6);

        Assert.Equal(0, nowcast.Steps[0].Probability);
        Assert.Null(nowcast.At(At6.AddMinutes(60)).Probability);
        Assert.Null(nowcast.At(At6.AddMinutes(60)).Likelihood);
    }

    [Fact]
    public void The_area_looked_at_grows_with_time()
    {
        // A small shower 10 km east (edge at 8 km) that does not move: out of the 2 km area at first, inside the
        // 9.5 km one at 90 min.
        var grid = Grid([(100, 110, 2)]);

        var nowcast = Nowcast.AtPoint(grid, null, Point, At6);

        Assert.Equal(0, nowcast.Steps[0].Probability);
        Assert.True(nowcast.Steps[^1].Probability > 0);
    }

    [Fact]
    public void Moves_the_whole_frame_ahead()
    {
        var grid = Grid([(100, 65, 5)]);

        var ahead = Nowcast.Advect(grid, East(30), At6.AddMinutes(60));

        Assert.Equal(At6.AddMinutes(60), ahead.Time);
        Assert.Equal(5f, ahead[100, 95]);
        Assert.Equal(0f, ahead[100, 65]);
        // The 30 western columns come from outside the frame.
        Assert.True(float.IsNaN(ahead[100, 10]));
        Assert.False(float.IsNaN(ahead[100, 40]));
    }

    [Fact]
    public void Rejects_a_now_before_the_frame_or_not_in_utc()
    {
        var grid = Grid();

        Assert.Throws<ArgumentException>(() => Nowcast.AtPoint(grid, null, Point, At6.AddMinutes(-5)));
        Assert.Throws<ArgumentException>(() => Nowcast.AtPoint(grid, null, Point, At6.ToOffset(TimeSpan.FromHours(2))));
    }

    [Theory]
    [InlineData(0, RainLikelihood.No)]
    [InlineData(0.19, RainLikelihood.No)]
    [InlineData(0.2, RainLikelihood.Possible)]
    [InlineData(0.5, RainLikelihood.Possible)]
    [InlineData(0.51, RainLikelihood.Likely)]
    [InlineData(1, RainLikelihood.Likely)]
    public void Words_follow_the_author_thresholds(double probability, RainLikelihood expected)
    {
        Assert.Equal(expected, RainLikelihoods.Classify(probability));
    }
}
