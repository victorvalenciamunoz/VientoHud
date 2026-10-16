namespace VientoHud.Core.Rain;

/// <summary>How hard it rains, with AEMET's thresholds for rain rate.</summary>
public enum RainIntensity
{
    /// <summary>Up to 2 mm/h.</summary>
    Light,

    /// <summary>Over 2 and up to 15 mm/h.</summary>
    Moderate,

    /// <summary>Over 15 and up to 30 mm/h.</summary>
    Heavy,

    /// <summary>Over 30 and up to 60 mm/h.</summary>
    VeryHeavy,

    /// <summary>Over 60 mm/h.</summary>
    Torrential,
}

public static class RainIntensities
{
    /// <summary>Intensity of a rain rate in mm/h (AEMET: débil, moderada, fuerte, muy fuerte, torrencial).</summary>
    public static RainIntensity Classify(double mmPerHour)
    {
        if (!double.IsFinite(mmPerHour) || mmPerHour < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mmPerHour), mmPerHour, "Rain rate must be finite and not negative.");
        }
        return mmPerHour switch
        {
            <= 2 => RainIntensity.Light,
            <= 15 => RainIntensity.Moderate,
            <= 30 => RainIntensity.Heavy,
            <= 60 => RainIntensity.VeryHeavy,
            _ => RainIntensity.Torrential,
        };
    }
}
