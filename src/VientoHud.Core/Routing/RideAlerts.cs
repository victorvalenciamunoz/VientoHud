using VientoHud.Core.Rain;

namespace VientoHud.Core.Routing;

/// <summary>What a point on the way warns about (DECISIONS 037).</summary>
public enum RideAlertKind
{
    /// <summary>A stretch with dangerous gusts across begins.</summary>
    Gusts,

    /// <summary>A stretch with likely rain begins.</summary>
    Rain,

    /// <summary>The part of the ride covered only by the warning for the area begins.</summary>
    AreaShowers,
}

/// <summary>A point on the way to mark in the GPX.</summary>
/// <param name="Position">Where the stretch begins (start of its first leg).</param>
/// <param name="StartKm">Distance from the start of the ride.</param>
/// <param name="EndKm">Where the stretch ends; equal to <paramref name="StartKm"/> for the area warning.</param>
/// <param name="Time">When the rider gets there, in UTC.</param>
/// <param name="Value">
/// Strongest gust across (km/h) for <see cref="RideAlertKind.Gusts"/>; highest chance of rain in [0, 1] for
/// <see cref="RideAlertKind.Rain"/> and <see cref="RideAlertKind.AreaShowers"/>.
/// </param>
public sealed record RideAlert(
    RideAlertKind Kind,
    Coordinate Position,
    double StartKm,
    double EndKm,
    DateTimeOffset Time,
    double Value);

/// <summary>
/// The points worth marking on a ride (the author's choice, DECISIONS 037): where each stretch with dangerous gusts
/// across begins, where each stretch with likely rain begins and where the warning for the area begins. Not the wind:
/// there would be too many and it is no danger.
/// </summary>
public static class RideAlerts
{
    /// <summary>Most points marked, so the map is not cluttered.</summary>
    public const int DefaultMaxCount = 10;

    /// <summary>
    /// The alerts of a ride in riding order. With more than <paramref name="maxCount"/>, gusts are kept first, then
    /// rain (the longest stretches), then the area warning.
    /// </summary>
    public static IReadOnlyList<RideAlert> For(RideOption option, int maxCount = DefaultMaxCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);
        var legs = option.Winds.Select(w => w.Leg).ToList();
        var alerts = new List<RideAlert>();

        foreach (var stretch in option.Score.GustyStretches)
        {
            var leg = legs.First(l => l.EndKm > stretch.StartKm + 1e-9);
            alerts.Add(new RideAlert(RideAlertKind.Gusts, leg.Start, stretch.StartKm, stretch.EndKm, leg.StartTime, stretch.MaxCrossGustKmh));
        }

        if (option.Rain is { } rain)
        {
            var run = new List<LegRain>();
            foreach (var legRain in rain.Legs.Append(null))
            {
                if (legRain?.Rain.Likelihood == RainLikelihood.Likely)
                {
                    run.Add(legRain);
                    continue;
                }
                if (run.Count > 0)
                {
                    alerts.Add(new RideAlert(RideAlertKind.Rain, run[0].Leg.Start, run[0].Leg.StartKm, run[^1].Leg.EndKm,
                        run[0].Leg.StartTime, run.Max(r => r.Rain.Probability!.Value)));
                    run.Clear();
                }
            }

            if (rain.AreaWarning is { } warning
                && rain.Legs.FirstOrDefault(l => l.Rain.Source == RainSourceKind.BeyondHorizon) is { } beyond)
            {
                alerts.Add(new RideAlert(RideAlertKind.AreaShowers, beyond.Leg.Start, beyond.Leg.StartKm, beyond.Leg.StartKm,
                    beyond.Leg.StartTime, warning.MaxProbability));
            }
        }

        return alerts
            .OrderBy(a => a.Kind)
            .ThenByDescending(a => a.EndKm - a.StartKm)
            .Take(maxCount)
            .OrderBy(a => a.StartKm)
            .ThenBy(a => a.Kind)
            .ToList();
    }
}
