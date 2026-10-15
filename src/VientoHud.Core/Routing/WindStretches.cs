namespace VientoHud.Core.Routing;

/// <summary>How the wind feels to the rider along a stretch.</summary>
public enum WindFeel
{
    /// <summary>Mostly in the face.</summary>
    Headwind,

    /// <summary>Mostly from behind.</summary>
    Tailwind,

    /// <summary>Mostly across, from the rider's right.</summary>
    FromRight,

    /// <summary>Mostly across, from the rider's left.</summary>
    FromLeft,

    /// <summary>Too weak to notice.</summary>
    Light,
}

/// <summary>A stretch of a ride with the same feel of the wind, to explain a ride in a few lines.</summary>
/// <param name="StartKm">Where the stretch begins, from the start of the ride.</param>
/// <param name="EndKm">Where it ends.</param>
/// <param name="Feel">How the wind feels on most of it.</param>
/// <param name="MeanKmh">
/// Average strength of the wind as it feels: in the face for <see cref="WindFeel.Headwind"/>, from behind for
/// <see cref="WindFeel.Tailwind"/>, across for the sides, and the plain speed for <see cref="WindFeel.Light"/>.
/// </param>
/// <param name="MaxCrossGustKmh">Strongest gust across the road in the stretch; null if no gust was forecast.</param>
public sealed record WindStretch(double StartKm, double EndKm, WindFeel Feel, double MeanKmh, double? MaxCrossGustKmh)
{
    public double LengthKm => EndKm - StartKm;
}

/// <summary>Groups the legs of a ride into a few stretches with the same feel of the wind (DECISIONS 023).</summary>
public static class WindStretches
{
    /// <summary>Stretches shorter than this are folded into a neighbour, so the explanation stays short.</summary>
    public const double DefaultMinKm = 5;

    /// <summary>A leg where neither the wind in the face nor across reaches this feels <see cref="WindFeel.Light"/>.</summary>
    public const double LightBelowKmh = 5;

    /// <summary>The ride as stretches of at least <paramref name="minKm"/> (unless the whole ride is shorter).</summary>
    public static IReadOnlyList<WindStretch> Describe(IReadOnlyList<LegWind> winds, double minKm = DefaultMinKm)
    {
        if (winds.Count == 0)
        {
            return [];
        }

        // Runs of consecutive legs: (feel, first leg, last leg).
        var runs = new List<(WindFeel Feel, int First, int Last)>();
        for (var i = 0; i < winds.Count; i++)
        {
            var feel = FeelOf(winds[i]);
            if (runs.Count > 0 && runs[^1].Feel == feel)
            {
                runs[^1] = runs[^1] with { Last = i };
            }
            else
            {
                runs.Add((feel, i, i));
            }
        }

        double Length((WindFeel Feel, int First, int Last) run) => winds[run.Last].Leg.EndKm - winds[run.First].Leg.StartKm;

        // Fold the shortest run into its longer neighbour until all are long enough.
        while (runs.Count > 1)
        {
            var shortest = Enumerable.Range(0, runs.Count).MinBy(i => Length(runs[i]));
            if (Length(runs[shortest]) >= minKm)
            {
                break;
            }
            var into = shortest == 0 ? 1
                : shortest == runs.Count - 1 ? shortest - 1
                : Length(runs[shortest - 1]) >= Length(runs[shortest + 1]) ? shortest - 1 : shortest + 1;
            var merged = (runs[into].Feel, Math.Min(runs[into].First, runs[shortest].First), Math.Max(runs[into].Last, runs[shortest].Last));
            runs[into] = merged;
            runs.RemoveAt(shortest);
            // Neighbours that now share a feel become one.
            for (var i = runs.Count - 1; i > 0; i--)
            {
                if (runs[i].Feel == runs[i - 1].Feel)
                {
                    runs[i - 1] = runs[i - 1] with { Last = runs[i].Last };
                    runs.RemoveAt(i);
                }
            }
        }

        return runs.Select(run =>
        {
            var legs = winds.Skip(run.First).Take(run.Last - run.First + 1).ToList();
            var km = legs.Sum(w => w.Leg.LengthKm);
            double Mean(Func<LegWind, double> value) => legs.Sum(w => value(w) * w.Leg.LengthKm) / km;
            var mean = run.Feel switch
            {
                WindFeel.Headwind => Mean(w => w.HeadwindKmh),
                WindFeel.Tailwind => Mean(w => -w.HeadwindKmh),
                WindFeel.FromRight => Mean(w => w.CrosswindKmh),
                WindFeel.FromLeft => Mean(w => -w.CrosswindKmh),
                _ => Mean(w => w.Wind.SpeedKmh),
            };
            var gusts = legs.Where(w => w.CrossGustKmh is not null).Select(w => w.CrossGustKmh!.Value).ToList();
            return new WindStretch(
                legs[0].Leg.StartKm, legs[^1].Leg.EndKm, run.Feel, mean, gusts.Count > 0 ? gusts.Max() : null);
        }).ToList();
    }

    private static WindFeel FeelOf(LegWind wind)
    {
        var head = wind.HeadwindKmh;
        var cross = wind.CrosswindKmh;
        if (Math.Max(Math.Abs(head), Math.Abs(cross)) < LightBelowKmh)
        {
            return WindFeel.Light;
        }
        return Math.Abs(head) >= Math.Abs(cross)
            ? head > 0 ? WindFeel.Headwind : WindFeel.Tailwind
            : cross > 0 ? WindFeel.FromRight : WindFeel.FromLeft;
    }
}
