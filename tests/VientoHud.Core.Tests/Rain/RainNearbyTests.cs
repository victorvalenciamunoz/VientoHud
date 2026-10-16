using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class RainNearbyTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    // 21 × 21 cells of 1 km with Madrid in the middle (row 10, column 10); every cell dry unless set.
    private static RainGrid Grid(params (int Row, int Column, float Rate)[] cells)
    {
        var (x, y) = Opera.Forward(Madrid);
        var left = Math.Floor(x / 1000) * 1000 - 10_000;
        var top = Math.Ceiling(y / 1000) * 1000 + 10_000;
        var rates = new float[21 * 21];
        foreach (var (row, column, rate) in cells)
        {
            rates[row * 21 + column] = rate;
        }
        return new RainGrid(Time, Opera, left, top, 1000, 21, 21, rates);
    }

    [Fact]
    public void All_dry()
    {
        var rain = RainNearby.Describe(Grid(), Madrid);

        Assert.Equal(Time, rain.Time);
        Assert.Equal(0f, rain.RateAtPoint);
        Assert.False(rain.RainsAtPoint);
        Assert.Equal(1, rain.CoveredFraction);
        Assert.Equal(0, rain.WetFraction);
        Assert.Null(rain.Nearest);
        Assert.Null(rain.Strongest);
    }

    [Fact]
    public void Finds_the_nearest_and_the_strongest_rain_with_distance_and_direction()
    {
        // 3 km east (light) and 8 km north (heavy).
        var rain = RainNearby.Describe(Grid((10, 13, 0.8f), (2, 10, 20f)), Madrid);

        Assert.False(rain.RainsAtPoint);
        Assert.Equal(3, rain.Nearest!.DistanceKm, tolerance: 0.6);
        Assert.Equal(90, rain.Nearest.BearingDegrees, tolerance: 15);
        Assert.Equal(0.8f, rain.Nearest.RateMmPerHour);
        Assert.Equal(8, rain.Strongest!.DistanceKm, tolerance: 0.6);
        Assert.True(rain.Strongest.BearingDegrees is < 10 or > 350);
        Assert.Equal(20f, rain.Strongest.RateMmPerHour);
        Assert.Equal(2.0 / (21 * 21), rain.WetFraction, tolerance: 1e-9);
    }

    [Fact]
    public void Rain_at_the_point_is_the_nearest_at_distance_zero()
    {
        var rain = RainNearby.Describe(Grid((10, 10, 1.5f), (10, 11, 3f)), Madrid);

        Assert.True(rain.RainsAtPoint);
        Assert.Equal(1.5f, rain.RateAtPoint);
        Assert.Equal(0, rain.Nearest!.DistanceKm);
        Assert.Equal(3f, rain.Strongest!.RateMmPerHour);
    }

    [Fact]
    public void Echoes_below_the_threshold_are_not_rain()
    {
        // 0.15 mm/h is below 12 dBZ (≈ 0.205 mm/h), the weakest echo AEMET draws.
        var rain = RainNearby.Describe(Grid((10, 10, 0.15f), (5, 5, 0.15f)), Madrid);

        Assert.Equal(0.205, RainNearby.DefaultThresholdMmPerHour, tolerance: 0.001);
        Assert.False(rain.RainsAtPoint);
        Assert.Null(rain.Nearest);
        Assert.Equal(0, rain.WetFraction);

        var lower = RainNearby.Describe(Grid((10, 10, 0.15f)), Madrid, thresholdMmPerHour: 0.1);
        Assert.True(lower.RainsAtPoint);
    }

    [Fact]
    public void Cells_without_data_do_not_count()
    {
        var rain = RainNearby.Describe(Grid((10, 10, float.NaN), (0, 0, float.NaN), (0, 1, 5f)), Madrid);

        Assert.True(float.IsNaN(rain.RateAtPoint));
        Assert.False(rain.RainsAtPoint);
        Assert.Equal(1 - 2.0 / (21 * 21), rain.CoveredFraction, tolerance: 1e-9);
        Assert.Equal(1.0 / (21 * 21 - 2), rain.WetFraction, tolerance: 1e-9);
    }

    [Fact]
    public void The_point_must_be_inside_the_grid()
    {
        Assert.Throws<ArgumentException>(() => RainNearby.Describe(Grid(), new Coordinate(42.5987, -5.5671)));
        Assert.Throws<ArgumentOutOfRangeException>(() => RainNearby.Describe(Grid(), Madrid, thresholdMmPerHour: 0));
    }
}
