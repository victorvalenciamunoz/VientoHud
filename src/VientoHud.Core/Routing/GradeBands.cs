namespace VientoHud.Core.Routing;

/// <summary>
/// How steep a stretch of road is, in the bands every view of the app shares (profile, 3D flyover, map): the same
/// thresholds and the same colours everywhere, so a red stretch means the same thing in all of them.
/// </summary>
public enum GradeBand
{
    /// <summary>Going down, below -<see cref="GradeBands.FlatPercent"/>.</summary>
    Downhill,

    /// <summary>Within ±<see cref="GradeBands.FlatPercent"/>: it hardly matters.</summary>
    Flat,

    /// <summary>Up to <see cref="GradeBands.GentlePercent"/>: a false flat, a gear or two.</summary>
    Gentle,

    /// <summary>Up to <see cref="GradeBands.ModeratePercent"/>: a proper climb.</summary>
    Moderate,

    /// <summary>Up to <see cref="GradeBands.SteepPercent"/>: hard.</summary>
    Steep,

    /// <summary>Beyond <see cref="GradeBands.SteepPercent"/>: a wall.</summary>
    VerySteep,
}

/// <summary>The thresholds and colours of the <see cref="GradeBand"/>s. Grades are in percent, positive uphill.</summary>
public static class GradeBands
{
    /// <summary>Up to this (either way) a stretch counts as flat.</summary>
    public const double FlatPercent = 2;

    /// <summary>Upper limit of <see cref="GradeBand.Gentle"/>.</summary>
    public const double GentlePercent = 4;

    /// <summary>Upper limit of <see cref="GradeBand.Moderate"/>.</summary>
    public const double ModeratePercent = 7;

    /// <summary>Upper limit of <see cref="GradeBand.Steep"/>; beyond it, <see cref="GradeBand.VerySteep"/>.</summary>
    public const double SteepPercent = 10;

    /// <summary>The band of a grade in percent (positive uphill).</summary>
    public static GradeBand Of(double gradePercent)
    {
        if (!double.IsFinite(gradePercent))
        {
            throw new ArgumentOutOfRangeException(nameof(gradePercent), gradePercent, "The grade must be a number.");
        }
        return gradePercent switch
        {
            < -FlatPercent => GradeBand.Downhill,
            < FlatPercent => GradeBand.Flat,
            < GentlePercent => GradeBand.Gentle,
            < ModeratePercent => GradeBand.Moderate,
            < SteepPercent => GradeBand.Steep,
            _ => GradeBand.VerySteep,
        };
    }

    /// <summary>
    /// The colour of a band as "#rrggbb", the same for the app's own drawings and for the 3D view's JavaScript: blue
    /// going down, green on the flat, then yellow, orange, red and purple as it steepens (what route sites usually
    /// use, so the author reads it without a legend).
    /// </summary>
    public static string Color(GradeBand band) => band switch
    {
        GradeBand.Downhill => "#4FA3E0",
        GradeBand.Flat => "#7CB342",
        GradeBand.Gentle => "#F2D230",
        GradeBand.Moderate => "#F39C12",
        GradeBand.Steep => "#E0453A",
        GradeBand.VerySteep => "#8E24AA",
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown band."),
    };

    /// <summary>All the bands, from downhill to the steepest, for legends.</summary>
    public static IReadOnlyList<GradeBand> All { get; } = Enum.GetValues<GradeBand>();
}
