using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>The wind a rider meets on one leg, at the moment they ride its middle (DECISIONS 020).</summary>
/// <param name="Leg">The leg.</param>
/// <param name="Wind">The wind at the middle of the leg, at <see cref="RouteLeg.MiddleTime"/>.</param>
public sealed record LegWind(RouteLeg Leg, LocalWind Wind)
{
    // Angle between where the wind comes FROM and where the rider goes TO: 0 = straight in the face.
    private double Relative => (Wind.DirectionFromDegrees - Leg.BearingDegrees) * Math.PI / 180;

    /// <summary>
    /// Wind along the leg, averaged over it: positive in the face (headwind), negative from behind (tailwind).
    /// Taken on the leg's straight line and scaled by its straightness, which gives the exact average along its bends.
    /// </summary>
    public double HeadwindKmh => Wind.SpeedKmh * Math.Cos(Relative) * Leg.Straightness;

    /// <summary>
    /// Wind across the leg's straight line: positive from the rider's right, negative from the left. On a winding leg
    /// it falls short of what the rider feels on the bends.
    /// </summary>
    public double CrosswindKmh => Wind.SpeedKmh * Math.Sin(Relative);

    /// <summary>Strength across the leg's straight line of the gust, from either side; null if there is no gust.</summary>
    public double? CrossGustKmh => Wind.GustKmh is { } gust ? Math.Abs(gust * Math.Sin(Relative)) : null;
}

/// <summary>Wind along a route: where to ask for it, and what each leg meets.</summary>
public static class RouteWind
{
    /// <summary>Distance between the places where the wind is asked for (the author's choice, DECISIONS 020).</summary>
    public const double DefaultSampleSpacingKm = 5;

    /// <summary>
    /// Where to ask for the wind along <paramref name="route"/>: the middles of equal stretches of about
    /// <paramref name="spacingKm"/> (as many as fit, rounded; at least one).
    /// </summary>
    public static IReadOnlyList<Coordinate> SamplePoints(Route route, double spacingKm = DefaultSampleSpacingKm) =>
        // The stretches are legs of that length; their times do not matter here.
        RouteLegs.Split(route, DateTimeOffset.UnixEpoch, legKm: spacingKm).Select(l => l.Middle).ToList();

    /// <summary>The wind each leg meets, from <paramref name="field"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The field has no wind for the time some leg is ridden.</exception>
    public static IReadOnlyList<LegWind> Along(IReadOnlyList<RouteLeg> legs, WindField field) =>
        legs.Select(leg => new LegWind(leg, field.At(leg.Middle, leg.MiddleTime))).ToList();

    /// <summary>
    /// Forecast for a route ridden as <paramref name="legs"/>: asks <paramref name="provider"/> once, at
    /// <see cref="SamplePoints"/> of <paramref name="route"/>, for the hours the ride lasts, and gives each leg its wind.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static async Task<IReadOnlyList<LegWind>> ForecastAsync(
        IWindForecastProvider provider,
        Route route,
        IReadOnlyList<RouteLeg> legs,
        double spacingKm = DefaultSampleSpacingKm,
        CancellationToken cancellationToken = default)
    {
        var field = await WindField.ForecastAsync(
            provider, SamplePoints(route, spacingKm), legs[0].StartTime, legs[^1].EndTime, cancellationToken);
        return Along(legs, field);
    }
}
