namespace VientoHud.Core.Rain;

/// <summary>Radar reflectivity to rain rate.</summary>
public static class Reflectivity
{
    /// <summary>
    /// Above this the echo is usually hail or a very wet target, and the Z-R relation gives absurd rates, so the
    /// reflectivity is capped here (≈ 100 mm/h) before converting. Common practice in radar hydrology.
    /// </summary>
    public const double CapDbz = 55;

    /// <summary>
    /// Rain rate in mm/h from reflectivity in dBZ with the Marshall-Palmer relation Z = 200·R^1.6, the standard for
    /// stratiform rain (Z in mm⁶/m³, Z = 10^(dBZ/10)). 20 dBZ ≈ 0.6 mm/h, 30 ≈ 2.7, 40 ≈ 11.5, 50 ≈ 48.6.
    /// </summary>
    public static double ToRainRate(double dbz)
    {
        if (!double.IsFinite(dbz))
        {
            throw new ArgumentOutOfRangeException(nameof(dbz), dbz, "Reflectivity must be finite.");
        }
        var z = Math.Pow(10, Math.Min(dbz, CapDbz) / 10);
        return Math.Pow(z / 200, 1 / 1.6);
    }
}
