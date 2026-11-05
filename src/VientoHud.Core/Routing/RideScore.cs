using VientoHud.Core.Rain;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>Consecutive legs where the gust across the road goes over the rider's limit.</summary>
/// <param name="StartKm">Where the stretch begins, from the start of the ride.</param>
/// <param name="EndKm">Where it ends.</param>
/// <param name="MaxCrossGustKmh">Strongest gust across the road in the stretch.</param>
public sealed record GustyStretch(double StartKm, double EndKm, double MaxCrossGustKmh)
{
    public double LengthKm => EndKm - StartKm;
}

/// <summary>How good a ride is for the wind (DECISIONS 022).</summary>
/// <param name="FirstHalfHeadwindKmh">Mean wind in the face over the first half of the distance (negative: from behind).</param>
/// <param name="SecondHalfHeadwindKmh">Mean wind in the face over the second half.</param>
/// <param name="GustyStretches">Where the gust across the road goes over the limit, in riding order.</param>
/// <param name="GustsKnown">Whether every leg has a gust forecast; when not, gusty stretches may be missing.</param>
public sealed record RideScore(
    double FirstHalfHeadwindKmh,
    double SecondHalfHeadwindKmh,
    IReadOnlyList<GustyStretch> GustyStretches,
    bool GustsKnown) : IComparable<RideScore>
{
    /// <summary>
    /// The main criterion: wind in the face in the first half minus in the second. Higher is better: the headwind comes
    /// while the legs are fresh and the ride home has it behind.
    /// </summary>
    public double WindAdvantageKmh => FirstHalfHeadwindKmh - SecondHalfHeadwindKmh;

    /// <summary>Km ridden with the gust across the road over the limit.</summary>
    public double GustyKm => GustyStretches.Sum(s => s.LengthKm);

    /// <summary>Better first: fewer gusty km, then a larger wind advantage.</summary>
    public int CompareTo(RideScore? other)
    {
        if (other is null)
        {
            return -1;
        }
        var gusty = GustyKm.CompareTo(other.GustyKm);
        return gusty != 0 ? gusty : other.WindAdvantageKmh.CompareTo(WindAdvantageKmh);
    }
}

/// <summary>One way of riding a route: the route as ridden, the wind on each leg and its score.</summary>
/// <param name="RouteIndex">Position of the route in the list that was ranked.</param>
/// <param name="Route">The route in riding order (already reversed when <paramref name="Reversed"/>).</param>
/// <param name="Reversed">Whether this is the route ridden the other way round from how it was generated.</param>
/// <param name="Winds">The wind on each leg, in riding order.</param>
/// <param name="Score">How good it is for the wind.</param>
/// <param name="Rain">The rain on each leg and the warning for the area; null when the rain was not looked at.</param>
public sealed record RideOption(
    int RouteIndex,
    Route Route,
    bool Reversed,
    IReadOnlyList<LegWind> Winds,
    RideScore Score,
    RideRain? Rain = null)
{
    /// <summary>Km where rain is likely; 0 when the rain was not looked at.</summary>
    public double LikelyRainKm => Rain?.LikelyKm ?? 0;

    /// <summary>How much of the route is in town (DECISIONS 042-043).</summary>
    public UrbanMeasure Urban { get; } = RouteUrban.Measure(Route);
}

/// <summary>Scores rides for the wind and ranks routes ridden both ways (DECISIONS 022 and 035).</summary>
public static class RideRanking
{
    /// <summary>
    /// Rides whose km of likely rain differ by less than this count as equally wet, and the wind decides (the author's
    /// choice, DECISIONS 035): a forecast of rain is not precise to a few km.
    /// </summary>
    public const double RainTieKm = 5;

    /// <summary>
    /// Rides whose km in town differ by less than this count as equally urban, and the wind decides (the author's choice,
    /// DECISIONS 043).
    /// </summary>
    public const double UrbanTieKm = 10;

    /// <summary>
    /// Best first, in the order the author chose (DECISIONS 035 and 043): fewer km with dangerous gusts across; then fewer
    /// km with likely rain, where differences under <see cref="RainTieKm"/> are a tie; then fewer km in town, where
    /// differences under <see cref="UrbanTieKm"/> are a tie; then the larger wind advantage.
    /// </summary>
    /// <remarks>
    /// Ties are not compared pair by pair (0, 4 and 8 km would be a tie two by two but not all three): sorted by the
    /// amount, each group takes the lowest left and every ride within the tie of it.
    /// </remarks>
    public static IReadOnlyList<RideOption> Order(IEnumerable<RideOption> options) =>
        options.GroupBy(o => o.Score.GustyKm).OrderBy(g => g.Key)
            .SelectMany(sameGusts => Ties(sameGusts, o => o.LikelyRainKm, RainTieKm))
            .SelectMany(sameRain => Ties(sameRain, o => o.Urban.UrbanKm, UrbanTieKm))
            .SelectMany(sameTown => sameTown.OrderByDescending(o => o.Score.WindAdvantageKmh))
            .ToList();

    // Groups of rides tied in `amount`, lowest first: each takes the lowest left and every ride within `tie` of it.
    private static IEnumerable<List<RideOption>> Ties(IEnumerable<RideOption> options, Func<RideOption, double> amount, double tie)
    {
        var left = options.OrderBy(amount).ToList();
        while (left.Count > 0)
        {
            var lowest = amount(left[0]);
            var group = left.TakeWhile(o => amount(o) < lowest + tie).ToList();
            yield return group;
            left.RemoveRange(0, group.Count);
        }
    }

    /// <summary>Gust across the road the rider is not comfortable with (the author's choice, DECISIONS 022).</summary>
    public const double DefaultMaxCrossGustKmh = 40;

    /// <summary>The score of a ride from the wind on its legs, in riding order.</summary>
    public static RideScore Score(IReadOnlyList<LegWind> winds, double maxCrossGustKmh = DefaultMaxCrossGustKmh)
    {
        if (winds.Count == 0)
        {
            throw new ArgumentException("A ride needs at least one leg.", nameof(winds));
        }

        var halfKm = winds[^1].Leg.EndKm / 2;
        var first = winds.Where(w => w.Leg.StartKm + w.Leg.LengthKm / 2 < halfKm).ToList();
        var second = winds.Where(w => w.Leg.StartKm + w.Leg.LengthKm / 2 >= halfKm).ToList();
        double Mean(List<LegWind> legs) => legs.Count == 0
            ? 0
            : legs.Sum(w => w.HeadwindKmh * w.Leg.LengthKm) / legs.Sum(w => w.Leg.LengthKm);

        var stretches = new List<GustyStretch>();
        foreach (var w in winds)
        {
            if (w.CrossGustKmh is not { } gust || gust <= maxCrossGustKmh)
            {
                continue;
            }
            if (stretches.Count > 0 && stretches[^1].EndKm >= w.Leg.StartKm - 1e-9)
            {
                var last = stretches[^1];
                stretches[^1] = last with { EndKm = w.Leg.EndKm, MaxCrossGustKmh = Math.Max(last.MaxCrossGustKmh, gust) };
            }
            else
            {
                stretches.Add(new GustyStretch(w.Leg.StartKm, w.Leg.EndKm, gust));
            }
        }

        return new RideScore(Mean(first), Mean(second), stretches, winds.All(w => w.Wind.GustKmh is not null));
    }

    /// <summary>
    /// Every route ridden both ways, best first (<see cref="Order"/>). Asks <paramref name="provider"/> once, at the
    /// wind points of all the routes, for the hours the longest ride lasts. With <paramref name="rain"/>, the rain on
    /// each leg counts too.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static async Task<IReadOnlyList<RideOption>> RankAsync(
        IWindForecastProvider provider,
        IReadOnlyList<Route> routes,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double maxCrossGustKmh = DefaultMaxCrossGustKmh,
        RainOutlook? rain = null,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return [];
        }

        var rides = routes
            .SelectMany((route, i) => new[]
            {
                (Index: i, Route: route, Reversed: false),
                (Index: i, Route: route.Reversed(), Reversed: true),
            })
            .Select(r => (r.Index, r.Route, r.Reversed, Legs: RouteLegs.Split(r.Route, departure, averageSpeedKmh)))
            .ToList();
        var field = await WindField.ForecastAsync(
            provider,
            routes.SelectMany(r => RouteWind.SamplePoints(r)).ToList(),
            departure,
            rides.Max(r => r.Legs[^1].EndTime),
            cancellationToken);

        return Rank(field, routes, departure, averageSpeedKmh, maxCrossGustKmh, rain);
    }

    /// <summary>
    /// Every route ridden both ways, best first, with a wind field already at hand (it must cover the places and hours of
    /// the rides: <see cref="RouteWind.SamplePoints"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The field has no wind for some leg.</exception>
    public static IReadOnlyList<RideOption> Rank(
        WindField field,
        IReadOnlyList<Route> routes,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double maxCrossGustKmh = DefaultMaxCrossGustKmh,
        RainOutlook? rain = null) =>
        Order(routes
            .SelectMany((route, i) => new[] { (Index: i, Route: route, Reversed: false), (Index: i, Route: route.Reversed(), Reversed: true) })
            .Select(r =>
            {
                var legs = RouteLegs.Split(r.Route, departure, averageSpeedKmh);
                var winds = RouteWind.Along(legs, field);
                return new RideOption(r.Index, r.Route, r.Reversed, winds, Score(winds, maxCrossGustKmh),
                    rain is null ? null : RouteRain.Along(legs, rain));
            }));

    /// <summary>Each route once, in its better way round, keeping the order of <paramref name="ranked"/>.</summary>
    public static IReadOnlyList<RideOption> BestWayRound(IReadOnlyList<RideOption> ranked) =>
        ranked.DistinctBy(o => o.RouteIndex).ToList();
}
