using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class LambertAzimuthalEqualAreaTests
{
    private const string OperaProj =
        "+proj=laea +lat_0=55.0 +lon_0=10.0 +x_0=1950000.0 +y_0=-2100000.0 +units=m +ellps=WGS84";

    private static readonly LambertAzimuthalEqualArea Opera = LambertAzimuthalEqualArea.FromProj(OperaProj);

    // Corners of the OPERA grid as the composites give them (where/UL_lat... of the 2026-10-03 fixtures),
    // computed by EUMETNET with PROJ: an independent check of our formulas.
    private static readonly Coordinate UpperLeft = new(67.0228327624372, -39.5357864125034);
    private static readonly Coordinate UpperRight = new(67.6210371071631, 57.8119647501499);
    private static readonly Coordinate LowerLeft = new(31.7462153182675, -10.4345768386404);
    private static readonly Coordinate LowerRight = new(31.987650276733, 29.421038635578);

    [Fact]
    public void Reads_the_opera_definition()
    {
        Assert.Equal(55, Opera.CenterLatitude);
        Assert.Equal(10, Opera.CenterLongitude);
        Assert.Equal(1_950_000, Opera.FalseEasting);
        Assert.Equal(-2_100_000, Opera.FalseNorthing);
    }

    [Fact]
    public void The_opera_grid_corners_make_a_3800_by_4400_km_rectangle()
    {
        var ul = Opera.Forward(UpperLeft);
        var ur = Opera.Forward(UpperRight);
        var ll = Opera.Forward(LowerLeft);
        var lr = Opera.Forward(LowerRight);

        // Within a metre: the cells are 1 km.
        Assert.Equal(ul.Y, ur.Y, tolerance: 1);
        Assert.Equal(ll.Y, lr.Y, tolerance: 1);
        Assert.Equal(ul.X, ll.X, tolerance: 1);
        Assert.Equal(ur.X, lr.X, tolerance: 1);
        Assert.Equal(3_800_000, ur.X - ul.X, tolerance: 1);
        Assert.Equal(4_400_000, ul.Y - ll.Y, tolerance: 1);
    }

    [Fact]
    public void The_centre_goes_to_the_false_origin()
    {
        var (x, y) = Opera.Forward(new Coordinate(55, 10));

        Assert.Equal(1_950_000, x, tolerance: 1e-6);
        Assert.Equal(-2_100_000, y, tolerance: 1e-6);
        Assert.Equal(new Coordinate(55, 10), Opera.Inverse(1_950_000, -2_100_000));
    }

    [Theory]
    [InlineData(40.4168, -3.7038)] // Madrid
    [InlineData(42.5987, -5.5671)] // León
    [InlineData(28.1, -15.4)] // Gran Canaria
    [InlineData(64.1, -21.9)] // Reykjavik
    [InlineData(55.75, 37.6)] // Moscow
    [InlineData(-33.9, 18.4)] // Cape Town, far from the centre
    public void Inverse_undoes_forward(double latitude, double longitude)
    {
        var (x, y) = Opera.Forward(new Coordinate(latitude, longitude));

        var back = Opera.Inverse(x, y);

        // 1e-7° ≈ 1 cm.
        Assert.Equal(latitude, back.Latitude, tolerance: 1e-7);
        Assert.Equal(longitude, back.Longitude, tolerance: 1e-7);
    }

    [Fact]
    public void Is_equal_area_near_madrid()
    {
        // A 0.01° square in Madrid measures the same on the ellipsoid and in the projection.
        var corner = new Coordinate(40.4, -3.7);
        var (x0, y0) = Opera.Forward(corner);
        var (x1, y1) = Opera.Forward(new Coordinate(40.41, -3.7));
        var (x2, y2) = Opera.Forward(new Coordinate(40.4, -3.69));
        var projectedArea = Math.Abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0));

        // Area on WGS84 from the radii of curvature at 40.405°: M·dφ · N·cosφ·dλ.
        var phi = 40.405 * Math.PI / 180;
        const double a = 6378137, e2 = 0.00669437999014;
        var w = Math.Sqrt(1 - e2 * Math.Sin(phi) * Math.Sin(phi));
        var m = a * (1 - e2) / (w * w * w);
        var n = a / w;
        var d = 0.01 * Math.PI / 180;
        var ellipsoidArea = m * d * n * Math.Cos(phi) * d;

        Assert.Equal(1, projectedArea / ellipsoidArea, tolerance: 1e-4);
    }

    [Theory]
    [InlineData("+proj=stere +lat_0=90 +lon_0=10 +ellps=WGS84")]
    [InlineData("+proj=laea +lat_0=55 +lon_0=10 +ellps=intl")]
    [InlineData("+proj=laea +lat_0=55 +lon_0=10 +units=km")]
    [InlineData("+proj=laea +lon_0=10")]
    [InlineData("+proj=laea +lat_0=north +lon_0=10")]
    public void Rejects_other_projections(string definition)
    {
        Assert.Throws<FormatException>(() => LambertAzimuthalEqualArea.FromProj(definition));
    }
}
