namespace VientoHud.Core.Rain;

/// <summary>
/// A square of rain rates around a point, cut from a radar composite and kept on the composite's own projected grid
/// (regular cells of a fixed size in metres), so that consecutive frames line up cell by cell. Immutable.
/// </summary>
/// <remarks>
/// Row 0 is the northern edge and column 0 the western edge. Each cell holds a rain rate in mm/h: 0 where the radar
/// looked and saw nothing (dry), <see cref="float.NaN"/> where there is no data (out of range, radar down, or outside
/// the composite).
/// </remarks>
public sealed class RainGrid
{
    private readonly float[] _rates;

    /// <param name="time">Nominal time of the frame, in UTC (OPERA: end of the 10-minute scan window).</param>
    /// <param name="left">Projected x of the western edge of column 0, in metres.</param>
    /// <param name="top">Projected y of the northern edge of row 0, in metres.</param>
    /// <param name="rates">Rain rates in mm/h, row by row from the north: <c>rows × columns</c> values.</param>
    public RainGrid(
        DateTimeOffset time,
        LambertAzimuthalEqualArea projection,
        double left,
        double top,
        double cellSizeMeters,
        int rows,
        int columns,
        float[] rates)
    {
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            throw new ArgumentException("Grid corner must be finite.");
        }
        if (!double.IsFinite(cellSizeMeters) || cellSizeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizeMeters), cellSizeMeters, "Cell size must be positive.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        if (rates.Length != rows * columns)
        {
            throw new ArgumentException($"Expected {rows * columns} rates, got {rates.Length}.", nameof(rates));
        }
        if (rates.Any(r => r < 0 || float.IsInfinity(r)))
        {
            throw new ArgumentException("Rates must be finite and not negative (NaN for no data).", nameof(rates));
        }

        Time = time;
        Projection = projection;
        Left = left;
        Top = top;
        CellSizeMeters = cellSizeMeters;
        Rows = rows;
        Columns = columns;
        _rates = (float[])rates.Clone();
    }

    public DateTimeOffset Time { get; }
    public LambertAzimuthalEqualArea Projection { get; }
    public double Left { get; }
    public double Top { get; }
    public double CellSizeMeters { get; }
    public int Rows { get; }
    public int Columns { get; }

    /// <summary>Rain rate of a cell in mm/h; NaN for no data.</summary>
    public float this[int row, int column]
    {
        get
        {
            if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
            {
                throw new ArgumentOutOfRangeException(row < 0 || row >= Rows ? nameof(row) : nameof(column));
            }
            return _rates[row * Columns + column];
        }
    }

    /// <summary>Cell containing a position, or null when it falls outside the grid.</summary>
    public (int Row, int Column)? CellOf(Coordinate position)
    {
        var (x, y) = Projection.Forward(position);
        var column = (int)Math.Floor((x - Left) / CellSizeMeters);
        var row = (int)Math.Floor((Top - y) / CellSizeMeters);
        return row >= 0 && row < Rows && column >= 0 && column < Columns ? (row, column) : null;
    }

    /// <summary>Position of the centre of a cell.</summary>
    public Coordinate CenterOf(int row, int column)
    {
        if ((uint)row >= (uint)Rows || (uint)column >= (uint)Columns)
        {
            throw new ArgumentOutOfRangeException(row < 0 || row >= Rows ? nameof(row) : nameof(column));
        }
        return Projection.Inverse(Left + (column + 0.5) * CellSizeMeters, Top - (row + 0.5) * CellSizeMeters);
    }

    /// <summary>Rain rate in mm/h at a position: null outside the grid, NaN for no data.</summary>
    public float? RateAt(Coordinate position) => CellOf(position) is var (row, column) ? this[row, column] : null;
}
