using VientoHud.Core.Rain;

namespace VientoHud.Core.Routing;

/// <summary>The chance of rain on one leg, at its middle, when the rider is there.</summary>
public sealed record LegRain(RouteLeg Leg, PlaceRain Rain);

/// <summary>The rain a ride may meet: leg by leg up to 3 h from now, and a warning for the area after that.</summary>
/// <param name="AreaWarning">For the legs ridden more than 3 h from now; null when none are, or nothing is expected.</param>
public sealed record RideRain(IReadOnlyList<LegRain> Legs, AreaRain? AreaWarning)
{
    /// <summary>Km of legs where rain is likely (above 50 %).</summary>
    public double LikelyKm => Legs.Where(l => l.Rain.Likelihood == RainLikelihood.Likely).Sum(l => l.Leg.LengthKm);

    /// <summary>Km of legs where rain is possible (20 % to 50 %).</summary>
    public double PossibleKm => Legs.Where(l => l.Rain.Likelihood == RainLikelihood.Possible).Sum(l => l.Leg.LengthKm);

    /// <summary>Km of legs not counted (ridden more than 3 h from now) or with nothing known.</summary>
    public double UncountedKm => Legs.Where(l => l.Rain.Probability is null).Sum(l => l.Leg.LengthKm);

    /// <summary>Km from the start whose rain is counted (up to 3 h from now); the rest is only warned about.</summary>
    public double CountedKm => Legs
        .Where(l => l.Rain.Source != RainSourceKind.BeyondHorizon)
        .Select(l => l.Leg.EndKm)
        .DefaultIfEmpty(0)
        .Max();

    /// <summary>Km with likely rain among the legs that start before <paramref name="km"/> from the start.</summary>
    public double LikelyKmWithin(double km) => Legs
        .Where(l => l.Leg.StartKm < km - 1e-9 && l.Rain.Likelihood == RainLikelihood.Likely)
        .Sum(l => l.Leg.LengthKm);

    /// <summary>First leg where rain is at least possible; null if none.</summary>
    public LegRain? FirstRain => Legs.FirstOrDefault(l => l.Rain.Likelihood is RainLikelihood.Possible or RainLikelihood.Likely);
}

/// <summary>Rain along a route (DECISIONS 034).</summary>
public static class RouteRain
{
    /// <summary>The chance of rain on each leg, at its middle and at the time the rider is there, from the outlook.</summary>
    public static RideRain Along(IReadOnlyList<RouteLeg> legs, RainOutlook outlook)
    {
        var rain = legs.Select(leg => new LegRain(leg, outlook.At(leg.Middle, leg.MiddleTime))).ToList();
        var beyond = rain.FirstOrDefault(l => l.Rain.Source == RainSourceKind.BeyondHorizon);
        var warning = beyond is null ? null : outlook.AreaWarning(beyond.Leg.StartTime, legs[^1].EndTime);
        return new RideRain(rain, warning);
    }
}
