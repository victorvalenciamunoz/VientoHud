using VientoHud.Core.Rain;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class RadarFramesTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset At6 = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    private static RainGrid Frame(DateTimeOffset time) => new(time, Opera, 0, 0, 1000, 1, 1, [0]);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 5)]
    [InlineData(59, 55)]
    public void Rounds_down_to_the_frame_time(int minutes, int expectedMinutes)
    {
        var time = At6.AddMinutes(minutes).AddSeconds(minutes == 4 ? 59 : 0);

        Assert.Equal(At6.AddMinutes(expectedMinutes), RadarFrames.AtOrBefore(time, FiveMinutes));
    }

    [Fact]
    public void Rounds_to_quarter_hours_for_the_rain_rate_product()
    {
        Assert.Equal(At6.AddMinutes(45), RadarFrames.AtOrBefore(At6.AddMinutes(52), TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Rejects_local_times()
    {
        Assert.Throws<ArgumentException>(() => RadarFrames.AtOrBefore(At6.ToOffset(TimeSpan.FromHours(2)), FiveMinutes));
    }

    [Fact]
    public async Task Takes_the_frame_at_or_before_the_time()
    {
        var source = new FakeRadarSource(FiveMinutes, Frame(At6), Frame(At6.AddMinutes(5)));

        var grid = await source.GetFrameAtOrBeforeAsync(At6.AddMinutes(8), Madrid, 50);

        Assert.Equal(At6.AddMinutes(5), grid!.Time);
        Assert.Equal([At6.AddMinutes(5)], source.Requested);
    }

    [Fact]
    public async Task Steps_back_over_missing_frames()
    {
        var source = new FakeRadarSource(FiveMinutes, Frame(At6));

        var grid = await source.GetFrameAtOrBeforeAsync(At6.AddMinutes(10), Madrid, 50);

        Assert.Equal(At6, grid!.Time);
        Assert.Equal([At6.AddMinutes(10), At6.AddMinutes(5), At6], source.Requested);
    }

    [Fact]
    public async Task Motion_frames_are_the_three_every_ten_minutes_before_the_latest()
    {
        var latest = Frame(At6.AddMinutes(30));
        var source = new FakeRadarSource(FiveMinutes, Frame(At6), Frame(At6.AddMinutes(10)), latest);

        var frames = await source.GetMotionFramesAsync(latest, Madrid, 50);

        // 6:20 is missing: left out.
        Assert.Equal([At6, At6.AddMinutes(10), At6.AddMinutes(30)], frames.Select(f => f.Time));
        Assert.Equal([At6, At6.AddMinutes(10), At6.AddMinutes(20)], source.Requested);
    }

    [Fact]
    public async Task Gives_up_after_the_last_step_back()
    {
        var source = new FakeRadarSource(FiveMinutes, Frame(At6));

        var grid = await source.GetFrameAtOrBeforeAsync(At6.AddMinutes(30), Madrid, 50, maxStepsBack: 3);

        Assert.Null(grid);
        Assert.Equal(4, source.Requested.Count);
    }
}
