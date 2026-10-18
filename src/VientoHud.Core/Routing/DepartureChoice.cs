using VientoHud.Core.Rain;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>The loops ridden from one departure time, each in its better way round, best first.</summary>
public sealed record DepartureOption(DateTimeOffset Departure, IReadOnlyList<RideOption> Ranked)
{
    public RideOption? Best => Ranked.Count == 0 ? null : Ranked[0];
}

/// <summary>When to leave (DECISIONS 036).</summary>
/// <param name="Chosen">The departure to take: the first one, unless waiting is worth it.</param>
/// <param name="First">The departure at the first time looked at (normally "now").</param>
/// <param name="Options">Every departure looked at, in time order.</param>
/// <param name="RainKmSaved">
/// Km of likely rain the chosen departure saves against the first one, over the km counted for both; 0 when not waiting.
/// </param>
/// <param name="GustyKmSaved">Km with dangerous gusts across saved against the first one; 0 when not waiting.</param>
public sealed record DepartureAdvice(
    DepartureOption Chosen,
    DepartureOption First,
    IReadOnlyList<DepartureOption> Options,
    double RainKmSaved,
    double GustyKmSaved)
{
    public bool Wait => Chosen.Departure > First.Departure;
}

/// <summary>
/// Whether to leave now or later (the author's rule, DECISIONS 036): the same loops ridden at each departure time;
/// waiting is worth it only to get wet less or avoid dangerous gusts across, never for the wind alone.
/// </summary>
public static class DepartureChoice
{
    /// <summary>The longest wait looked at (the author's choice).</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromHours(2);

    public static readonly TimeSpan DefaultStep = TimeSpan.FromMinutes(15);

    /// <summary>Departure times from <paramref name="first"/> every <paramref name="step"/> up to the longest wait.</summary>
    public static IReadOnlyList<DateTimeOffset> Times(DateTimeOffset first, TimeSpan? maxWait = null, TimeSpan? step = null)
    {
        var wait = maxWait ?? DefaultMaxWait;
        var every = step ?? DefaultStep;
        if (wait < TimeSpan.Zero || every <= TimeSpan.Zero)
        {
            throw new ArgumentException("The wait must not be negative and the step must be positive.");
        }
        return Enumerable.Range(0, (int)(wait / every) + 1).Select(i => first + i * every).ToList();
    }

    /// <summary>
    /// Ranks <paramref name="routes"/> for each departure in <paramref name="departures"/> (first = "now") with one
    /// wind field and one rain outlook for all of them, and picks when to leave: the first departure, unless a later
    /// one saves dangerous gusts or at least <see cref="RideRanking.RainTieKm"/> km of likely rain. The rain is
    /// compared only over the km counted for both: a later departure has more of its ride beyond the 3 h counted, and
    /// knowing less about it must not make it look drier. Among the departures worth it: the most gusts saved, then the
    /// most rain saved, then the earliest.
    /// </summary>
    public static DepartureAdvice Choose(
        WindField field,
        IReadOnlyList<Route> routes,
        IReadOnlyList<DateTimeOffset> departures,
        RainOutlook? rain,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double maxCrossGustKmh = RideRanking.DefaultMaxCrossGustKmh)
    {
        if (departures.Count == 0)
        {
            throw new ArgumentException("At least one departure is needed.", nameof(departures));
        }
        if (departures.Zip(departures.Skip(1)).Any(p => p.Second <= p.First))
        {
            throw new ArgumentException("Departures must be in time order.", nameof(departures));
        }

        var options = departures
            .Select(d => new DepartureOption(d, RideRanking.BestWayRound(
                RideRanking.Rank(field, routes, d, averageSpeedKmh, maxCrossGustKmh, rain))))
            .ToList();
        var first = options[0];
        if (first.Best is not { } now)
        {
            return new DepartureAdvice(first, first, options, 0, 0);
        }

        var worth = options.Skip(1)
            .Where(o => o.Best is not null)
            .Select(o =>
            {
                var later = o.Best!;
                var gusts = now.Score.GustyKm - later.Score.GustyKm;
                // Over the km counted for the later departure (the fewer), the first one's likely rain against its own.
                var common = later.Rain?.CountedKm ?? 0;
                var rainSaved = (now.Rain?.LikelyKmWithin(common) ?? 0) - later.LikelyRainKm;
                return (Option: o, Gusts: gusts, Rain: rainSaved);
            })
            .Where(x => x.Gusts > 1e-9 && x.Rain > -RideRanking.RainTieKm
                        || Math.Abs(x.Gusts) <= 1e-9 && x.Rain >= RideRanking.RainTieKm)
            .OrderByDescending(x => x.Gusts)
            .ThenByDescending(x => x.Rain)
            .ThenBy(x => x.Option.Departure)
            .FirstOrDefault();

        return worth.Option is null
            ? new DepartureAdvice(first, first, options, 0, 0)
            : new DepartureAdvice(worth.Option, first, options, Math.Max(0, worth.Rain), worth.Gusts);
    }

    /// <summary>
    /// <see cref="Choose"/> asking <paramref name="provider"/> once for the wind at all the routes over every departure
    /// and the longest ride.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static async Task<DepartureAdvice> ChooseAsync(
        IWindForecastProvider provider,
        IReadOnlyList<Route> routes,
        IReadOnlyList<DateTimeOffset> departures,
        RainOutlook? rain,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double maxCrossGustKmh = RideRanking.DefaultMaxCrossGustKmh,
        CancellationToken cancellationToken = default)
    {
        if (departures.Count == 0)
        {
            throw new ArgumentException("At least one departure is needed.", nameof(departures));
        }
        if (routes.Count == 0)
        {
            var none = departures.Select(d => new DepartureOption(d, [])).ToList();
            return new DepartureAdvice(none[0], none[0], none, 0, 0);
        }
        var longest = routes.Max(r => RouteLegs.Split(r, departures[^1], averageSpeedKmh)[^1].EndTime);
        var field = await WindField.ForecastAsync(
            provider, routes.SelectMany(r => RouteWind.SamplePoints(r)).ToList(), departures[0], longest, cancellationToken);
        return Choose(field, routes, departures, rain, averageSpeedKmh, maxCrossGustKmh);
    }
}
