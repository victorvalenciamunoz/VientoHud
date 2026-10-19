using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using static VientoHud.ConsoleApp.Display;
using static VientoHud.ConsoleApp.RouteCommand;

namespace VientoHud.ConsoleApp;

/// <summary>
/// A ride through the points the rider gives (DECISIONS 048): one request to BRouter, then the wind both ways round, the
/// rain, leave now or wait, and one GPX. Formatting only: everything comes from Core.
/// </summary>
internal sealed class TraceCommand(TracedRideAdvisor advisor, ILocationProvider device, TextWriter output, string engineName, string windAttribution)
{
    /// <summary>0 when the GPX was written, 1 when the points could not be joined.</summary>
    public async Task<int> RunAsync(TraceOptions options, CancellationToken cancellationToken)
    {
        var (start, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var departure = options.At is { } at ? DepartureTime.Next(at, now, TimeZoneInfo.Local) : now;
        var request = new TracedRequest(start, options.Via, departure, options.SpeedKmh);

        output.WriteLine(request.Shape == RouteShape.OutAndBack
            ? $"Ida y vuelta hasta {Coordinates(options.Via[0])} desde {Coordinates(start)} ({source})"
            : $"Vuelta por {options.Via.Count} puntos desde {Coordinates(start)} ({source})");
        output.WriteLine($"Salida {Local(departure, "ddd dd/MM HH:mm")}{(options.At is null ? " (ahora)" : "")} a {Kmh(options.SpeedKmh)} de media.");
        output.WriteLine($"Motor de rutas: BRouter en {engineName}.");
        output.WriteLine();

        TracedRide ride;
        try
        {
            ride = await advisor.AdviseAsync(request, now, cancellationToken);
        }
        catch (RouteException ex) when (ex.Failure == RouteFailure.Unreachable)
        {
            output.WriteLine($"BRouter no pudo unir los puntos: {ex.Message}. Mueve el punto a una carretera.");
            return 1;
        }

        var option = ride.Ride;
        var chosen = ride.Departure.Chosen;
        var recommendation = Recommendation(ride.Departure, options.At is null);
        output.WriteLine(recommendation);
        if (ride.RainFailure is not null)
        {
            output.WriteLine($"Sin previsión de lluvia: {ride.RainFailure}");
        }
        output.WriteLine();

        var outAndBack = ride.Shape == RouteShape.OutAndBack;
        var turn = outAndBack ? "ida y vuelta" : option.Route.TurnsClockwise() ? "sentido horario" : "sentido antihorario";
        var lines = new List<string>
        {
            $"{Capitalized(turn)}: {Advantage(option.Score)}",
            string.Join(" · ", WindStretches.Describe(option.Winds).Select(Stretch)),
        };
        if (option.Score.GustyStretches.Count > 0)
        {
            lines.Add($"¡Ojo! Rachas de lado de más de {Kmh(RideRanking.DefaultMaxCrossGustKmh)}: " +
                      string.Join(", ", option.Score.GustyStretches.Select(s =>
                          $"km {s.StartKm.ToString("0", Spanish)}-{s.EndKm.ToString("0", Spanish)} (hasta {Kmh(s.MaxCrossGustKmh)})")) + ".");
        }
        else if (!option.Score.GustsKnown)
        {
            lines.Add("Sin previsión de rachas para alguna hora: no se puede avisar de las de lado.");
        }
        lines.AddRange(RainLines(option, ride.RainFailure));
        lines.Add($"Ciudad: {Km(option.Urban.UrbanKm)} ({Percent(option.Urban.UrbanFraction)}) por calles o travesías.");
        lines.Add($"{Summary(ride)}. Llegada hacia las {Local(option.Winds[^1].Leg.EndTime, "HH:mm")}.");

        var alerts = RideAlerts.For(option);
        var farthest = options.Via.MaxBy(v => v.DistanceKmTo(start));
        var bearing = start.BearingTo(farthest);
        var name = $"Sal {Local(chosen.Departure, "HH:mm")} · {option.Route.DistanceKm.ToString("0", Spanish)} km {Point(bearing)}" +
                   (outAndBack ? " ida y vuelta" : "");
        var description = string.Join("\n",
            new[] { recommendation, $"{(outAndBack ? "Ida y vuelta" : "Vuelta")} {Km(option.Route.DistanceKm)}{(outAndBack ? "" : $", {turn}")}." }
                .Concat(lines)
                .Concat(Attributions(option.Route, ride.Rain)));
        Directory.CreateDirectory(options.OutputDirectory);
        var file = await WriteGpxAsync(
            options.OutputDirectory,
            $"{(outAndBack ? "ida-y-vuelta" : "vuelta")}-{option.Route.DistanceKm:0}km-{Point(bearing)}-{Local(chosen.Departure, "yyyyMMdd-HHmm")}.gpx",
            option.Route, name, chosen.Departure, description, alerts.Select(Waypoint).ToList(), cancellationToken);

        foreach (var line in lines)
        {
            output.WriteLine(line);
        }
        output.WriteLine($"GPX: {file}{(alerts.Count switch { 0 => "", 1 => " (1 aviso en la ruta)", var n => $" ({n} avisos en la ruta)" })}");
        output.WriteLine();
        output.WriteLine(option.Route.Attribution);
        output.WriteLine(windAttribution);
        if (ride.Rain is not null)
        {
            output.WriteLine(OperaRadarSourceAttribution);
        }
        return 0;
    }

    /// <summary>Distance, climb, roads and how far the route passes from the points given.</summary>
    private static string Summary(TracedRide ride)
    {
        var a = ride.Analysis;
        var parts = new List<string> { Km(ride.Route.DistanceKm) };
        if (ride.Route.AscentMeters is { } ascent)
        {
            parts.Add($"desnivel {ascent.ToString("0", Spanish)} m");
        }
        parts.Add($"nacionales (trunk) {Km(a.TrunkKm)}");
        if (a.PavedTrackKm >= 0.05)
        {
            parts.Add($"caminos asfaltados {Km(a.PavedTrackKm)}");
        }
        if (a.ForbiddenKm >= 0.05)
        {
            parts.Add($"¡tramos prohibidos (autovía o sin asfalto) {Km(a.ForbiddenKm)}!");
        }
        if (a.MaxWaypointOffsetKm >= 0.5)
        {
            parts.Add($"pasa a {Km(a.MaxWaypointOffsetKm)} de un punto");
        }
        return string.Join(", ", parts);
    }
}
