namespace VientoHud.Core.Location;

/// <summary>Source of the device's current position.</summary>
public interface ILocationProvider
{
    /// <exception cref="LocationUnavailableException">The position cannot be obtained.</exception>
    Task<LocationFix> GetCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>A position reported by the device.</summary>
/// <param name="Position">WGS84 position.</param>
/// <param name="AccuracyMeters">Radius of uncertainty in metres, when the device reports it.</param>
/// <param name="Timestamp">When the position was measured, in UTC.</param>
public sealed record LocationFix(Coordinate Position, double? AccuracyMeters, DateTimeOffset Timestamp);

/// <summary>The device position could not be obtained (permission denied, no signal, timeout…).</summary>
public sealed class LocationUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
