using System.Net;
using System.Net.Http.Headers;
using VientoHud.Core.Net;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Net;

public class HttpRangeStreamTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly Coordinate Elsewhere = TestPlaces.Origin.Destination(30, 300);
    private static readonly Uri Somewhere = new("https://radar.test/frame.h5");

    // A server holding one file that answers byte ranges as S3 does (206 with Content-Range), or ignores them.
    private sealed class RangeServer(byte[] file, bool honoursRanges = true) : HttpMessageHandler
    {
        public List<(long From, long To)> Ranges { get; } = [];

        public long BytesSent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.Contains("missing"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
            }
            var range = request.Headers.Range?.Ranges.Single();
            if (!honoursRanges || range is null)
            {
                BytesSent += file.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(file) });
            }
            var from = range.From!.Value;
            var to = Math.Min(range.To!.Value, file.Length - 1);
            Ranges.Add((from, to));
            var part = file.AsSpan((int)from, (int)(to - from + 1)).ToArray();
            BytesSent += part.Length;
            var content = new ByteArrayContent(part);
            content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, file.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
    }

    private static void AssertSameGrid(RainGrid expected, RainGrid actual)
    {
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal((expected.Rows, expected.Columns, expected.Left, expected.Top), (actual.Rows, actual.Columns, actual.Left, actual.Top));
        for (var r = 0; r < expected.Rows; r++)
        {
            for (var c = 0; c < expected.Columns; c++)
            {
                Assert.Equal(expected[r, c], actual[r, c]);
            }
        }
    }

    [Theory]
    [InlineData("Opera/OPERA@20261003T0600@0@DBZH.h5", 45.0, 1.0)]
    [InlineData("Opera/OPERA@20261003T0600@0@DBZH.h5", 47.5, 2.5)]
    [InlineData("Opera/OPERA@20261003T0600@0@RATE.h5", 45.0, 1.0)]
    public async Task Reading_a_crop_by_ranges_gives_the_same_grid_with_a_fraction_of_the_file(string fixture, double lat, double lon)
    {
        var file = Fixture.ReadBytes(fixture);
        var center = new Coordinate(lat, lon);
        var whole = OdimComposite.ReadAround(new MemoryStream(file), center, 150);
        var server = new RangeServer(file);

        var stream = (await HttpRangeStream.OpenAsync(new HttpClient(server), Somewhere))!;
        var part = OdimComposite.ReadAround(stream, center, 150);

        AssertSameGrid(whole, part);
        Assert.Equal(file.Length, stream.Length);
        // A rainy day's composite (the fixtures): a fraction of the file (how much depends on the chunks the crop falls
        // in; under 30 % at these points), in a handful of requests.
        Assert.True(server.BytesSent < file.Length / 3, $"{server.BytesSent} of {file.Length} bytes");
        Assert.InRange(stream.Requests, 2, 10);
        Assert.Equal(stream.Requests, server.Ranges.Count);
    }

    [Fact]
    public async Task Blocks_already_fetched_are_not_asked_for_again()
    {
        var file = Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@DBZH.h5");
        var server = new RangeServer(file);
        var blocks = new Dictionary<long, byte[]>();

        var first = (await HttpRangeStream.OpenAsync(new HttpClient(server), Somewhere, blocks: blocks))!;
        OdimComposite.ReadAround(first, Origin, 150);
        var afterFirst = server.Ranges.Count;
        var again = (await HttpRangeStream.OpenAsync(new HttpClient(server), Somewhere, blocks: blocks))!;
        OdimComposite.ReadAround(again, Origin, 150);

        // Only the opening request again (it tells the length); the crop's blocks were all there.
        Assert.Equal(afterFirst + 1, server.Ranges.Count);
    }

    [Fact]
    public async Task A_missing_file_is_null_and_a_server_that_ignores_ranges_still_works()
    {
        var file = Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5");

        Assert.Null(await HttpRangeStream.OpenAsync(new HttpClient(new RangeServer(file)), new Uri("https://radar.test/missing.h5")));

        var ignoring = new RangeServer(file, honoursRanges: false);
        var stream = (await HttpRangeStream.OpenAsync(new HttpClient(ignoring), Somewhere))!;
        var grid = OdimComposite.ReadAround(stream, Elsewhere, 100);

        AssertSameGrid(OdimComposite.ReadAround(new MemoryStream(file), Elsewhere, 100), grid);
        Assert.Equal(1, stream.Requests);
    }

    [Fact]
    public async Task The_radar_source_reads_frames_by_ranges_when_asked_to()
    {
        var file = Fixture.ReadBytes("Opera/OPERA@20261003T0600@0@RATE.h5");
        var server = new RangeServer(file);
        var time = new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
        var source = new OperaRadarSource(
            new HttpClient(server),
            new OperaOptions { Product = OperaProduct.RainRate, PartialReads = true },
            new FixedTimeProvider(time.AddMinutes(20)));

        var grid = await source.GetFrameAsync(time, Origin, 150);
        var sentFirst = server.BytesSent;
        var again = await source.GetFrameAsync(time, Origin, 150);

        Assert.NotNull(grid);
        AssertSameGrid(OdimComposite.ReadAround(new MemoryStream(file), Origin, 150), grid);
        AssertSameGrid(grid, again!);
        Assert.True(sentFirst < file.Length / 4);
        // The second time only the opening block comes back.
        Assert.True(server.BytesSent - sentFirst <= HttpRangeStream.DefaultBlockSize);
    }
}
