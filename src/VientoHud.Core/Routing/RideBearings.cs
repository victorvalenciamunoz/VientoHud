using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>Which bearings to look for loops in, given the wind of the ride (DECISIONS 023).</summary>
public static class RideBearings
{
    /// <summary>Angle between the loop straight into the wind and the next ones (the author's choice).</summary>
    public const double DefaultSpreadDegrees = 45;

    /// <summary>
    /// Route bearings (degrees TOWARDS) for <paramref name="count"/> loops: when there is a clear wind, the first one
    /// straight into it (towards where it comes FROM) and the rest alternately <paramref name="spreadDegrees"/> to
    /// either side, then twice that, and so on; when the wind is calm or variable it does not matter, so they are
    /// spread evenly from north (<see cref="LoopGenerator.SpreadBearings"/>).
    /// </summary>
    public static IReadOnlyList<double> For(
        StartDirectionAdvice wind,
        int count,
        double spreadDegrees = DefaultSpreadDegrees)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (wind.Kind != StartAdviceKind.HeadIntoWind || wind.StartBearingDegrees is not { } intoWind)
        {
            return LoopGenerator.SpreadBearings(count);
        }

        // 0, +1, -1, +2, -2, ... steps of the spread.
        return Enumerable.Range(0, count)
            .Select(i => (i + 1) / 2 * (i % 2 == 1 ? 1 : -1) * spreadDegrees)
            .Select(offset => ((intoWind + offset) % 360 + 360) % 360)
            .ToList();
    }

    /// <summary>
    /// Bearings to widen the search with when no loop into the wind is valid, in order: 90° to either side, then
    /// with the wind behind (180°). Those within a degree of <paramref name="tried"/> are left out.
    /// </summary>
    public static IReadOnlyList<double> Wider(double intoWindDegrees, IReadOnlyList<double> tried) =>
        new[] { 90.0, -90, 180 }
            .Select(offset => ((intoWindDegrees + offset) % 360 + 360) % 360)
            .Where(b => tried.All(t => Compass.AngleBetween(t, b) >= 1))
            .ToList();
}
