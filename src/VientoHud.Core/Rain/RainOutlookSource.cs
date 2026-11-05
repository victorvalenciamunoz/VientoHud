using VientoHud.Core.Rain.Forecasts;

namespace VientoHud.Core.Rain;

/// <summary>The rain to come around a place, as far and as late as a ride goes.</summary>
public interface IRainOutlookSource
{
    /// <param name="start">Where the ride starts.</param>
    /// <param name="reachKm">How far from the start it goes.</param>
    /// <param name="now">The current time, in UTC; the outlook is made from it.</param>
    /// <param name="until">When the latest ride ends, in UTC.</param>
    /// <exception cref="RadarException">The radar failed.</exception>
    /// <exception cref="RainForecastException">The fallback failed.</exception>
    Task<RainOutlook> ForecastAsync(
        Coordinate start, double reachKm, DateTimeOffset now, DateTimeOffset until, CancellationToken cancellationToken = default);
}

/// <summary>Asks the radar, the model and the fallback every time (<see cref="RainOutlook.ForecastAsync"/>).</summary>
public sealed class RainOutlookSource(
    IRadarSource radar, IRainForecastSource model, IRainProbabilitySource fallback, RainOutlookOptions? options = null)
    : IRainOutlookSource
{
    public Task<RainOutlook> ForecastAsync(
        Coordinate start, double reachKm, DateTimeOffset now, DateTimeOffset until, CancellationToken cancellationToken = default) =>
        RainOutlook.ForecastAsync(radar, model, fallback, start, reachKm, now, until, options, cancellationToken);
}

/// <summary>
/// Keeps the last outlook and answers with it while it still serves (DECISIONS 049): the same place (within
/// <see cref="MaxMoveKm"/>), not older than <see cref="MaxAge"/>, and covering the reach and the hours asked. When
/// it does ask, it asks a little farther and later than needed, so the next point touched nearby needs no new
/// request: the rain does not change for moving a point 2 km, but every request costs radar frames and up to 225
/// calls to Open-Meteo.
/// </summary>
public sealed class CachedRainOutlookSource(
    IRainOutlookSource inner,
    TimeSpan? maxAge = null,
    double maxMoveKm = 1,
    double reachPaddingKm = 10,
    TimeSpan? untilPadding = null) : IRainOutlookSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (Coordinate Start, double ReachKm, DateTimeOffset Until, RainOutlook Outlook)? _last;

    public TimeSpan MaxAge { get; } = maxAge ?? TimeSpan.FromMinutes(10);

    public double MaxMoveKm { get; } = maxMoveKm;

    public double ReachPaddingKm { get; } = reachPaddingKm;

    public TimeSpan UntilPadding { get; } = untilPadding ?? TimeSpan.FromHours(1);

    /// <summary>How many times the inner source was asked.</summary>
    public int Requests { get; private set; }

    public async Task<RainOutlook> ForecastAsync(
        Coordinate start, double reachKm, DateTimeOffset now, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_last is { } last
                && last.Start.DistanceKmTo(start) <= MaxMoveKm
                && last.ReachKm >= reachKm
                && last.Until >= until
                && now >= last.Outlook.Now
                && now - last.Outlook.Now <= MaxAge)
            {
                return last.Outlook;
            }
            var reach = reachKm + ReachPaddingKm;
            var late = until + UntilPadding;
            var outlook = await inner.ForecastAsync(start, reach, now, late, cancellationToken);
            Requests++;
            _last = (start, reach, late, outlook);
            return outlook;
        }
        finally
        {
            _gate.Release();
        }
    }
}
