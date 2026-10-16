namespace VientoHud.Core.Rain;

/// <summary>
/// How the rain moves: one velocity for the whole area, from comparing two radar frames.
/// </summary>
/// <remarks>
/// Direction convention: degrees TOWARDS where the rain goes (like route bearings), not the meteorological
/// "from" of the wind.
/// </remarks>
/// <param name="EastKmh">Speed towards the east in km/h (negative: towards the west).</param>
/// <param name="NorthKmh">Speed towards the north in km/h (negative: towards the south).</param>
/// <param name="Correlation">How alike the frames are once the earlier one is moved, in [-1, 1].</param>
/// <param name="StillCorrelation">How alike they are without moving it: the gain over this is the evidence.</param>
public sealed record RainMotion(
    DateTimeOffset From,
    DateTimeOffset To,
    double EastKmh,
    double NorthKmh,
    double Correlation,
    double StillCorrelation)
{
    public double SpeedKmh => Math.Sqrt(EastKmh * EastKmh + NorthKmh * NorthKmh);

    /// <summary>Degrees TOWARDS where the rain goes, clockwise from north, in [0, 360). 0 when it does not move.</summary>
    public double TowardsDegrees => (Math.Atan2(EastKmh, NorthKmh) * 180 / Math.PI + 360) % 360;
}

/// <summary>Why no motion could be estimated.</summary>
public enum RainMotionFailure
{
    /// <summary>Too little rain in one of the frames to follow it.</summary>
    TooLittleRain,

    /// <summary>No displacement makes the frames clearly alike.</summary>
    NoClearMatch,

    /// <summary>The best match is at the edge of the search: faster than allowed, or not real.</summary>
    TooFast,
}

/// <summary>A motion, or why there is none.</summary>
public sealed record RainMotionEstimate(RainMotion? Motion, RainMotionFailure? Failure);

/// <summary>Settings for <see cref="RainMotions"/>.</summary>
public sealed record RainMotionOptions
{
    /// <summary>Fastest motion looked for. Rain in Spain rarely moves faster than ~100 km/h.</summary>
    public double MaxSpeedKmh { get; init; } = 150;

    /// <summary>Least area with rain, in each frame, to try.</summary>
    public double MinWetAreaKm2 { get; init; } = 200;

    /// <summary>
    /// Least correlation of the best match to trust it. Real pairs 15-45 min apart gave 0.43-0.89 (DECISIONS 028);
    /// unrelated frames with a few cells of rain reach ~0.38 at some shift by chance.
    /// </summary>
    public double MinCorrelation { get; init; } = 0.4;

    /// <summary>Least rain rate that counts as rain (default: 12 dBZ, as in <see cref="RainNearby"/>).</summary>
    public double ThresholdMmPerHour { get; init; } = RainNearby.DefaultThresholdMmPerHour;
}

/// <summary>
/// Estimates one displacement for the whole area between two frames of the same grid: the shift of the earlier frame
/// that makes it most alike the later one (Pearson correlation over the cells with data in both).
/// </summary>
/// <remarks>
/// <para>
/// What is compared is the shape of the rain, not its amount: each cell becomes its reflectivity above 12 dBZ, up to
/// 18 dB more (0 when dry), so a heavy core does not outweigh a wide area of light rain, and dry cells count (dry
/// where dry).
/// </para>
/// <para>
/// The search is coarse to fine: first on blocks of ~4 km over every shift up to the maximum speed, then cell by cell
/// around the best block, and last a parabola through the peak and its neighbours for a fraction of a cell.
/// </para>
/// </remarks>
public static class RainMotions
{
    private const double CoarseCellMeters = 4000;
    // Cells weigh at most as much as moderate rain (30 dBZ, ≈ 2.7 mm/h): a strong core, or a strong echo that does not
    // move (mountains, wind farms), must not outweigh a wide area of rain.
    private const double MaxDbzAboveThreshold = 18;

    // At least this share of the grid must have data in both frames at a shift for its correlation to count.
    private const double MinOverlapFraction = 0.25;

    /// <exception cref="ArgumentException">The frames are not on the same grid or not in time order.</exception>
    public static RainMotionEstimate Estimate(RainGrid earlier, RainGrid later, RainMotionOptions? options = null)
    {
        options ??= new RainMotionOptions();
        CheckSameGrid(earlier, later);
        var interval = later.Time - earlier.Time;
        if (interval <= TimeSpan.Zero || interval > TimeSpan.FromHours(2))
        {
            throw new ArgumentException("The later frame must come after the earlier one, within two hours.", nameof(later));
        }

        int rows = earlier.Rows, columns = earlier.Columns;
        var cellKm = earlier.CellSizeMeters / 1000;
        var a = Shape(earlier, options.ThresholdMmPerHour, out var wetA);
        var b = Shape(later, options.ThresholdMmPerHour, out var wetB);
        var minWetCells = options.MinWetAreaKm2 / (cellKm * cellKm);
        if (wetA < minWetCells || wetB < minWetCells)
        {
            return new RainMotionEstimate(null, RainMotionFailure.TooLittleRain);
        }

        var maxShift = (int)Math.Ceiling(options.MaxSpeedKmh * interval.TotalHours / cellKm);

        // Coarse search on blocks.
        var block = Math.Max(1, (int)Math.Round(CoarseCellMeters / earlier.CellSizeMeters));
        var (coarseA, coarseRows, coarseColumns) = Blocks(a, rows, columns, block);
        var (coarseB, _, _) = Blocks(b, rows, columns, block);
        var coarseMax = (maxShift + block - 1) / block;
        var (bestX, bestY, _) = BestShift(coarseA, coarseB, coarseRows, coarseColumns, -coarseMax, coarseMax, -coarseMax, coarseMax);

        // Fine search, cell by cell, around the best block.
        var (fineX, fineY, best) = BestShift(
            a, b, rows, columns,
            bestX * block - block, bestX * block + block,
            bestY * block - block, bestY * block + block);
        var still = Correlation(a, b, rows, columns, 0, 0) ?? double.NaN;

        if (double.IsNaN(best) || best < options.MinCorrelation)
        {
            return new RainMotionEstimate(null, RainMotionFailure.NoClearMatch);
        }
        if (Math.Abs(fineX) >= maxShift || Math.Abs(fineY) >= maxShift)
        {
            return new RainMotionEstimate(null, RainMotionFailure.TooFast);
        }

        // A fraction of a cell from a parabola through the peak and its two neighbours, on each axis.
        var dx = fineX + Vertex(
            Correlation(a, b, rows, columns, fineX - 1, fineY), best, Correlation(a, b, rows, columns, fineX + 1, fineY));
        var dy = fineY + Vertex(
            Correlation(a, b, rows, columns, fineX, fineY - 1), best, Correlation(a, b, rows, columns, fineX, fineY + 1));

        var hours = interval.TotalHours;
        // Rows grow towards the south: a shift down is a motion to the south.
        return new RainMotionEstimate(
            new RainMotion(earlier.Time, later.Time, dx * cellKm / hours, -dy * cellKm / hours, best, still),
            null);
    }

    private static void CheckSameGrid(RainGrid earlier, RainGrid later)
    {
        var p = earlier.Projection;
        var q = later.Projection;
        var same = earlier.Rows == later.Rows && earlier.Columns == later.Columns
            && earlier.CellSizeMeters == later.CellSizeMeters
            && Math.Abs(earlier.Left - later.Left) < 1e-3 && Math.Abs(earlier.Top - later.Top) < 1e-3
            && p.CenterLatitude == q.CenterLatitude && p.CenterLongitude == q.CenterLongitude
            && p.FalseEasting == q.FalseEasting && p.FalseNorthing == q.FalseNorthing;
        if (!same)
        {
            throw new ArgumentException("The frames must cover the same grid (same centre, size and cells).", nameof(later));
        }
    }

    // Reflectivity above the threshold, 0 when dry, NaN without data.
    private static double[] Shape(RainGrid grid, double threshold, out int wet)
    {
        var values = new double[grid.Rows * grid.Columns];
        var thresholdDbz = Reflectivity.FromRainRate(threshold);
        wet = 0;
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var column = 0; column < grid.Columns; column++)
            {
                var rate = grid[row, column];
                double value;
                if (float.IsNaN(rate))
                {
                    value = double.NaN;
                }
                else if (rate < threshold)
                {
                    value = 0;
                }
                else
                {
                    value = Math.Min(Reflectivity.FromRainRate(rate) - thresholdDbz, MaxDbzAboveThreshold);
                    wet++;
                }
                values[row * grid.Columns + column] = value;
            }
        }
        return values;
    }

    // Mean of each block × block square with at least half its cells with data; NaN otherwise.
    private static (double[] Values, int Rows, int Columns) Blocks(double[] values, int rows, int columns, int block)
    {
        if (block == 1)
        {
            return (values, rows, columns);
        }
        int blockRows = rows / block, blockColumns = columns / block;
        var result = new double[blockRows * blockColumns];
        for (var br = 0; br < blockRows; br++)
        {
            for (var bc = 0; bc < blockColumns; bc++)
            {
                double sum = 0;
                var count = 0;
                for (var r = br * block; r < (br + 1) * block; r++)
                {
                    for (var c = bc * block; c < (bc + 1) * block; c++)
                    {
                        var v = values[r * columns + c];
                        if (!double.IsNaN(v))
                        {
                            sum += v;
                            count++;
                        }
                    }
                }
                result[br * blockColumns + bc] = count * 2 >= block * block ? sum / count : double.NaN;
            }
        }
        return (result, blockRows, blockColumns);
    }

    private static (int X, int Y, double Correlation) BestShift(
        double[] a, double[] b, int rows, int columns, int minX, int maxX, int minY, int maxY)
    {
        (int X, int Y, double Correlation) best = (0, 0, double.NaN);
        for (var dy = minY; dy <= maxY; dy++)
        {
            for (var dx = minX; dx <= maxX; dx++)
            {
                if (Correlation(a, b, rows, columns, dx, dy) is { } correlation
                    && (double.IsNaN(best.Correlation) || correlation > best.Correlation))
                {
                    best = (dx, dy, correlation);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Pearson correlation between the earlier frame moved <paramref name="dx"/> cells east and <paramref name="dy"/>
    /// cells south and the later one, over the cells with data in both. Null when they overlap too little or one of
    /// them is flat there.
    /// </summary>
    private static double? Correlation(double[] a, double[] b, int rows, int columns, int dx, int dy)
    {
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
        var n = 0;
        int rowStart = Math.Max(0, dy), rowEnd = Math.Min(rows, rows + dy);
        int columnStart = Math.Max(0, dx), columnEnd = Math.Min(columns, columns + dx);
        for (var r = rowStart; r < rowEnd; r++)
        {
            var later = r * columns;
            var earlier = (r - dy) * columns - dx;
            for (var c = columnStart; c < columnEnd; c++)
            {
                var x = a[earlier + c];
                var y = b[later + c];
                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    continue;
                }
                sumA += x;
                sumB += y;
                sumAA += x * x;
                sumBB += y * y;
                sumAB += x * y;
                n++;
            }
        }

        if (n < MinOverlapFraction * rows * columns)
        {
            return null;
        }
        var covariance = sumAB - sumA * sumB / n;
        var varianceA = sumAA - sumA * sumA / n;
        var varianceB = sumBB - sumB * sumB / n;
        return varianceA <= 1e-12 || varianceB <= 1e-12 ? null : covariance / Math.Sqrt(varianceA * varianceB);
    }

    // Offset in [-0.5, 0.5] of the top of the parabola through (-1, left), (0, center), (1, right).
    private static double Vertex(double? left, double center, double? right)
    {
        if (left is not { } l || right is not { } r)
        {
            return 0;
        }
        var curvature = l - 2 * center + r;
        return curvature >= 0 ? 0 : Math.Clamp((l - r) / (2 * curvature), -0.5, 0.5);
    }
}
