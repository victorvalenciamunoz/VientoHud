using System.Globalization;
using VientoHud.Core.Location;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Replay;

/// <summary>
/// A past moment and place to live again (phase 9, step 4): the app's clock starts there and runs on, the rider is
/// there, and every source answers with what was known then, as the judge of phase 5 did: the radar up to five
/// minutes before (OPERA's archive), the last AROME run already out (~40 min after its start), and the wind as the
/// short-range forecast stood. For development builds only (<c>VIENTOHUD_APP_REPLAY</c>), to see rain without waiting
/// for it.
/// </summary>
/// <param name="Start">The moment, in UTC.</param>
/// <param name="Place">Where the rider is.</param>
public sealed record ReplayDay(DateTimeOffset Start, Coordinate Place)
{
    /// <summary>The radar frames this much before the clock are already out (as in the judge of phase 5).</summary>
    public static readonly TimeSpan RadarDelay = TimeSpan.FromMinutes(5);

    /// <summary>An AROME run is out this long after its start.</summary>
    public static readonly TimeSpan RunDelay = TimeSpan.FromMinutes(40);

    /// <summary>Reads "2026-10-19T10:00Z;45.1000;1.2340" (UTC moment; latitude; longitude).</summary>
    public static bool TryParse(string? text, out ReplayDay day)
    {
        day = null!;
        var parts = text?.Split(';', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 3 }
            || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return false;
        }
        day = new ReplayDay(start.ToUniversalTime(), new Coordinate(latitude, longitude));
        return true;
    }

    /// <summary>A clock that starts at <see cref="Start"/> when created and runs at the real pace.</summary>
    public TimeProvider Clock(TimeProvider? real = null) => new ReplayClock(Start, real ?? TimeProvider.System);

    /// <summary>The rider stands at <see cref="Place"/>, measured now by <paramref name="clock"/>.</summary>
    public ILocationProvider Location(TimeProvider clock) => new FixedLocation(Place, clock);

    /// <summary>The radar as it was: frames up to <see cref="RadarDelay"/> before the clock, from <paramref name="radar"/>.</summary>
    public IRadarSource Radar(IRadarSource radar, TimeProvider clock) => new PastRadar(radar, clock);

    /// <summary>The last AROME run already out at <see cref="Start"/> (runs start every 3 h).</summary>
    public DateTimeOffset LastRun
    {
        get
        {
            var out_ = Start - RunDelay;
            return new DateTimeOffset(out_.Year, out_.Month, out_.Day, out_.Hour / 3 * 3, 0, 0, TimeSpan.Zero);
        }
    }

    /// <summary>The rain model as it was (<see cref="LastRun"/>).</summary>
    public IRainForecastSource RainModel(HttpClient http) => new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.SingleRun(LastRun));

    /// <summary>The fallback beyond the model, from the archive.</summary>
    public IRainProbabilitySource RainFallback(HttpClient http) => new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.Archive);

    /// <summary>The wind as the short-range forecast stood (AROME, else the best match), from the archive.</summary>
    public IWindForecastProvider Wind(HttpClient http) =>
        new ArchivedWindForecast(new OpenMeteoPastWindForecastSource(http), ForecastLead.ShortRange);

    private sealed class ReplayClock(DateTimeOffset start, TimeProvider real) : TimeProvider
    {
        private readonly DateTimeOffset _realStart = real.GetUtcNow();

        public override DateTimeOffset GetUtcNow() => start + (real.GetUtcNow() - _realStart);
    }

    private sealed class FixedLocation(Coordinate place, TimeProvider clock) : ILocationProvider
    {
        public Task<LocationFix> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocationFix(place, 5, clock.GetUtcNow()));
    }

    // OPERA as it was at the clock's time: no frame after it (minus the publishing delay) exists yet.
    private sealed class PastRadar(IRadarSource inner, TimeProvider clock) : IRadarSource
    {
        public TimeSpan FrameInterval => inner.FrameInterval;

        public string Attribution => inner.Attribution;

        private DateTimeOffset Latest => RadarFrames.AtOrBefore(clock.GetUtcNow() - RadarDelay, inner.FrameInterval);

        public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) => Task.FromResult(Latest);

        public Task<RainGrid?> GetFrameAsync(DateTimeOffset time, Coordinate center, double radiusKm, CancellationToken cancellationToken = default) =>
            time > Latest ? Task.FromResult<RainGrid?>(null) : inner.GetFrameAsync(time, center, radiusKm, cancellationToken);
    }
}

/// <summary>
/// Past forecasts as if they were live, for one lead (moved here from the judge of phase 5): AROME, and the best
/// match where AROME lacks an hour or a place.
/// </summary>
public sealed class ArchivedWindForecast(IPastWindForecastSource archive, ForecastLead lead) : IWindForecastProvider
{
    public async Task<WindForecast> GetHourlyAsync(Coordinate location, DateTimeOffset from, int hours, CancellationToken cancellationToken = default) =>
        (await GetHourlyAsync([location], from, hours, cancellationToken))[0];

    public async Task<IReadOnlyList<WindForecast>> GetHourlyAsync(
        IReadOnlyList<Coordinate> locations, DateTimeOffset from, int hours, CancellationToken cancellationToken = default)
    {
        foreach (var model in new[] { "meteofrance_arome_france", "best_match" })
        {
            var past = await archive.GetAsync(locations, model, lead, from, from.AddHours(hours), cancellationToken);
            if (past.All(p => p.Hours.Count == hours && p.GridPoint is not null))
            {
                return past.Select(p => new WindForecast(p.Requested, p.GridPoint!.Value, 0, p.Hours, OpenMeteoPastWindForecastSource.Attribution)).ToList();
            }
        }
        throw new WindForecastException($"No archived {lead} forecast for every place and hour from {from:u}.");
    }
}
