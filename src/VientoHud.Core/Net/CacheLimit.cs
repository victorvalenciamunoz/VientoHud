namespace VientoHud.Core.Net;

/// <summary>
/// Keeps a cache folder under a size (phase 9, step 3): when it is over, the files used longest ago (the later of
/// their write and access times: the 3D store sets the access time on every read) are deleted until it is under,
/// and the folders left empty with them. Best effort: a file that cannot be deleted is skipped.
/// </summary>
public static class CacheLimit
{
    /// <summary>The cap of the map caches on the phone.</summary>
    public const long DefaultMaxBytes = 50L * 1024 * 1024;

    /// <summary>Trims <paramref name="directory"/> to <paramref name="maxBytes"/>; returns the bytes it holds afterwards and those deleted.</summary>
    public static (long Kept, long Deleted) Trim(string directory, long maxBytes = DefaultMaxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        if (!Directory.Exists(directory))
        {
            return (0, 0);
        }
        var files = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories)
            .Select(f => (File: f, f.Length, Used: f.LastAccessTimeUtc > f.LastWriteTimeUtc ? f.LastAccessTimeUtc : f.LastWriteTimeUtc))
            .ToList();
        var total = files.Sum(f => f.Length);
        var deleted = 0L;
        foreach (var file in files.OrderBy(f => f.Used))
        {
            if (total <= maxBytes)
            {
                break;
            }
            try
            {
                file.File.Delete();
                total -= file.Length;
                deleted += file.Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        if (deleted > 0)
        {
            RemoveEmptyFolders(directory);
        }
        return (total, deleted);
    }

    private static void RemoveEmptyFolders(string directory)
    {
        foreach (var folder in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length).ToList())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
