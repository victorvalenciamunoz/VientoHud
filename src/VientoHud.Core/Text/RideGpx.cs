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

    /// <summary>
    /// The GPX of a traced ride (DECISIONS 048) and the file name to give it: the name carries the bearing to the farthest
    /// point touched (Hammerhead shows the file name), the description the advice as the screen shows it, the alerts as
    /// waypoints. The same from the console and the app.
    /// </summary>
    /// <param name="fromNow">Whether the rider asked to leave now (the recommendation says "Sal ahora") or at a time.</param>
    public static RideFile ForTraced(TracedRide ride, bool fromNow, TimeZoneInfo zone)
    {
        var option = ride.Ride;
        var start = ride.Request.Start;
        var bearing = start.BearingTo(ride.Request.Via.MaxBy(v => v.DistanceKmTo(start)));
        var departure = ride.Departure.Chosen.Departure;
        var turn = RideText.Turn(option, ride.Shape);
        var lines = new List<string>
        {
            RideText.Recommendation(ride.Departure, fromNow, zone),
            $"{RideText.Kind(ride.Shape)} {Km(option.Route.DistanceKm)}{(ride.Shape == RouteShape.OutAndBack ? "" : $", {turn}")}.",
            $"{Capitalized(turn)}: {RideText.Advantage(option.Score)}",
        };
        lines.AddRange(RideText.Lines(option, ride.RainFailure, zone));
        lines.Add($"{RideText.TracedSummary(ride)}. {RideText.Arrival(option, zone)}");
        lines.AddRange(RideText.Attributions(option.Route, ride.Rain));
        return new RideFile(
            FileName(ride.Shape, option.Route.DistanceKm, bearing, departure, zone),
            Track(option, Name(departure, option.Route.DistanceKm, bearing, ride.Shape, zone), departure, lines, zone));
    }
}

/// <summary>A GPX ready to write or share, with the file name the GPS will show.</summary>
public sealed record RideFile(string FileName, GpxTrack Track);
