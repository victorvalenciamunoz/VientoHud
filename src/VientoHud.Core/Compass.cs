namespace VientoHud.Core;

/// <summary>The 16 points of the compass, clockwise from north.</summary>
public enum CompassPoint { N, NNE, NE, ENE, E, ESE, SE, SSE, S, SSW, SW, WSW, W, WNW, NW, NNW }

public static class Compass
{
    private const double SectorDegrees = 360.0 / 16;

    /// <summary>
    /// Nearest of the 16 compass points to an angle in degrees clockwise from north. Works for any convention:
    /// the caller decides whether the angle is a wind direction (FROM) or a bearing (TO).
    /// Each sector is half-open, so 11.25° is NNE, not N.
    /// </summary>
    public static CompassPoint ToCompassPoint(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Angle must be finite.");
        }

        var normalized = (degrees % 360 + 360) % 360;
        var sector = (int)Math.Floor(normalized / SectorDegrees + 0.5) % 16;
        return (CompassPoint)sector;
    }

    /// <summary>Smallest angle between two directions, in [0, 180]: 350° and 10° are 20° apart.</summary>
    public static double AngleBetween(double degreesA, double degreesB)
    {
        var difference = Math.Abs(degreesA - degreesB) % 360;
        return difference > 180 ? 360 - difference : difference;
    }
}
