using System.Net;
using System.Text;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public class OperaRadarSourceTests
{
    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset October3At6 = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset October16At1107 = new(2026, 10, 16, 11, 7, 0, TimeSpan.Zero);

    private const string RateKey = "2026/10/03/OPERA/COMP/OPERA@20261003T0600@0@RATE.h5";

    private static readonly byte[] NotFound = Fixture.ReadBytes("Opera/missing-key.xml");

    private static (OperaRadarSource Source, BinaryHttpMessageHandler Handler) Create(
        DateTimeOffset now,
        Func<string, (HttpStatusCode, byte[])> respond,
        OperaProduct product = OperaProduct.Reflectivity,
        string? cacheDirectory = null,
        TimeSpan? cacheMaxAge = null)
    {
        // Paths come with the '@' escaped; compare them unescaped.
        var handler = new BinaryHttpMessageHandler(uri => respond(Uri.UnescapeDataString(uri.PathAndQuery)));
        var options = new OperaOptions { Product = product, CacheDirectory = cacheDirectory, CacheMaxAge = cacheMaxAge };
        return (new OperaRadarSource(new HttpClient(handler), options, new FixedTimeProvider(now)), handler);
    }

    private static byte[] Listing(params string[] keys) => Encoding.UTF8.GetBytes(
        $"""<?xml version="1.0" encoding="UTF-8"?><ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><IsTruncated>false</IsTruncated>{string.Concat(keys.Select(k => $"<Contents><Key>{k}</Key></Contents>"))}</ListBucketResult>""");

    [Fact]
    public async Task Finds_the_latest_reflectivity_frame_listing_the_last_two_hours()
    {
        var (source, handler) = Create(October16At1107, _ =>
            (HttpStatusCode.OK, Fixture.ReadBytes("Opera/list-24h-2026-10-16-after-0907.xml")));

        var latest = await source.GetLatestFrameTimeAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 11, 0, 0, TimeSpan.Zero), latest);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "/openradar-24h?list-type=2&prefix=2026/10/16/OPERA/COMP/&start-after=2026/10/16/OPERA/COMP/OPERA@20261016T0907",
            Uri.UnescapeDataString(request.PathAndQuery));
    }

    [Fact]
    public async Task The_rain_rate_product_has_its_own_latest_frame()
    {
        var (source, _) = Create(October16At1107, _ =>
            (HttpStatusCode.OK, Fixture.ReadBytes("Opera/list-24h-2026-10-16-after-0907.xml")), OperaProduct.RainRate);

        var latest = await source.GetLatestFrameTimeAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 10, 45, 0, TimeSpan.Zero), latest);
        Assert.Equal(TimeSpan.FromMinutes(15), source.FrameInterval);
    }

    [Fact]
    public async Task Just_after_midnight_also_lists_the_day_before()
    {
        var (source, handler) = Create(new DateTimeOffset(2026, 10, 16, 0, 20, 0, TimeSpan.Zero), path =>
            path.Contains("prefix=2026/10/15/")
                ? (HttpStatusCode.OK, Fixture.ReadBytes("Opera/list-24h-2026-10-15-after-2220.xml"))
                : (HttpStatusCode.OK, Listing(
                    "2026/10/16/OPERA/COMP/OPERA@20261016T0000@0@DBZH.h5",
                    "2026/10/16/OPERA/COMP/OPERA@20261016T0005@0@DBZH.h5",
                    "2026/10/16/OPERA/COMP/OPERA@20261016T0010@0@DBZH.tiff",
                    "2026/10/16/OPERA/COMP/OPERA@20261016T0015@0@RATE.h5")));

        var latest = await source.GetLatestFrameTimeAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 0, 5, 0, TimeSpan.Zero), latest);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("start-after=2026/10/15/OPERA/COMP/OPERA@20261015T2220",
            Uri.UnescapeDataString(handler.Requests[0].Query));
        Assert.DoesNotContain("start-after", handler.Requests[1].Query);
    }

    [Fact]
    public async Task Follows_listings_split_in_pages()
    {
        var page1 = Encoding.UTF8.GetBytes("""
            <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><IsTruncated>true</IsTruncated>
            <NextContinuationToken>next+page</NextContinuationToken>
            <Contents><Key>2026/10/16/OPERA/COMP/OPERA@20261016T1000@0@DBZH.h5</Key></Contents></ListBucketResult>
            """);
        var (source, handler) = Create(October16At1107, path =>
            (HttpStatusCode.OK, path.Contains("continuation-token=next+page")
                ? Listing("2026/10/16/OPERA/COMP/OPERA@20261016T1005@0@DBZH.h5")
                : page1));

        var latest = await source.GetLatestFrameTimeAsync();

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 10, 5, 0, TimeSpan.Zero), latest);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task No_recent_frames_is_a_radar_error()
    {
        var (source, _) = Create(October16At1107, _ => (HttpStatusCode.OK, Listing()));

        await Assert.ThrowsAsync<RadarException>(() => source.GetLatestFrameTimeAsync());
    }

    [Fact]
    public async Task An_old_frame_comes_from_the_archive()
    {
        var (source, handler) = Create(October16At1107, path =>
            path == $"/openradar-archive/{RateKey}"
                ? (HttpStatusCode.OK, Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5"))
                : (HttpStatusCode.NotFound, NotFound), OperaProduct.RainRate);

        var grid = await source.GetFrameAsync(October3At6, Madrid, 50);

        Assert.NotNull(grid);
        Assert.Equal(October3At6, grid.Time);
        Assert.Equal(51, grid.Rows);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_recent_frame_is_looked_for_in_the_24_hour_bucket_first()
    {
        var (source, handler) = Create(October3At6.AddMinutes(20), path =>
            path == $"/openradar-archive/{RateKey}"
                ? (HttpStatusCode.OK, Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5"))
                : (HttpStatusCode.NotFound, NotFound), OperaProduct.RainRate);

        var grid = await source.GetFrameAsync(October3At6, Madrid, 50);

        Assert.NotNull(grid);
        Assert.Equal(
            [$"/openradar-24h/{RateKey}", $"/openradar-archive/{RateKey}"],
            handler.Requests.Select(u => Uri.UnescapeDataString(u.AbsolutePath)));
    }

    [Fact]
    public async Task A_missing_frame_is_null()
    {
        var (source, handler) = Create(October16At1107, _ => (HttpStatusCode.NotFound, NotFound));

        var grid = await source.GetFrameAsync(October3At6.AddMinutes(5), Madrid, 50);

        Assert.Null(grid);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_file_for_another_time_is_a_radar_error()
    {
        var (source, _) = Create(October16At1107, _ =>
            (HttpStatusCode.OK, Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5")), OperaProduct.RainRate);

        await Assert.ThrowsAsync<RadarException>(() => source.GetFrameAsync(October3At6.AddMinutes(15), Madrid, 50));
    }

    [Fact]
    public async Task Server_and_network_failures_are_radar_errors()
    {
        var (source, _) = Create(October16At1107, _ => (HttpStatusCode.ServiceUnavailable, "busy"u8.ToArray()));
        var offline = new OperaRadarSource(new HttpClient(new FailingHttpMessageHandler()));

        await Assert.ThrowsAsync<RadarException>(() => source.GetFrameAsync(October3At6, Madrid, 50));
        await Assert.ThrowsAsync<RadarException>(() => source.GetLatestFrameTimeAsync());
        await Assert.ThrowsAsync<RadarException>(() => offline.GetFrameAsync(October3At6, Madrid, 50));
    }

    [Fact]
    public async Task Rejects_times_that_are_not_frame_times_in_utc()
    {
        var (source, handler) = Create(October16At1107, _ => (HttpStatusCode.NotFound, NotFound));

        await Assert.ThrowsAsync<ArgumentException>(() => source.GetFrameAsync(October3At6.AddMinutes(2), Madrid, 50));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetFrameAsync(October3At6.ToOffset(TimeSpan.FromHours(2)), Madrid, 50));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.GetFrameAsync(October3At6, Madrid, 0));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Keeps_downloaded_files_in_the_cache()
    {
        var cache = Path.Combine(Path.GetTempPath(), "vientohud-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var (first, firstHandler) = Create(October16At1107, path =>
                path == $"/openradar-archive/{RateKey}"
                    ? (HttpStatusCode.OK, Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5"))
                    : (HttpStatusCode.NotFound, NotFound), OperaProduct.RainRate, cache);
            var (second, secondHandler) = Create(October16At1107, _ => (HttpStatusCode.ServiceUnavailable, []),
                OperaProduct.RainRate, cache);

            var downloaded = await first.GetFrameAsync(October3At6, Madrid, 50);
            var cached = await second.GetFrameAsync(October3At6, Madrid, 50);

            Assert.Single(firstHandler.Requests);
            Assert.Empty(secondHandler.Requests);
            Assert.True(File.Exists(Path.Combine([cache, .. RateKey.Split('/')])));
            Assert.Equal(downloaded!.RateAt(Madrid), cached!.RateAt(Madrid));
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Old_cached_files_are_deleted_when_a_new_one_is_stored_if_a_maximum_age_is_set()
    {
        var cache = Path.Combine(Path.GetTempPath(), "vientohud-tests", Guid.NewGuid().ToString("N"));
        try
        {
            // Two frames already in the cache: one from 4 h before "now" (October16At1107), one from 1 h before.
            var old = Path.Combine(cache, "2026", "10", "16", "OPERA", "COMP", "old.h5");
            var fresh = Path.Combine(cache, "2026", "10", "16", "OPERA", "COMP", "fresh.h5");
            var emptyFolder = Path.Combine(cache, "2026", "10", "15");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            Directory.CreateDirectory(emptyFolder);
            await File.WriteAllBytesAsync(old, [1]);
            await File.WriteAllBytesAsync(fresh, [1]);
            File.SetLastWriteTimeUtc(old, October16At1107.AddHours(-4).UtcDateTime);
            File.SetLastWriteTimeUtc(fresh, October16At1107.AddHours(-1).UtcDateTime);
            var (source, _) = Create(October16At1107, path =>
                path == $"/openradar-archive/{RateKey}"
                    ? (HttpStatusCode.OK, Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5"))
                    : (HttpStatusCode.NotFound, NotFound), OperaProduct.RainRate, cache, TimeSpan.FromHours(3));

            await source.GetFrameAsync(October3At6, Madrid, 50);

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
            Assert.True(File.Exists(Path.Combine([cache, .. RateKey.Split('/')])));
            Assert.False(Directory.Exists(emptyFolder));
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    [Fact]
    public void Reflectivity_frames_come_every_five_minutes_with_attribution()
    {
        var source = new OperaRadarSource(new HttpClient());

        Assert.Equal(TimeSpan.FromMinutes(5), source.FrameInterval);
        Assert.Contains("OPERA", source.Attribution);
        Assert.Contains("CC BY 4.0", source.Attribution);
    }
}
