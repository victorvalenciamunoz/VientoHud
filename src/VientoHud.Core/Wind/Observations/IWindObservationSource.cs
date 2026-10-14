namespace VientoHud.Core.Wind.Observations;

/// <summary>Source of measured wind at weather stations, used to check forecasts against reality.</summary>
public interface IWindObservationSource
{
    /// <summary>
    /// Stations that are still reporting, nearest first, at most <paramref name="count"/> and within
    /// <paramref name="maxDistanceKm"/> of <paramref name="near"/>. May return fewer, or none.
    /// </summary>
    /// <exception cref="WindObservationException">The source failed.</exception>
    Task<IReadOnlyList<WeatherStation>> FindNearestStationsAsync(
        Coordinate near,
        int count,
        double maxDistanceKm,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every observation of the given stations with <c>from &lt;= Time &lt; to</c>, ordered by station and time.
    /// </summary>
    /// <exception cref="WindObservationException">The source failed.</exception>
    Task<IReadOnlyList<WindObservation>> GetObservationsAsync(
        IReadOnlyCollection<string> stationIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>A weather station.</summary>
/// <param name="Id">Identifier in its source (ICAO code for METAR stations, e.g. LELN).</param>
/// <param name="DistanceKm">Distance from the point it was searched from, in km.</param>
public sealed record WeatherStation(
    string Id,
    string Name,
    Coordinate Position,
    double? ElevationMeters,
    double DistanceKm);

/// <summary>Wind measured at a station.</summary>
/// <param name="Time">Observation time, in UTC.</param>
/// <param name="SpeedKmh">Mean wind speed in km/h (10-minute mean for METAR).</param>
/// <param name="GustKmh">Gust in km/h. Null when not reported: METAR only reports significant gusts.</param>
/// <param name="DirectionFromDegrees">
/// Meteorological convention: degrees the wind blows FROM, clockwise from true north, in [0, 360).
/// Null when there is no meaningful direction: calm, or variable (METAR VRB).
/// </param>
public sealed record WindObservation(
    string StationId,
    DateTimeOffset Time,
    double SpeedKmh,
    double? GustKmh,
    double? DirectionFromDegrees)
{
    public DateTimeOffset Time { get; } = Time.Offset == TimeSpan.Zero
        ? Time
        : throw new ArgumentException("Time must be in UTC.", nameof(Time));

    public double SpeedKmh { get; } = SpeedKmh >= 0
        ? SpeedKmh
        : throw new ArgumentOutOfRangeException(nameof(SpeedKmh), SpeedKmh, "Speed cannot be negative.");

    public double? DirectionFromDegrees { get; } = DirectionFromDegrees is { } d
        ? double.IsFinite(d)
            ? (d % 360 + 360) % 360
            : throw new ArgumentOutOfRangeException(nameof(DirectionFromDegrees), d, "Direction must be finite.")
        : null;
}

/// <summary>Wind observations could not be obtained.</summary>
public sealed class WindObservationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
