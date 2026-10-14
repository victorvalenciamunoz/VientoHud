namespace VientoHud.Core;

/// <summary>A WGS84 position in decimal degrees.</summary>
public readonly record struct Coordinate(double Latitude, double Longitude)
{
    public double Latitude { get; } = Latitude is >= -90 and <= 90
        ? Latitude
        : throw new ArgumentOutOfRangeException(nameof(Latitude), Latitude, "Latitude must be in [-90, 90].");

    public double Longitude { get; } = Longitude is >= -180 and <= 180
        ? Longitude
        : throw new ArgumentOutOfRangeException(nameof(Longitude), Longitude, "Longitude must be in [-180, 180].");
}
