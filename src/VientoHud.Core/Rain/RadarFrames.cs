namespace VientoHud.Core.Rain;

/// <summary>Picking radar frames by time.</summary>
public static class RadarFrames
{
    /// <summary>Frames to step back when the one asked for is missing (OPERA sometimes skips one).</summary>
    public const int DefaultMaxStepsBack = 3;

    /// <summary>The frame time at or before <paramref name="time"/> (UTC): a multiple of <paramref name="interval"/>.</summary>
    public static DateTimeOffset AtOrBefore(DateTimeOffset time, TimeSpan interval)
    {
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive.");
        }
        return new DateTimeOffset(time.Ticks - time.Ticks % interval.Ticks, TimeSpan.Zero);
    }

    /// <summary>
    /// The frame of the time at or before <paramref name="time"/>; if it is missing, the one before, up to
    /// <paramref name="maxStepsBack"/> frames back. Null when all of them are missing.
    /// </summary>
    public static async Task<RainGrid?> GetFrameAtOrBeforeAsync(
        this IRadarSource source,
        DateTimeOffset time,
        Coordinate center,
        double radiusKm,
        int maxStepsBack = DefaultMaxStepsBack,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxStepsBack);
        var frame = AtOrBefore(time, source.FrameInterval);
        for (var step = 0; step <= maxStepsBack; step++)
        {
            var grid = await source.GetFrameAsync(frame - step * source.FrameInterval, center, radiusKm, cancellationToken);
            if (grid is not null)
            {
                return grid;
            }
        }
        return null;
    }
}
