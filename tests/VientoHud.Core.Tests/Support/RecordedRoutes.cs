using System.Globalization;
using System.Net;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

namespace VientoHud.Core.Tests.Support;

/// <summary>Real BRouter routes from Fixtures/BRouter (see its README), read through the real client.</summary>
internal static class RecordedRoutes
{
    public static readonly RecordedRoute LeonNorth = new(
        "route-leon-loop.geojson",
        "-5.5671,42.5987|-5.596752,42.614558|-5.578426,42.624359|-5.555774,42.624359|-5.537448,42.614558|-5.5671,42.5987");

    public static readonly RecordedRoute LeonEastRepeats = new(
        "route-leon-east-repeats.geojson",
        "-5.5671,42.5987|-5.523983,42.642346|-5.497366,42.615353|-5.497403,42.582005|-5.524044,42.555038|-5.5671,42.5987");

    public static readonly RecordedRoute LeonSouthFastbike = new(
        "route-leon-south-fastbike.geojson",
        "-5.5671,42.5987|-5.537456,42.582838|-5.555779,42.57304|-5.578421,42.57304|-5.596744,42.582838|-5.5671,42.5987");

    public static readonly RecordedRoute TresCantosSouthwestOffset = new(
        "route-trescantos-southwest-offset.geojson",
        "-3.7083,40.6006|-3.714486,40.55588|-3.747908,40.567177|-3.766221,40.591237|-3.762418,40.618877|-3.7083,40.6006");
}

/// <summary>A recorded route and the waypoints it was requested with ("lon,lat|lon,lat", as BRouter takes them).</summary>
internal sealed record RecordedRoute(string File, string LonLats)
{
    public IReadOnlyList<Coordinate> Waypoints { get; } = LonLats.Split('|')
        .Select(p => p.Split(','))
        .Select(p => new Coordinate(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[0], CultureInfo.InvariantCulture)))
        .ToList();

    public Task<Route> LoadAsync()
    {
        var server = new ScriptedHttpMessageHandler(_ => (HttpStatusCode.OK, Fixture.Read("BRouter/" + File)));
        var engine = new BRouterRouteEngine(
            new HttpClient(server),
            new BRouterOptions { ServerProfile = "recorded", MinRequestInterval = TimeSpan.Zero });
        return engine.RouteAsync(Waypoints);
    }
}
