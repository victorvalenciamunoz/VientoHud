using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Support;

/// <summary>
/// Real rides from Fixtures/Rides (see its README): GPS tracks of good routes, as examples of what the generator
/// should produce. Read as routes without road tags.
/// </summary>
internal static class RecordedRides
{
    /// <summary>
    /// A balloon: leaves Humanes de Madrid and comes back along the same ~19 km of road, with a loop at the far end.
    /// </summary>
    public static Route HumanesBalloon() => Load("humanes-balloon-2026-09-13.gpx");

    /// <summary>
    /// A pure out-and-back ride of 96 km from La Bañeza, leaving and entering town by different streets (first ~1 km).
    /// </summary>
    public static Route LaBanezaOutAndBack() => Load("labaneza-out-and-back-2026-08-02.gpx");

    /// <summary>
    /// A lasso of 84 km from Boadilla del Monte: a loop near home joined by a 4.6 km neck, ridden there and back,
    /// to a 50 km loop.
    /// </summary>
    public static Route BoadillaLasso() => Load("boadilla-lasso-2025-05-25.gpx");

    /// <summary>
    /// A balloon in the middle, 21.6 km from Humanes de Madrid: a 4 km string, a small loop, and from it a 3 km dead end
    /// to the farthest point of the ride, ridden there and back.
    /// </summary>
    public static Route HumanesBalloonInTheMiddle() => Load("humanes-balloon-in-the-middle-2025-04-13.gpx");

    private static Route Load(string file)
    {
        XNamespace gpx = "http://www.topografix.com/GPX/1/1";
        var points = XDocument.Parse(Fixture.Read("Rides/" + file))
            .Descendants(gpx + "trkpt")
            .Select(p => new RoutePoint(new Coordinate(
                double.Parse(p.Attribute("lat")!.Value, CultureInfo.InvariantCulture),
                double.Parse(p.Attribute("lon")!.Value, CultureInfo.InvariantCulture))))
            .ToList();
        var km = points.Zip(points.Skip(1), (a, b) => a.Position.DistanceKmTo(b.Position)).Sum();
        return new Route(points, [], km, null, "Recorded ride");
    }
}
