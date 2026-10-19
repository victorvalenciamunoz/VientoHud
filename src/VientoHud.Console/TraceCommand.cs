using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;
using static VientoHud.ConsoleApp.Display;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.ConsoleApp;

/// <summary>
/// A ride through the points the rider gives (DECISIONS 048): one request to BRouter, then the wind both ways round, the
/// rain, leave now or wait, and one GPX. Layout only: everything, sentences included, comes from Core.
/// </summary>
internal sealed class TraceCommand(TracedRideAdvisor advisor, ILocationProvider device, TextWriter output, string engineName, string windAttribution)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;

    /// <summary>0 when the GPX was written, 1 when the points could not be joined.</summary>
    public async Task<int> RunAsync(TraceOptions options, CancellationToken cancellationToken)
    {
        var (start, source) = await ResolveLocationAsync(options.Location, device, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var departure = options.At is { } at ? DepartureTime.Next(at, now, Zone) : now;
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
        var recommendation = RideText.Recommendation(ride.Departure, options.At is null, Zone);
        output.WriteLine(recommendation);
        if (ride.RainFailure is not null)
        {
            output.WriteLine($"Sin previsión de lluvia: {ride.RainFailure}");
        }
        output.WriteLine();

        var outAndBack = ride.Shape == RouteShape.OutAndBack;
        var turn = RideText.Turn(option, ride.Shape);
        var lines = new List<string> { $"{Capitalized(turn)}: {RideText.Advantage(option.Score)}" };
        lines.AddRange(RideText.Lines(option, ride.RainFailure, Zone));
        lines.Add($"{RideText.TracedSummary(ride)}. {RideText.Arrival(option, Zone)}");

        // The file name carries the bearing to the farthest point given (Hammerhead shows the file name).
        var bearing = start.BearingTo(options.Via.MaxBy(v => v.DistanceKmTo(start)));
        var track = RideGpx.Track(
            option, RideGpx.Name(chosen.Departure, option.Route.DistanceKm, bearing, ride.Shape, Zone), chosen.Departure,
            new[] { recommendation, $"{RideText.Kind(ride.Shape)} {Km(option.Route.DistanceKm)}{(outAndBack ? "" : $", {turn}")}." }
                .Concat(lines)
                .Concat(RideText.Attributions(option.Route, ride.Rain)),
            Zone);
        var file = await WriteGpxAsync(
            options.OutputDirectory, RideGpx.FileName(ride.Shape, option.Route.DistanceKm, bearing, chosen.Departure, Zone),
            track, cancellationToken);

        foreach (var line in lines)
        {
            output.WriteLine(line);
        }
        output.WriteLine($"GPX: {file}{AlertsNote(track.Waypoints.Count)}");
        output.WriteLine();
        output.WriteLine(option.Route.Attribution);
        output.WriteLine(windAttribution);
        if (ride.Rain is not null)
        {
            output.WriteLine(Core.Rain.Opera.OperaRadarSource.AttributionText);
        }
        return 0;
    }
}
