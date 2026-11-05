using System.Globalization;

namespace VientoHud.Core.Rain;

/// <summary>
/// Lambert azimuthal equal-area projection on the WGS84 ellipsoid (Snyder 1987, "Map projections: a working manual",
/// pp. 182-190), as PROJ defines it with <c>+proj=laea</c>. The OPERA radar composites use it.
/// </summary>
/// <remarks>
/// Projected coordinates are in metres, x towards the east and y towards the north, including the false easting and
/// northing. The inverse uses the series for the latitude from the authalic latitude, accurate to well under a metre.
/// </remarks>
public sealed class LambertAzimuthalEqualArea
{
    private const double SemiMajorAxis = 6378137;
    private const double Flattening = 1 / 298.257223563;
    private const double Degrees = Math.PI / 180;

    private static readonly double E2 = 2 * Flattening - Flattening * Flattening;
    private static readonly double E = Math.Sqrt(E2);
    private static readonly double Qp = Q(Math.PI / 2);
    private static readonly double Rq = SemiMajorAxis * Math.Sqrt(Qp / 2);

    private readonly double _lambda0;
    private readonly double _sinBeta1;
    private readonly double _cosBeta1;
    private readonly double _d;

    public LambertAzimuthalEqualArea(double centerLatitude, double centerLongitude, double falseEasting, double falseNorthing)
    {
        if (centerLatitude is < -90 or > 90 || !double.IsFinite(centerLatitude))
        {
            throw new ArgumentOutOfRangeException(nameof(centerLatitude), centerLatitude, "Latitude must be in [-90, 90].");
        }
        if (Math.Abs(centerLatitude) > 89.999)
        {
            throw new ArgumentOutOfRangeException(nameof(centerLatitude), centerLatitude, "Polar aspects are not supported.");
        }
        if (!double.IsFinite(centerLongitude) || !double.IsFinite(falseEasting) || !double.IsFinite(falseNorthing))
        {
            throw new ArgumentException("Projection parameters must be finite.");
        }

        CenterLatitude = centerLatitude;
        CenterLongitude = centerLongitude;
        FalseEasting = falseEasting;
        FalseNorthing = falseNorthing;

        var phi1 = centerLatitude * Degrees;
        var beta1 = Math.Asin(Q(phi1) / Qp);
        _lambda0 = centerLongitude * Degrees;
        _sinBeta1 = Math.Sin(beta1);
        _cosBeta1 = Math.Cos(beta1);
        var m1 = Math.Cos(phi1) / Math.Sqrt(1 - E2 * Math.Sin(phi1) * Math.Sin(phi1));
        _d = SemiMajorAxis * m1 / (Rq * _cosBeta1);
    }

    public double CenterLatitude { get; }
    public double CenterLongitude { get; }
    public double FalseEasting { get; }
    public double FalseNorthing { get; }

    /// <summary>
    /// Reads a PROJ definition such as <c>+proj=laea +lat_0=55.0 +lon_0=10.0 +x_0=1950000.0 +y_0=-2100000.0
    /// +units=m +ellps=WGS84</c>. Only laea on WGS84 in metres is accepted.
    /// </summary>
    /// <exception cref="FormatException">Not a WGS84 laea definition in metres.</exception>
    public static LambertAzimuthalEqualArea FromProj(string definition)
    {
        var parameters = definition
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.TrimStart('+').Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "", StringComparer.Ordinal);

        string Get(string name, string? fallback = null) =>
            parameters.TryGetValue(name, out var value) ? value
            : fallback ?? throw new FormatException($"Projection '{definition}' has no {name}.");
        double Number(string name, string? fallback = null) =>
            double.TryParse(Get(name, fallback), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException($"Projection '{definition}' has a bad {name}.");

        if (Get("proj") != "laea")
        {
            throw new FormatException($"Projection '{definition}' is not laea.");
        }
        if (Get("ellps", "WGS84") != "WGS84" || Get("units", "m") != "m")
        {
            throw new FormatException($"Projection '{definition}' is not on WGS84 in metres.");
        }

        return new LambertAzimuthalEqualArea(Number("lat_0"), Number("lon_0"), Number("x_0", "0"), Number("y_0", "0"));
    }

    /// <summary>Projected coordinates (metres) of a position.</summary>
    public (double X, double Y) Forward(Coordinate position)
    {
        var phi = position.Latitude * Degrees;
        var lambda = position.Longitude * Degrees - _lambda0;
        var beta = Math.Asin(Math.Clamp(Q(phi) / Qp, -1, 1));
        var sinBeta = Math.Sin(beta);
        var cosBeta = Math.Cos(beta);
        var denominator = 1 + _sinBeta1 * sinBeta + _cosBeta1 * cosBeta * Math.Cos(lambda);
        if (denominator < 1e-12)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "The antipode of the centre cannot be projected.");
        }
        var b = Rq * Math.Sqrt(2 / denominator);
        var x = b * _d * cosBeta * Math.Sin(lambda);
        var y = b / _d * (_cosBeta1 * sinBeta - _sinBeta1 * cosBeta * Math.Cos(lambda));
        return (x + FalseEasting, y + FalseNorthing);
    }

    /// <summary>Position of projected coordinates (metres).</summary>
    public Coordinate Inverse(double x, double y)
    {
        var dx = (x - FalseEasting) / _d;
        var dy = (y - FalseNorthing) * _d;
        var rho = Math.Sqrt(dx * dx + dy * dy);
        if (rho < 1e-9)
        {
            return new Coordinate(CenterLatitude, CenterLongitude);
        }

        var ce = 2 * Math.Asin(Math.Clamp(rho / (2 * Rq), -1, 1));
        var sinCe = Math.Sin(ce);
        var cosCe = Math.Cos(ce);
        var beta = Math.Asin(Math.Clamp(cosCe * _sinBeta1 + dy * sinCe * _cosBeta1 / rho, -1, 1));
        var lambda = _lambda0 + Math.Atan2(dx * sinCe, rho * _cosBeta1 * cosCe - dy * _sinBeta1 * sinCe);

        var e4 = E2 * E2;
        var e6 = e4 * E2;
        var phi = beta
            + (E2 / 3 + 31 * e4 / 180 + 517 * e6 / 5040) * Math.Sin(2 * beta)
            + (23 * e4 / 360 + 251 * e6 / 3780) * Math.Sin(4 * beta)
            + 761 * e6 / 45360 * Math.Sin(6 * beta);

        var longitude = (lambda / Degrees + 540) % 360 - 180;
        return new Coordinate(Math.Clamp(phi / Degrees, -90, 90), longitude);
    }

    private static double Q(double phi)
    {
        var sinPhi = Math.Sin(phi);
        return (1 - E2) * (sinPhi / (1 - E2 * sinPhi * sinPhi)
                           - 1 / (2 * E) * Math.Log((1 - E * sinPhi) / (1 + E * sinPhi)));
    }
}
