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
