using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>
/// What comes after the routes are at hand, for any advisor: the rain around them and when to leave
/// (DECISIONS 035-036).
/// </summary>
internal static class RideForecast
{
    /// <summary>
    /// Asks for the rain as far as the routes go, from <paramref name="now"/> (or the departure, if earlier) to the end
    /// of the latest ride, and ranks the routes both ways round at each departure up to 2 h after
    /// <paramref name="departure"/>. A failed rain forecast is not an error: the advice goes without it, with why.
    /// </summary>
    /// <exception cref="WindForecastException">The wind forecast failed.</exception>
    public static async Task<(DepartureAdvice Departure, RainOutlook? Rain, string? RainFailure)> ChooseAsync(
        IWindForecastProvider winds,
        IRainOutlookSource rain,
        IReadOnlyList<Route> routes,
        Coordinate start,
        DateTimeOffset departure,
        double averageSpeedKmh,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var departures = DepartureChoice.Times(departure);
        RainOutlook? outlook = null;
        string? rainFailure = null;
        if (routes.Count > 0)
        {
            var reach = routes.SelectMany(r => r.Points).Max(p => p.Position.DistanceKmTo(start));
            var last = routes.Max(r => RouteLegs.Split(r, departures[^1], averageSpeedKmh)[^1].EndTime);
            try
            {
                outlook = await rain.ForecastAsync(start, reach, now < departure ? now : departure, last, cancellationToken);
            }
            catch (Exception ex) when (ex is RadarException or RainForecastException)
            {
                rainFailure = ex.Message;
            }
        }
        var advice = await DepartureChoice.ChooseAsync(
            winds, routes, departures, outlook, averageSpeedKmh, cancellationToken: cancellationToken);
        return (advice, outlook, rainFailure);
    }
}
