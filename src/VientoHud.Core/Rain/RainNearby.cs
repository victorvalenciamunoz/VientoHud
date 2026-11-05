namespace VientoHud.Core.Rain;

/// <summary>A cell with rain, seen from a point.</summary>
/// <param name="DistanceKm">From the point to the centre of the cell; 0 for the point's own cell.</param>
/// <param name="BearingDegrees">Degrees TOWARDS the cell from the point, clockwise from north, in [0, 360).</param>
/// <param name="RateMmPerHour">Rain rate in the cell.</param>
public sealed record RainCell(Coordinate Position, double DistanceKm, double BearingDegrees, float RateMmPerHour);

/// <summary>The rain around a point in one radar frame.</summary>
/// <param name="Time">Time of the frame, in UTC.</param>
/// <param name="RateAtPoint">Rain rate in the point's cell in mm/h; NaN when the radar has no data there.</param>
/// <param name="CoveredFraction">Share of the cells with radar data, in [0, 1].</param>
/// <param name="WetFraction">Share of the cells with data that have rain (at least the threshold), in [0, 1].</param>
/// <param name="Nearest">The closest cell with rain (the point's own when it rains there); null when none.</param>
/// <param name="Strongest">The cell with the most rain; null when none.</param>
/// <param name="ThresholdMmPerHour">Least rain rate that counts as rain.</param>
public sealed record RainNearby(
    DateTimeOffset Time,
    Coordinate Point,
    float RateAtPoint,
    double CoveredFraction,
    double WetFraction,
    RainCell? Nearest,
    RainCell? Strongest,
    double ThresholdMmPerHour)
{
    /// <summary>
    /// Default least rain rate that counts as rain: 12 dBZ with Marshall-Palmer (≈ 0.2 mm/h), the weakest echo AEMET
    /// draws on its radar maps, so that the text agrees with what the maps show.
    /// </summary>
    public static readonly double DefaultThresholdMmPerHour = Reflectivity.ToRainRate(12);

    /// <summary>True when the point's cell has rain; false when it is dry or has no data.</summary>
    public bool RainsAtPoint => RateAtPoint >= ThresholdMmPerHour;

    /// <summary>Looks at every cell of the frame from <paramref name="point"/>, which must be inside the grid.</summary>
    public static RainNearby Describe(RainGrid grid, Coordinate point, double? thresholdMmPerHour = null)
    {
        var threshold = thresholdMmPerHour ?? DefaultThresholdMmPerHour;
        if (!double.IsFinite(threshold) || threshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdMmPerHour), threshold, "Threshold must be positive.");
        }
        var (pointRow, pointColumn) = grid.CellOf(point)
            ?? throw new ArgumentException("The point is outside the grid.", nameof(point));

        int covered = 0, wet = 0;
        RainCell? nearest = null, strongest = null;
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var column = 0; column < grid.Columns; column++)
            {
                var rate = grid[row, column];
                if (float.IsNaN(rate))
                {
                    continue;
                }
                covered++;
                if (rate < threshold)
                {
                    continue;
                }
                wet++;

                var own = row == pointRow && column == pointColumn;
                var center = grid.CenterOf(row, column);
                var distance = own ? 0 : point.DistanceKmTo(center);
                if (nearest is null || distance < nearest.DistanceKm)
                {
                    nearest = new RainCell(center, distance, own ? 0 : point.BearingTo(center), rate);
                }
                if (strongest is null || rate > strongest.RateMmPerHour
                    || (rate == strongest.RateMmPerHour && distance < strongest.DistanceKm))
                {
                    strongest = new RainCell(center, distance, own ? 0 : point.BearingTo(center), rate);
                }
            }
        }

        var cells = grid.Rows * grid.Columns;
        return new RainNearby(
            grid.Time,
            point,
            grid[pointRow, pointColumn],
            (double)covered / cells,
            covered == 0 ? 0 : (double)wet / covered,
            nearest,
            strongest,
            threshold);
    }
}
