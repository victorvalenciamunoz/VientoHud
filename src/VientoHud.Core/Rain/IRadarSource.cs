namespace VientoHud.Core.Rain;

/// <summary>Source of radar frames as rain rates around a point.</summary>
public interface IRadarSource
{
    /// <summary>Time between consecutive frames (frames fall on multiples of it, in UTC).</summary>
    TimeSpan FrameInterval { get; }

    /// <summary>Attribution the data licence asks for.</summary>
    string Attribution { get; }

    /// <summary>Time of the most recent frame published, in UTC.</summary>
    /// <exception cref="RadarException">The source failed or has no recent frames.</exception>
    Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The frame of <paramref name="time"/> (UTC, a multiple of <see cref="FrameInterval"/>) cut to a square of
    /// side 2·<paramref name="radiusKm"/> centred on <paramref name="center"/>. Null when the source has no frame for
    /// that time (frames are sometimes missing).
    /// </summary>
    /// <exception cref="RadarException">The source failed or returned an unreadable frame.</exception>
    Task<RainGrid?> GetFrameAsync(
        DateTimeOffset time,
        Coordinate center,
        double radiusKm,
        CancellationToken cancellationToken = default);
}

/// <summary>Radar data could not be obtained or read.</summary>
public sealed class RadarException(string message, Exception? innerException = null) : Exception(message, innerException);
