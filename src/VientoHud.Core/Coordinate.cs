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

    private const double EarthMeanRadiusKm = 6371.0088;

    /// <summary>Great-circle distance in km (haversine on a spherical Earth, error below 0.5 %).</summary>
    public double DistanceKmTo(Coordinate other)
    {
        var lat1 = Latitude * Math.PI / 180;
        var lat2 = other.Latitude * Math.PI / 180;
        var dLat = lat2 - lat1;
        var dLon = (other.Longitude - Longitude) * Math.PI / 180;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        // atan2 instead of asin: asin loses precision near antipodal points.
        return 2 * EarthMeanRadiusKm * Math.Atan2(Math.Sqrt(h), Math.Sqrt(Math.Max(0, 1 - h)));
    }
}
