using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class OdimCompositeTests
{
    private const string Reflectivity = "Opera/OPERA@20261003T0600@0@DBZH.h5";
    private const string RainRate = "Opera/OPERA@20261003T0600@0@RATE.h5";

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly Coordinate Barajas = new(40.4936, -3.5668);
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    // Reading the 1 km window takes a few seconds: once for the whole class.
    private static readonly Lazy<RainGrid> MadridReflectivity = new(() => Read(Reflectivity, Madrid, 150));

    private static RainGrid Read(string fixture, Coordinate center, double radiusKm)
    {
        using var stream = File.OpenRead(Fixture.FullPath(fixture));
        return OdimComposite.ReadAround(stream, center, radiusKm);
    }

    private static IEnumerable<float> Cells(RainGrid grid) =>
        from row in Enumerable.Range(0, grid.Rows)
        from column in Enumerable.Range(0, grid.Columns)
        select grid[row, column];

    [Fact]
    public void Cuts_a_square_of_1_km_cells_around_the_point()
    {
        var grid = MadridReflectivity.Value;

        Assert.Equal(Time, grid.Time);
        Assert.Equal(1000, grid.CellSizeMeters);
        Assert.Equal(301, grid.Rows);
        Assert.Equal(301, grid.Columns);
        Assert.Equal((150, 150), grid.CellOf(Madrid));
        Assert.True(grid.CenterOf(150, 150).DistanceKmTo(Madrid) < 0.75);
    }

    [Fact]
    public void Shows_the_rain_barajas_reported()
    {
        // METAR LEMD 03/10 06:00 UTC: SHRA (shower of moderate rain).
        var grid = MadridReflectivity.Value;

        Assert.InRange(grid.RateAt(Barajas)!.Value, 0.5f, 3f);
    }

    [Fact]
    public void Madrid_is_fully_covered_with_dry_and_wet_cells()
    {
        var cells = Cells(MadridReflectivity.Value).ToList();

        Assert.DoesNotContain(cells, float.IsNaN);
        var wet = cells.Count(r => r > 0) / (double)cells.Count;
        Assert.InRange(wet, 0.15, 0.40);
        Assert.InRange(cells.Max(), 20f, 100f);
    }

    [Fact]
    public void Reads_the_rain_rate_product_on_2_km_cells()
    {
        var grid = Read(RainRate, Madrid, 150);

        Assert.Equal(Time, grid.Time);
        Assert.Equal(2000, grid.CellSizeMeters);
        Assert.Equal(151, grid.Rows);
        Assert.Equal((75, 75), grid.CellOf(Madrid));
        Assert.True(grid.RateAt(Barajas) > 0);
        Assert.DoesNotContain(Cells(grid), float.IsNaN);
    }

    [Fact]
    public void Cells_beyond_the_edge_of_the_composite_have_no_data()
    {
        // 31.75° N 10.43° W is the south-west corner of the OPERA grid: three quarters of the square fall outside.
        var grid = Read(RainRate, new Coordinate(31.7462, -10.4346), 50);

        Assert.Equal(51, grid.Rows);
        Assert.True(float.IsNaN(grid[50, 0]));
        Assert.True(float.IsNaN(grid[0, 0]));
        Assert.True(Cells(grid).Count(float.IsNaN) >= 51 * 51 * 3 / 4);
    }

    [Fact]
    public void A_point_far_outside_gives_a_square_with_no_data()
    {
        var grid = Read(RainRate, new Coordinate(40.71, -74.0), 20);

        Assert.Equal(21, grid.Rows);
        Assert.All(Cells(grid), r => Assert.True(float.IsNaN(r)));
    }

    [Fact]
    public void A_file_that_is_not_hdf5_is_a_radar_error()
    {
        using var stream = File.OpenRead(Fixture.FullPath("Opera/missing-key.xml"));

        Assert.Throws<RadarException>(() => OdimComposite.ReadAround(stream, Madrid, 10));
    }

    [Fact]
    public void Rejects_a_radius_that_is_not_positive()
    {
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(() => OdimComposite.ReadAround(stream, Madrid, 0));
    }
}
