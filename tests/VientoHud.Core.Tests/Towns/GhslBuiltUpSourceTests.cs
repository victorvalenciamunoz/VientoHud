using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Towns;
using VientoHud.Core.Towns.Ghsl;

namespace VientoHud.Core.Tests.Towns;

public class GhslBuiltUpSourceTests
{
    private const string Prefix = "GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss_V1_0";
    private static readonly Coordinate Humanes = new(40.2526, -3.8299);

    // The recorded fixture: 2 × 2 real blocks of tile R5_C18 around Humanes (512 × 512 cells, LZW), zipped like GHSL's.
    private static BinaryHttpMessageHandler Server(Func<string, byte[]?> tiles) => new(uri =>
        tiles(uri.Segments[^1]) is { } body ? (HttpStatusCode.OK, body) : (HttpStatusCode.NotFound, []));

    private static byte[]? HumanesTile(string file) =>
        file == $"{Prefix}_R5_C18.zip" ? Fixture.ReadBytes($"Ghsl/{Prefix}_R5_C18.zip") : null;

    [Fact]
    public async Task Reads_the_built_up_share_around_humanes_from_its_tile()
    {
        var server = Server(HumanesTile);
        var source = new GhslBuiltUpSource(new HttpClient(server));

        var grid = await source.GetAroundAsync(Humanes, 10);

        Assert.Equal(new Uri($"https://jeodpp.jrc.ec.europa.eu/ftp/jrc-opendata/GHSL/GHS_BUILT_S_GLOBE_R2023A/GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss/V1-0/tiles/{Prefix}_R5_C18.zip"),
            Assert.Single(server.Requests));
        Assert.Equal((284, 217), (grid.Width, grid.Height));
        Assert.True(grid.North >= Humanes.Latitude + 0.089 && grid.South <= Humanes.Latitude - 0.089);
        // Town centres are a third or more built; fields between Humanes and Moraleja almost nothing.
        Assert.Equal(0.330, grid.ShareAt(Humanes), 3);
        Assert.Equal(0.438, grid.ShareAt(new Coordinate(40.2842, -3.7942)), 3);
        Assert.True(grid.ShareAt(new Coordinate(40.23, -3.87)) < 0.02);
        Assert.Equal(GhslBuiltUpSource.AttributionText, source.Attribution);
    }

    [Fact]
    public async Task Keeps_a_downloaded_tile_and_does_not_ask_again()
    {
        var cache = Path.Combine(Path.GetTempPath(), "vientohud-ghsl-" + Guid.NewGuid());
        try
        {
            var server = Server(HumanesTile);
            var options = new GhslOptions { CacheDirectory = cache };

            var first = await new GhslBuiltUpSource(new HttpClient(server), options).GetAroundAsync(Humanes, 5);
            var second = await new GhslBuiltUpSource(new HttpClient(server), options).GetAroundAsync(Humanes, 5);

            Assert.Single(server.Requests);
            Assert.True(File.Exists(Path.Combine(cache, $"{Prefix}_R5_C18.tif")));
            Assert.Equal(first.ShareAt(Humanes), second.ShareAt(Humanes));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task A_missing_tile_is_open_ground_and_is_remembered()
    {
        var cache = Path.Combine(Path.GetTempPath(), "vientohud-ghsl-" + Guid.NewGuid());
        try
        {
            var server = Server(_ => null);
            var options = new GhslOptions { CacheDirectory = cache };
            var sea = new Coordinate(44.0, -12.0);

            var grid = await new GhslBuiltUpSource(new HttpClient(server), options).GetAroundAsync(sea, 3);
            await new GhslBuiltUpSource(new HttpClient(server), options).GetAroundAsync(sea, 3);

            Assert.Equal($"{Prefix}_R5_C17.zip", Assert.Single(server.Requests).Segments[^1]);
            Assert.Equal(0, grid.ShareAt(sea));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task A_window_across_two_tiles_reads_both_and_no_data_is_open_ground()
    {
        // Two small tiles either side of the line between columns 18 and 19 (0.0079° W), at 45° N: 1,000 m² built per
        // cell to the west, 3,000 to the east, and one cell without data.
        const double gridWest = -180.00791660463318;
        const double gridNorth = 89.09958333862926;
        var north = gridNorth - 52736 * TestGeoTiff.CellDegrees;
        var line = gridWest + 216000 * TestGeoTiff.CellDegrees;
        var noData = new Coordinate(45.0, line + 10.5 * TestGeoTiff.CellDegrees);
        var noDataRow = (int)Math.Floor((north - noData.Latitude) / TestGeoTiff.CellDegrees);
        var server = Server(file => file switch
        {
            $"{Prefix}_R5_C18.zip" => TestGeoTiff.Zip(line - 256 * TestGeoTiff.CellDegrees, north, 256, (_, _) => 1000),
            $"{Prefix}_R5_C19.zip" => TestGeoTiff.Zip(line, north, 256, (x, y) => (x, y) == (10, noDataRow) ? (ushort)65535 : (ushort)3000),
            _ => null,
        });

        var grid = await new GhslBuiltUpSource(new HttpClient(server)).GetAroundAsync(new Coordinate(45.0, line), 2);

        Assert.Equal(2, server.Requests.Count);
        var cellM2 = Math.Pow(TestGeoTiff.CellDegrees * 6371.0088 * Math.PI / 180 * 1000, 2) * Math.Cos(45.0 * Math.PI / 180);
        Assert.Equal(1000 / cellM2, grid.ShareAt(new Coordinate(45.0, line - 0.01)), 2);
        Assert.Equal(3000 / cellM2, grid.ShareAt(new Coordinate(45.0, line + 0.01)), 2);
        Assert.Equal(0, grid.ShareAt(noData));
    }

    [Fact]
    public async Task Server_errors_and_broken_files_are_reported()
    {
        var failing = new BinaryHttpMessageHandler(_ => (HttpStatusCode.InternalServerError, []));
        var broken = Server(_ => "not a zip"u8.ToArray());

        await Assert.ThrowsAsync<BuiltUpException>(() => new GhslBuiltUpSource(new HttpClient(failing)).GetAroundAsync(Humanes, 5));
        await Assert.ThrowsAsync<BuiltUpException>(() => new GhslBuiltUpSource(new HttpClient(broken)).GetAroundAsync(Humanes, 5));
    }
}
