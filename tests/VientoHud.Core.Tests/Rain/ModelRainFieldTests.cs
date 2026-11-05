using System.Net;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public partial class ModelRainFieldTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset At12 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

    // One point with the given rates (mm/h) every 15 min, the first interval ending at `firstEnd`.
    private static RainForecastSeries Point(Coordinate location, DateTimeOffset firstEnd, params double[] rates) =>
        new(location, location, "test", Quarter,
            rates.Select((rate, i) => new RainForecastSample(firstEnd + i * Quarter, rate)).ToList());

    [Fact]
    public void Grid_covers_the_radius_north_to_south_and_west_to_east()
    {
        var grid = ModelRainField.GridAround(Origin, 10, 5);

        Assert.Equal(25, grid.Count);
        Assert.True(Origin.DistanceKmTo(grid[12]) < 0.001);
        Assert.True(grid[0].Latitude > Origin.Latitude && grid[0].Longitude < Origin.Longitude);
        Assert.True(grid[24].Latitude < Origin.Latitude && grid[24].Longitude > Origin.Longitude);
        Assert.Equal(5, grid[12].DistanceKmTo(grid[13]), tolerance: 0.05);
        Assert.Equal(5, grid[12].DistanceKmTo(grid[7]), tolerance: 0.05);
        Assert.Single(ModelRainField.GridAround(Origin, 0, 5));
    }

    [Fact]
    public void Chance_is_the_share_of_wet_points_and_intervals_around()
    {
        // Four points within 15 km: two wet at 12:15 only, two always dry. The interval of 12:10 is the one ending 12:15.
        var near = Origin.Destination(90, 3);
        var field = new ModelRainField(
        [
            Point(Origin, At12.AddMinutes(15), 1, 0, 0),
            Point(near, At12.AddMinutes(15), 2, 0, 0),
            Point(Origin.Destination(0, 5), At12.AddMinutes(15), 0, 0, 0),
            Point(Origin.Destination(180, 5), At12.AddMinutes(15), 0, 0, 0),
            Point(Origin.Destination(270, 40), At12.AddMinutes(15), 9, 9, 9), // far: left out
        ]);
        var exact = new ModelRainOptions { TimeMargin = TimeSpan.Zero };

        Assert.Equal(0.5, field.Probability(Origin, At12.AddMinutes(10), exact));
        Assert.Equal(0, field.Probability(Origin, At12.AddMinutes(30), exact));
        // With 15 min each way at 12:30: intervals ending 12:15, 12:30, 12:45 → 2 wet out of 12.
        Assert.Equal(2.0 / 12, field.Probability(Origin, At12.AddMinutes(30), new ModelRainOptions { TimeMargin = Quarter })!.Value, 9);
    }

    [Fact]
    public void Outside_the_grid_or_beyond_the_forecast_is_unknown()
    {
        var field = new ModelRainField([Point(Origin, At12.AddMinutes(15), 1, 1)]);

        Assert.Equal(At12.AddMinutes(30), field.LastTime);
        Assert.Null(field.Probability(Origin.Destination(330, 290), At12.AddMinutes(15)));
        Assert.Null(field.Probability(Origin, At12.AddHours(3)));
        Assert.Throws<ArgumentException>(() => field.Probability(Origin, At12.ToOffset(TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Rates_below_the_threshold_are_dry()
    {
        var field = new ModelRainField([Point(Origin, At12.AddMinutes(15), 0.1, 0.4)]);
        var exact = new ModelRainOptions { TimeMargin = TimeSpan.Zero };

        Assert.Equal(0, field.Probability(Origin, At12.AddMinutes(15), exact));
        Assert.Equal(1, field.Probability(Origin, At12.AddMinutes(30), exact));
    }
}
