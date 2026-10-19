using System.Globalization;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.Core.Text;

/// <summary>
/// What goes on the GPX of a ride: its name, its file name (the one Hammerhead shows, DECISIONS 037) and the points
/// that warn on the way. The same from the console and the app.
/// </summary>
public static class RideGpx
{
    /// <summary>"Sal 10:30 · 60 km SO" (plus "ida y vuelta").</summary>
    public static string Name(DateTimeOffset departure, double distanceKm, double bearing, RouteShape shape, TimeZoneInfo zone) =>
        $"Sal {Local(departure, zone, "HH:mm")} · {distanceKm.ToString("0", Spanish)} km {Point(bearing)}" +
        (shape == RouteShape.OutAndBack ? " ida y vuelta" : "");

    /// <summary>"1-vuelta-60km-SO-20261019-1030.gpx"; without a rank, "vuelta-60km-SO-20261019-1030.gpx"; "-descartada" when so.</summary>
    public static string FileName(
        RouteShape shape, double km, double bearing, DateTimeOffset departure, TimeZoneInfo zone, int? rank = null, bool discarded = false) =>
        $"{(rank is { } r ? $"{r}-" : "")}{FileKind(shape)}-{km.ToString("0", CultureInfo.InvariantCulture)}km-{Point(bearing)}-" +
        $"{Local(departure, zone, "yyyyMMdd-HHmm")}{(discarded ? "-descartada" : "")}.gpx";

    public static string FileKind(RouteShape shape) => shape == RouteShape.OutAndBack ? "ida-y-vuelta" : "vuelta";

    /// <summary>A point on the way for the GPX: short name for the GPS screen, details in the description.</summary>
    public static GpxWaypoint Waypoint(RideAlert alert, TimeZoneInfo zone)
    {
        var km = $"km {alert.StartKm.ToString("0", Spanish)}";
        var stretch = $"km {alert.StartKm.ToString("0", Spanish)}-{alert.EndKm.ToString("0", Spanish)}";
        return alert.Kind switch
        {
            RideAlertKind.Gusts => new GpxWaypoint(alert.Position, $"Racha lado {alert.Value.ToString("0", Spanish)}",
                $"{stretch}: rachas de lado de hasta {Kmh(alert.Value)} hacia las {Local(alert.Time, zone, "HH:mm")}."),
            RideAlertKind.Rain => new GpxWaypoint(alert.Position, $"Lluvia ~{Local(alert.Time, zone, "HH:mm")}",
                $"{stretch}: lluvia probable ({Percent(alert.Value)}) hacia las {Local(alert.Time, zone, "HH:mm")}."),
            _ => new GpxWaypoint(alert.Position, "Posibles chubascos",
                $"Desde el {km} (hacia las {Local(alert.Time, zone, "HH:mm")}): posibles chubascos en la zona (hasta {Percent(alert.Value)})."),
        };
    }

    /// <summary>The GPX of a ride: the route, the name, the description lines and the alerts as waypoints.</summary>
    public static GpxTrack Track(
        RideOption option, string name, DateTimeOffset departure, IEnumerable<string> descriptionLines, TimeZoneInfo zone) =>
        GpxTrack.FromRoute(
            option.Route, name, departure, string.Join("\n", descriptionLines),
            RideAlerts.For(option).Select(a => Waypoint(a, zone)).ToList());
}
