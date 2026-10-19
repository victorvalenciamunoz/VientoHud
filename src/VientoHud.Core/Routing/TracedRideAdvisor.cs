using VientoHud.Core.Rain;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>A ride the rider traced on the map (DECISIONS 048).</summary>
/// <param name="Start">Where the ride starts and ends.</param>
/// <param name="Via">
/// The points touched on the map, in order: one makes an out and back to it; two or more, a loop through them back
/// to the start.
/// </param>
/// <param name="Departure">The earliest departure, in UTC; later ones up to 2 h are also tried (DECISIONS 036).</param>
/// <param name="AverageSpeedKmh">Average speed of the whole ride.</param>
public sealed record TracedRequest(
    Coordinate Start,
    IReadOnlyList<Coordinate> Via,
    DateTimeOffset Departure,
    double AverageSpeedKmh = RideDistance.DefaultAverageSpeedKmh)
{
    public IReadOnlyList<Coordinate> Via { get; } = Via is { Count: >= 1 }
        ? Via
        : throw new ArgumentException("At least one point to go through is needed.", nameof(Via));

    public DateTimeOffset Departure { get; } = Departure.Offset == TimeSpan.Zero
        ? Departure
        : throw new ArgumentException("Departure must be in UTC.", nameof(Departure));

    public RouteShape Shape => Via.Count == 1 ? RouteShape.OutAndBack : RouteShape.Loop;
}

/// <summary>The route the engine joined the points with, before any weather: enough to draw it.</summary>
/// <param name="Request">What was traced.</param>
/// <param name="Route">There and back for one point, a loop otherwise.</param>
/// <param name="Analysis">
/// Kinds of road (forbidden ones are measured, not refused: the rider chose the points), repetition and how far the
/// route passes from each point.
/// </param>
public sealed record TracedRoute(TracedRequest Request, Route Route, RouteAnalysis Analysis)
{
    public RouteShape Shape => Request.Shape;

    /// <summary>
    /// Where the route passes closest to each point touched, in the same order. The engine moves a point to the nearest
    /// road it may use, sometimes kilometres away (no asphalt nearer): the map should show both.
    /// </summary>
    public IReadOnlyList<Coordinate> Reached =>
        Request.Via.Select(via => Route.Points.MinBy(p => p.Position.DistanceKmTo(via)).Position).ToList();
}

/// <summary>Everything to show for a traced ride: the route, what roads it takes, the rain and when to leave.</summary>
/// <param name="Request">What was traced.</param>
/// <param name="Route">The route as the engine joined the points: there and back for one point, a loop otherwise.</param>
/// <param name="Analysis">As in <see cref="TracedRoute.Analysis"/>.</param>
/// <param name="Departure">When to leave, with the ride in its better way round for each departure.</param>
/// <param name="Rain">The rain to come; null when it could not be asked for.</param>
/// <param name="RainFailure">Why the rain could not be asked for; null otherwise.</param>
public sealed record TracedRide(
    TracedRequest Request,
    Route Route,
    RouteAnalysis Analysis,
    DepartureAdvice Departure,
    RainOutlook? Rain,
    string? RainFailure)
{
    public RouteShape Shape => Request.Shape;

    /// <summary>The ride at the chosen departure, in its better way round, with its wind, rain and town.</summary>
    public RideOption Ride => Departure.Chosen.Best!;
}

/// <summary>
/// Advises on a ride the rider traced on the map: one request to the route engine, then the same judgement as the
/// automatic planner (wind both ways round, rain, leave now or wait). Seconds instead of minutes (DECISIONS 048).
/// In two steps, so the app can draw the route as soon as the engine answers.
/// </summary>
public sealed class TracedRideAdvisor(IRouteEngine engine, IWindForecastProvider winds, IRainOutlookSource rain)
{
    /// <summary>The route through the points: one request to the engine.</summary>
    /// <exception cref="RouteException">The engine could not join the points (move them) or failed.</exception>
    public async Task<TracedRoute> RouteAsync(TracedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = request.Start;
        Route route;
        IReadOnlyList<Coordinate> judged;
        if (request.Shape == RouteShape.OutAndBack)
        {
            // Only the way there is asked for; back is the same road (DECISIONS 040).
            route = (await engine.RouteAsync([start, request.Via[0]], cancellationToken)).ThereAndBack();
            judged = [start, request.Via[0], start];
        }
        else
        {
            judged = [start, .. request.Via, start];
            route = await engine.RouteAsync(judged, cancellationToken);
        }
        return new TracedRoute(request, route, RouteAnalyzer.Analyze(route, judged));
    }

    /// <summary>The weather and the departure for a route already at hand.</summary>
    /// <param name="now">The current time, in UTC: the rain is forecast from it (or from the departure, if earlier).</param>
    /// <exception cref="WindForecastException">The wind forecast failed.</exception>
    public async Task<TracedRide> AdviseAsync(TracedRoute traced, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(traced);
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Now must be in UTC.", nameof(now));
        }
        var request = traced.Request;
        var (departure, outlook, rainFailure) = await RideForecast.ChooseAsync(
            winds, rain, [traced.Route], request.Start, request.Departure, request.AverageSpeedKmh, now, cancellationToken);
        return new TracedRide(request, traced.Route, traced.Analysis, departure, outlook, rainFailure);
    }

    /// <summary>Both steps at once.</summary>
    /// <exception cref="RouteException">The engine could not join the points (move them) or failed.</exception>
    /// <exception cref="WindForecastException">The wind forecast failed.</exception>
    public async Task<TracedRide> AdviseAsync(TracedRequest request, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        await AdviseAsync(await RouteAsync(request, cancellationToken), now, cancellationToken);
}
