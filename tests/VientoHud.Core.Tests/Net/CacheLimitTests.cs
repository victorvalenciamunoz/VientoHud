using VientoHud.Core.Net;

namespace VientoHud.Core.Tests.Net;

public class CacheLimitTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vientohud-cache-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Noon = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    // A file of the given size under the cache, last written (and accessed) minutesAgo before noon.
    private string File(string relative, int bytes, int minutesAgo)
    {
        var path = Path.Combine(_directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        var when = Noon.AddMinutes(-minutesAgo);
        System.IO.File.SetLastWriteTimeUtc(path, when);
        System.IO.File.SetLastAccessTimeUtc(path, when);
        return path;
    }

    [Fact]
    public void Deletes_what_was_used_longest_ago_until_it_fits_and_the_folders_left_empty()
    {
        var oldest = File("a/12/1/1.png", 400, 300);
        var old = File("a/12/1/2.png", 400, 200);
        var recent = File("b/12/2/1.png", 400, 100);
        var newest = File("b/12/2/2.png", 400, 10);

        var (kept, deleted) = CacheLimit.Trim(_directory, 1000);

        Assert.Equal(800, kept);
        Assert.Equal(800, deleted);
        Assert.False(System.IO.File.Exists(oldest));
        Assert.False(System.IO.File.Exists(old));
        Assert.True(System.IO.File.Exists(recent));
        Assert.True(System.IO.File.Exists(newest));
        Assert.False(Directory.Exists(Path.Combine(_directory, "a")));
        Assert.True(Directory.Exists(_directory));
    }

    [Fact]
    public void A_file_read_lately_stays_although_it_was_written_long_ago()
    {
        var writtenLongAgoReadNow = File("12/1/1.png", 400, 1000);
        System.IO.File.SetLastAccessTimeUtc(writtenLongAgoReadNow, Noon);
        var middle = File("12/1/2.png", 400, 100);

        CacheLimit.Trim(_directory, 500);

        Assert.True(System.IO.File.Exists(writtenLongAgoReadNow));
        Assert.False(System.IO.File.Exists(middle));
    }

    [Fact]
    public void Under_the_cap_or_without_the_folder_nothing_is_deleted()
    {
        File("12/1/1.png", 400, 10);

        Assert.Equal((400L, 0L), CacheLimit.Trim(_directory, 1000));
        Assert.Equal((0L, 0L), CacheLimit.Trim(Path.Combine(_directory, "missing")));
    }
}
