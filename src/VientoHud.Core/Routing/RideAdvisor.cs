using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>What the rider asks for.</summary>
/// <param name="Start">Where the ride starts and ends.</param>
/// <param name="TargetKm">How long the routes should be.</param>
/// <param name="Departure">The earliest departure, in UTC; later ones up to 2 h are also tried (DECISIONS 036).</param>
/// <param name="AverageSpeedKmh">Average speed of the whole ride.</param>
/// <param name="Count">How many bearings to look in.</param>
/// <param name="FirstBearing">A bearing chosen by the rider (degrees TOWARDS); null to let the wind choose.</param>
public sealed record RideRequest(
    Coordinate Start,
    double TargetKm,
    DateTimeOffset Departure,
    double AverageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
    int Count = 3,
    double? FirstBearing = null);

/// <summary>Everything the app or the console shows before a ride: the routes, the rain and when to leave.</summary>
/// <param name="Request">What was asked for.</param>
/// <param name="Plan">The routes the planner found for each bearing, valid or not.</param>
/// <param name="Departure">
/// When to leave and the valid routes ranked for each departure; each <see cref="RideOption.RouteIndex"/> points into
/// <paramref name="FoundLoops"/> (use <see cref="LoopOf"/>).
/// </param>
/// <param name="FoundLoops">Positions in <see cref="RidePlan.Loops"/> of the valid routes, in the order they were ranked.</param>
/// <param name="Rain">The rain to come; null when there was no valid route or it could not be asked for.</param>
/// <param name="RainFailure">Why the rain could not be asked for; null otherwise.</param>
public sealed record RideAdvice(
    RideRequest Request,
    RidePlan Plan,
    DepartureAdvice Departure,
    IReadOnlyList<int> FoundLoops,
    RainOutlook? Rain,
    string? RainFailure)
{
    /// <summary>The bearing and shape a ranked option comes from.</summary>
    public LoopResult LoopOf(RideOption option) => Plan.Loops[FoundLoops[option.RouteIndex]];
}

/// <summary>
/// Plans a ride from start to end (phases 3 to 6): the routes for the wind, the rain along them and whether to leave now
/// or wait (DECISIONS 035-036). What the console's <c>route</c> did step by step, so that the app does the same.
/// </summary>
public sealed class RideAdvisor(
    RidePlanner planner,
    IWindForecastProvider winds,
    IRadarSource radar,
    IRainForecastSource rainModel,
    IRainProbabilitySource rainFallback)
{
    /// <param name="request">What the rider asks for.</param>
    /// <param name="now">The current time, in UTC: the rain is forecast from it (or from the departure, if earlier).</param>
    /// <param name="progress">Told about each bearing as soon as it is done (the slow part: one or more requests each).</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <exception cref="WindForecastException">The wind forecast failed. A failed rain forecast is not an error: see
    /// <see cref="RideAdvice.RainFailure"/>.</exception>
    public async Task<RideAdvice> AdviseAsync(
        RideRequest request,
        DateTimeOffset now,
        IProgress<LoopResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Now must be in UTC.", nameof(now));
        }

        var plan = await planner.PlanAsync(
            request.Start, request.TargetKm, request.Departure, request.AverageSpeedKmh, request.Count,
            request.FirstBearing, progress, cancellationToken: cancellationToken);

        // The valid routes, ridden at each departure up to 2 h later with the wind and the rain (DECISIONS 035-036).
        var found = Enumerable.Range(0, plan.Loops.Count).Where(i => plan.Loops[i].Found).ToList();
        var routes = found.Select(i => plan.Loops[i].Best!.Route).ToList();
        var (advice, outlook, rainFailure) = await RideForecast.ChooseAsync(
            winds, radar, rainModel, rainFallback, routes, request.Start, request.Departure, request.AverageSpeedKmh, now,
            cancellationToken);
        return new RideAdvice(request, plan, advice, found, outlook, rainFailure);
    }
}
