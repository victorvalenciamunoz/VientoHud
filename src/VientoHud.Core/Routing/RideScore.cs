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
/// <param name="Route">The route in riding order (already reversed when <paramref name="Reversed"/>).</param>
/// <param name="Reversed">Whether this is the route ridden the other way round from how it was generated.</param>
/// <param name="Winds">The wind on each leg, in riding order.</param>
/// <param name="Score">How good it is for the wind.</param>
public sealed record RideOption(Route Route, bool Reversed, IReadOnlyList<LegWind> Winds, RideScore Score);

/// <summary>Scores rides for the wind and ranks routes ridden both ways (DECISIONS 022).</summary>
public static class RideRanking
{
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
    /// Every route ridden both ways, best first. Asks <paramref name="provider"/> once, at the wind points of all the
    /// routes, for the hours the longest ride lasts.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static async Task<IReadOnlyList<RideOption>> RankAsync(
        IWindForecastProvider provider,
        IReadOnlyList<Route> routes,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        double maxCrossGustKmh = DefaultMaxCrossGustKmh,
        CancellationToken cancellationToken = default)
    {
        if (routes.Count == 0)
        {
            return [];
        }

        var rides = routes
            .SelectMany(route => new[] { (Route: route, Reversed: false), (Route: route.Reversed(), Reversed: true) })
            .Select(r => (r.Route, r.Reversed, Legs: RouteLegs.Split(r.Route, departure, averageSpeedKmh)))
            .ToList();
        var field = await WindField.ForecastAsync(
            provider,
            routes.SelectMany(r => RouteWind.SamplePoints(r)).ToList(),
            departure,
            rides.Max(r => r.Legs[^1].EndTime),
            cancellationToken);

        return rides
            .Select(r =>
            {
                var winds = RouteWind.Along(r.Legs, field);
                return new RideOption(r.Route, r.Reversed, winds, Score(winds, maxCrossGustKmh));
            })
            .OrderBy(o => o.Score)
            .ToList();
    }
}
