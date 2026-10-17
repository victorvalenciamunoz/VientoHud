using System.Net;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class ModelRainFieldTests
{
    private static readonly Coordinate Barajas = new(40.4936, -3.5668);
    private static readonly DateTimeOffset At12 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    // One point with the given rates (mm/h) every 15 min, the first interval ending at `firstEnd`.
    private static RainForecastSeries Point(Coordinate location, DateTimeOffset firstEnd, params double[] rates) =>
        new(location, location, "test", Quarter,
            rates.Select((rate, i) => new RainForecastSample(firstEnd + i * Quarter, rate)).ToList());

    [Fact]
    public void Grid_covers_the_radius_north_to_south_and_west_to_east()
    {
        var grid = ModelRainField.GridAround(Barajas, 10, 5);

        Assert.Equal(25, grid.Count);
        Assert.True(Barajas.DistanceKmTo(grid[12]) < 0.001);
        Assert.True(grid[0].Latitude > Barajas.Latitude && grid[0].Longitude < Barajas.Longitude);
        Assert.True(grid[24].Latitude < Barajas.Latitude && grid[24].Longitude > Barajas.Longitude);
        Assert.Equal(5, grid[12].DistanceKmTo(grid[13]), tolerance: 0.05);
        Assert.Equal(5, grid[12].DistanceKmTo(grid[7]), tolerance: 0.05);
        Assert.Single(ModelRainField.GridAround(Barajas, 0, 5));
    }

    [Fact]
    public void Chance_is_the_share_of_wet_points_and_intervals_around()
    {
        // Four points within 15 km: two wet at 12:15 only, two always dry. The interval of 12:10 is the one ending 12:15.
        var near = Barajas.Destination(90, 3);
        var field = new ModelRainField(
        [
            Point(Barajas, At12.AddMinutes(15), 1, 0, 0),
            Point(near, At12.AddMinutes(15), 2, 0, 0),
            Point(Barajas.Destination(0, 5), At12.AddMinutes(15), 0, 0, 0),
            Point(Barajas.Destination(180, 5), At12.AddMinutes(15), 0, 0, 0),
            Point(Barajas.Destination(270, 40), At12.AddMinutes(15), 9, 9, 9), // far: left out
        ]);
        var exact = new ModelRainOptions { TimeMargin = TimeSpan.Zero };

        Assert.Equal(0.5, field.Probability(Barajas, At12.AddMinutes(10), exact));
        Assert.Equal(0, field.Probability(Barajas, At12.AddMinutes(30), exact));
        // With 15 min each way at 12:30: intervals ending 12:15, 12:30, 12:45 → 2 wet out of 12.
        Assert.Equal(2.0 / 12, field.Probability(Barajas, At12.AddMinutes(30), new ModelRainOptions { TimeMargin = Quarter })!.Value, 9);
    }

    [Fact]
    public void Outside_the_grid_or_beyond_the_forecast_is_unknown()
    {
        var field = new ModelRainField([Point(Barajas, At12.AddMinutes(15), 1, 1)]);

        Assert.Equal(At12.AddMinutes(30), field.LastTime);
        Assert.Null(field.Probability(new Coordinate(42.6, -5.57), At12.AddMinutes(15)));
        Assert.Null(field.Probability(Barajas, At12.AddHours(3)));
        Assert.Throws<ArgumentException>(() => field.Probability(Barajas, At12.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Rates_below_the_threshold_are_dry()
    {
        var field = new ModelRainField([Point(Barajas, At12.AddMinutes(15), 0.1, 0.4)]);
        var exact = new ModelRainOptions { TimeMargin = TimeSpan.Zero };

        Assert.Equal(0, field.Probability(Barajas, At12.AddMinutes(15), exact));
        Assert.Equal(1, field.Probability(Barajas, At12.AddMinutes(30), exact));
    }

    [Fact]
    public async Task Reads_one_past_run_on_a_grid_and_gives_its_chance_of_rain()
    {
        // Recorded 2026-10-17: AROME HD 15 min, run of 6 Oct 12:00 UTC, 3 × 3 points every 5 km around Barajas. It put
        // showers over Barajas 13:00-13:45 and 15:00-15:45 (the real storm came at 14:06 and 16:00-17:15).
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            Fixture.Read("OpenMeteo/single-run-arome-hd-15min-barajas-3x3-2026-10-06T12.json"));
        var source = new OpenMeteoRainForecastSource(
            new HttpClient(handler), OpenMeteoRainOptions.SingleRun(At12));
        var grid = ModelRainField.GridAround(Barajas, 5, 5);

        var series = await source.GetAsync(grid, At12, At12.AddHours(8));
        var field = new ModelRainField(series);

        var query = Uri.UnescapeDataString(Assert.Single(handler.Requests).Query);
        Assert.StartsWith("https://single-runs-api.open-meteo.com/", handler.Requests[0].ToString());
        Assert.Contains("run=2026-10-06T12:00", query);
        Assert.DoesNotContain("start_date", query);
        Assert.Equal(9, series.Count);
        // The run starts at 12:00 and lasts 6 hours: intervals ending 12:15 to 18:00.
        Assert.Equal(At12.AddMinutes(15), series[4].Samples[0].End);
        Assert.Equal(At12.AddHours(6), field.LastTime);
        // Over Barajas, 13:15-13:30: 0.3 mm → 1.2 mm/h.
        Assert.Equal(1.2, series[4].RateAt(At12.AddMinutes(90))!.Value, tolerance: 1e-9);

        Assert.True(field.Probability(Barajas, At12.AddMinutes(90)) > 0.3);
        Assert.True(field.Probability(Barajas, At12.AddHours(5)) < 0.1);
    }

    [Fact]
    public async Task Reads_the_latest_forecast_with_dates()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            Fixture.Read("OpenMeteo/latest-arome-hd-15min-barajas-3x3-2026-10-17.json"));
        var source = new OpenMeteoRainForecastSource(new HttpClient(handler), OpenMeteoRainOptions.Latest);
        var day = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        var series = await source.GetAsync(ModelRainField.GridAround(Barajas, 5, 5), day, day.AddDays(1));

        Assert.StartsWith("https://api.open-meteo.com/", handler.Requests[0].ToString());
        Assert.Contains("start_date=2026-10-17&end_date=2026-10-18", Uri.UnescapeDataString(handler.Requests[0].Query));
        Assert.Equal(9, series.Count);
        Assert.All(series, s => Assert.NotEmpty(s.Samples));
    }

    [Fact]
    public async Task Reads_the_hourly_chance_of_rain_as_a_fallback()
    {
        // Recorded 2026-10-17: best_match precipitation_probability at Barajas (from the ICON ensembles), in %.
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            Fixture.Read("OpenMeteo/latest-probability-best-match-barajas-2026-10-17.json"));
        var source = new OpenMeteoRainForecastSource(new HttpClient(handler), OpenMeteoRainOptions.Latest);
        var day = new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        var series = (await source.GetProbabilityAsync([Barajas], day, day.AddDays(2))).Single();

        Assert.Contains("hourly=precipitation_probability&models=best_match", Uri.UnescapeDataString(handler.Requests[0].Query));
        Assert.Equal(TimeSpan.FromHours(1), series.Interval);
        Assert.Equal("best_match", series.Model);
        // Values are stamped at the hour; 12:00 is the 13th value (53 %).
        Assert.Equal(0.53, series.ProbabilityAt(day.AddHours(12))!.Value, tolerance: 1e-9);
        Assert.Equal(0.53, series.ProbabilityAt(day.AddHours(11).AddMinutes(30))!.Value, tolerance: 1e-9);
        Assert.All(series.Samples, s => Assert.InRange(s.Probability, 0, 1));
    }
}
