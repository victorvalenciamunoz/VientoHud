using System.Net;
using VientoHud.Core.Flyover;

namespace VientoHud.Core.Tests.Flyover;

public class TileStoreTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vientohud-tiles-" + Guid.NewGuid().ToString("N"));
    private readonly List<Uri> _requests = [];
    private HttpStatusCode _status = HttpStatusCode.OK;

    private TileStore Store(TimeProvider? time = null) => new(new HttpClient(new Handler(this)), _directory, time);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fetches_a_tile_once_and_serves_it_from_disk_after()
    {
        var store = Store();
        var tile = new TileAddress(12, 2059, 1473);

        var first = await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));
        var second = await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));

        Assert.Equal(Png, first);
        Assert.Equal(Png, second);
        Assert.Single(_requests);
        Assert.Equal("https://s3.amazonaws.com/elevation-tiles-prod/terrarium/12/2059/1473.png", _requests[0].ToString());
        Assert.True(store.Has("terrarium", tile));
        Assert.True(File.Exists(Path.Combine(_directory, "terrarium", "12", "2059", "1473.png")));
        Assert.Equal(Png.Length, store.SizeBytes());
        Assert.Equal(Png.Length, store.SizeBytes("terrarium"));
        Assert.Equal(0, store.SizeBytes("other"));
    }

    [Fact]
    public async Task A_tile_served_from_disk_is_marked_as_used_but_keeps_its_age()
    {
        var time = new MovableTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var store = Store(time);
        var tile = new TileAddress(12, 2059, 1473);
        await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));
        var path = store.PathOf("terrarium", tile);
        File.SetLastWriteTimeUtc(path, time.Now.UtcDateTime);

        time.Now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));

        Assert.Equal(time.Now.UtcDateTime, File.GetLastAccessTimeUtc(path), TimeSpan.FromSeconds(2));
        Assert.Equal(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(path), TimeSpan.FromSeconds(2));
        Assert.Single(_requests);
    }

    [Fact]
    public async Task A_tile_that_cannot_be_had_is_null_and_not_kept()
    {
        var store = Store();
        var tile = new TileAddress(12, 2059, 1473);
        _status = HttpStatusCode.NotFound;

        var bytes = await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));

        Assert.Null(bytes);
        Assert.False(store.Has("terrarium", tile));
        Assert.Empty(Directory.Exists(_directory) ? Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task An_old_tile_is_fetched_again()
    {
        var time = new MovableTimeProvider(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        var store = Store(time);
        var tile = new TileAddress(12, 2059, 1473);
        await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));
        File.SetLastWriteTimeUtc(store.PathOf("terrarium", tile), time.GetUtcNow().UtcDateTime);

        time.Now += TimeSpan.FromDays(89);
        Assert.True(store.Has("terrarium", tile));
        time.Now += TimeSpan.FromDays(2);
        Assert.False(store.Has("terrarium", tile));
        await store.GetOrFetchAsync("terrarium", tile, TerrainTiles.Url(tile));

        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task Fetches_a_corridor_ahead_skipping_what_it_has()
    {
        var store = Store();
        var tiles = new[] { new TileAddress(12, 2059, 1473), new TileAddress(12, 2060, 1473), new TileAddress(11, 1029, 736) };
        await store.GetOrFetchAsync("terrarium", tiles[0], TerrainTiles.Url(tiles[0]));
        var reported = new List<int>();

        var (fetched, failed) = await store.PrefetchAsync("terrarium", tiles, TerrainTiles.Url, new Progress<int>(reported.Add));

        Assert.Equal(2, fetched);
        Assert.Equal(0, failed);
        Assert.Equal(3, _requests.Count);
        Assert.All(tiles, t => Assert.True(store.Has("terrarium", t)));
        Assert.Equal(3 * Png.Length, store.SizeBytes());
    }

    [Fact]
    public async Task Counts_the_tiles_a_corridor_could_not_get()
    {
        var store = Store();
        _status = HttpStatusCode.ServiceUnavailable;

        var (fetched, failed) = await store.PrefetchAsync("terrarium", [new TileAddress(8, 128, 92)], TerrainTiles.Url);

        Assert.Equal(0, fetched);
        Assert.Equal(1, failed);
    }

    private sealed class MovableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(TileStoreTests owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (owner._requests)
            {
                owner._requests.Add(request.RequestUri!);
            }
            return Task.FromResult(new HttpResponseMessage(owner._status)
            {
                Content = new ByteArrayContent(owner._status == HttpStatusCode.OK ? Png : []),
            });
        }
    }
}
