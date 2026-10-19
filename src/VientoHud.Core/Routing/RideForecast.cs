using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>
/// What comes after the routes are at hand, for any advisor: the wind along them, the rain around them and when to
/// leave (DECISIONS 035-036). In pieces, so the app can show the wind (one request) before the rain (radar frames).
/// </summary>
internal static class RideForecast
{
    /// <summary>The wind at the routes' sample points over every departure up to 2 h after <paramref name="departure"/>.</summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static Task<WindField> WindAsync(
        IWindForecastProvider winds,
        IReadOnlyList<Route> routes,
        IReadOnlyList<DateTimeOffset> departures,
        double averageSpeedKmh,
        CancellationToken cancellationToken)
    {
        var longest = routes.Max(r => RouteLegs.Split(r, departures[^1], averageSpeedKmh)[^1].EndTime);
        return WindField.ForecastAsync(
            winds, routes.SelectMany(r => RouteWind.SamplePoints(r)).ToList(), departures[0], longest, cancellationToken);
    }

    /// <summary>
    /// The rain as far as the routes go, from <paramref name="now"/> (or the departure, if earlier) to the end of the
    /// latest ride. A failed forecast is not an error: null, with why.
    /// </summary>
    public static async Task<(RainOutlook? Rain, string? RainFailure)> RainAsync(
        IRainOutlookSource rain,
        IReadOnlyList<Route> routes,
        Coordinate start,
        DateTimeOffset departure,
        IReadOnlyList<DateTimeOffset> departures,
        double averageSpeedKmh,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (routes.Count == 0)
        {
            return (null, null);
        }
        var reach = routes.SelectMany(r => r.Points).Max(p => p.Position.DistanceKmTo(start));
        var last = routes.Max(r => RouteLegs.Split(r, departures[^1], averageSpeedKmh)[^1].EndTime);
        try
        {
            return (await rain.ForecastAsync(start, reach, now < departure ? now : departure, last, cancellationToken), null);
        }
        catch (Exception ex) when (ex is RadarException or RainForecastException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>The rain, then the routes ranked both ways round at each departure, all at once.</summary>
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
        var (outlook, rainFailure) = await RainAsync(rain, routes, start, departure, departures, averageSpeedKmh, now, cancellationToken);
        var advice = await DepartureChoice.ChooseAsync(
            winds, routes, departures, outlook, averageSpeedKmh, cancellationToken: cancellationToken);
        return (advice, outlook, rainFailure);
    }
}
