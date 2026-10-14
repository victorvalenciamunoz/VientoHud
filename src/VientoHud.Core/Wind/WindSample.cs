namespace VientoHud.Core.Wind;

/// <summary>Forecast wind for one hour at 10 m above ground.</summary>
/// <param name="Time">Start of the hour, in UTC.</param>
/// <param name="SpeedKmh">Mean wind speed in km/h.</param>
/// <param name="GustKmh">Maximum gust in km/h.</param>
/// <param name="DirectionFromDegrees">
/// Meteorological convention: degrees the wind blows FROM, clockwise from north (0 = from the north,
/// 90 = from the east). Normalized to [0, 360). Not to be confused with route bearings, which point TO.
/// </param>
/// <param name="Model">Weather model that produced this value (provider-specific name).</param>
public sealed record WindSample(
    DateTimeOffset Time,
    double SpeedKmh,
    double GustKmh,
    double DirectionFromDegrees,
    string Model)
{
    public DateTimeOffset Time { get; } = Time.Offset == TimeSpan.Zero
        ? Time
        : throw new ArgumentException("Time must be in UTC.", nameof(Time));

    public double SpeedKmh { get; } = SpeedKmh >= 0
        ? SpeedKmh
        : throw new ArgumentOutOfRangeException(nameof(SpeedKmh), SpeedKmh, "Speed cannot be negative.");

    public double GustKmh { get; } = GustKmh >= 0
        ? GustKmh
        : throw new ArgumentOutOfRangeException(nameof(GustKmh), GustKmh, "Gust cannot be negative.");

    public double DirectionFromDegrees { get; } = double.IsFinite(DirectionFromDegrees)
        ? (DirectionFromDegrees % 360 + 360) % 360
        : throw new ArgumentOutOfRangeException(nameof(DirectionFromDegrees), DirectionFromDegrees, "Direction must be finite.");
}
