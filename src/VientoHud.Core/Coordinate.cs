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

    /// <summary>
    /// Initial bearing of the great circle from here to <paramref name="other"/>: degrees TOWARDS, clockwise from north,
    /// in [0, 360). 0 for the same point.
    /// </summary>
    public double BearingTo(Coordinate other)
    {
        var lat1 = Latitude * Math.PI / 180;
        var lat2 = other.Latitude * Math.PI / 180;
        var dLon = (other.Longitude - Longitude) * Math.PI / 180;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    /// <summary>
    /// The point <paramref name="distanceKm"/> away along the great circle that leaves here towards
    /// <paramref name="bearingDegrees"/> (degrees TOWARDS, clockwise from north). Longitude wraps across 180°.
    /// </summary>
    public Coordinate Destination(double bearingDegrees, double distanceKm)
    {
        if (!double.IsFinite(bearingDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(bearingDegrees), bearingDegrees, "Bearing must be finite.");
        }
        if (!double.IsFinite(distanceKm) || distanceKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceKm), distanceKm, "Distance must be finite and not negative.");
        }

        var angular = distanceKm / EarthMeanRadiusKm;
        var bearing = bearingDegrees * Math.PI / 180;
        var lat1 = Latitude * Math.PI / 180;
        var lon1 = Longitude * Math.PI / 180;
        var lat2 = Math.Asin(Math.Clamp(
            Math.Sin(lat1) * Math.Cos(angular) + Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing), -1, 1));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1),
            Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
        var longitude = (lon2 * 180 / Math.PI + 540) % 360 - 180;
        return new Coordinate(lat2 * 180 / Math.PI, longitude);
    }
}
