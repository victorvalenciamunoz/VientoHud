using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>
/// An arrow on the map showing where the wind goes (a wind FROM the west points east: a push). The shaft from
/// <paramref name="Tail"/> to <paramref name="Tip"/>, the head from <paramref name="HeadLeft"/> to the tip to
/// <paramref name="HeadRight"/>.
/// </summary>
/// <param name="SpeedKmh">The wind's speed, for the stroke.</param>
/// <param name="Feel">How it feels to the rider there, for the color; null when it is not part of a ride.</param>
/// <param name="AtKm">Where along the ride it stands, for its label; 0 when it is not part of a ride.</param>
public sealed record WindArrow(
    Coordinate Tail, Coordinate Tip, Coordinate HeadLeft, Coordinate HeadRight, double SpeedKmh, WindFeel? Feel, double AtKm = 0);

/// <summary>Wind arrows along a ride (phase 7e): one every few km where the wind is felt.</summary>
public static class WindArrows
{
    /// <summary>Spacing and length of the arrows along a ride.</summary>
    public const double DefaultEveryKm = 5;

    public const double DefaultLengthKm = 1.6;

    /// <summary>The head's arms, as a share of the length, swept back from the tip.</summary>
    public const double HeadShare = 0.3;

    public const double HeadAngleDegrees = 150;

    /// <summary>An arrow centred on <paramref name="centre"/> for a wind blowing FROM <paramref name="windFromDegrees"/>.</summary>
    public static WindArrow At(Coordinate centre, double windFromDegrees, double speedKmh, double lengthKm, WindFeel? feel = null)
    {
        var towards = (windFromDegrees + 180) % 360;
        var tip = centre.Destination(towards, lengthKm / 2);
        var tail = centre.Destination(windFromDegrees, lengthKm / 2);
        var arm = lengthKm * HeadShare;
        return new WindArrow(
            tail, tip,
            tip.Destination((towards + HeadAngleDegrees) % 360, arm),
            tip.Destination((towards - HeadAngleDegrees + 360) % 360, arm),
            speedKmh, feel);
    }

    /// <summary>
    /// One arrow every <paramref name="everyKm"/> along a ride (at km 2.5, 7.5…) with the wind the rider meets there at
    /// the time they pass, colored by how it feels; none where it is light (its direction would be noise).
    /// </summary>
    public static IReadOnlyList<WindArrow> AlongRide(
        IReadOnlyList<LegWind> winds, double everyKm = DefaultEveryKm, double lengthKm = DefaultLengthKm)
    {
        ArgumentNullException.ThrowIfNull(winds);
        var arrows = new List<WindArrow>();
        if (winds.Count == 0)
        {
            return arrows;
        }
        var total = winds[^1].Leg.EndKm;
        for (var km = everyKm / 2; km < total; km += everyKm)
        {
            var at = km;
            var leg = winds.First(w => w.Leg.EndKm >= at);
            var feel = WindStretches.FeelOf(leg);
            if (feel != WindFeel.Light)
            {
                arrows.Add(At(leg.Leg.Middle, leg.Wind.DirectionFromDegrees, leg.Wind.SpeedKmh, lengthKm, feel) with { AtKm = at });
            }
        }
        return arrows;
    }
}
