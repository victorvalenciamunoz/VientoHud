namespace VientoHud.Core.Towns;

/// <summary>
/// Share of built-up ground in each cell of a latitude/longitude grid, north up: row 0 is the northern edge and column 0
/// the western one. Cells span <see cref="CellDegrees"/> in latitude and in longitude (so they are narrower in km than
/// tall away from the equator). Does not cross the 180° meridian.
/// </summary>
public sealed class BuiltUpGrid
{
    private readonly float[] _shares;

    /// <param name="north">Latitude of the northern edge of row 0.</param>
    /// <param name="west">Longitude of the western edge of column 0.</param>
    /// <param name="cellDegrees">Size of a cell in degrees of latitude and of longitude.</param>
    /// <param name="width">Columns.</param>
    /// <param name="height">Rows.</param>
    /// <param name="shares">Built-up share of each cell in [0, 1], row by row from the north-west corner.</param>
    public BuiltUpGrid(double north, double west, double cellDegrees, int width, int height, float[] shares)
    {
        if (!double.IsFinite(cellDegrees) || cellDegrees <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellDegrees), cellDegrees, "Cell size must be positive.");
        }
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("The grid needs at least one cell.");
        }
        ArgumentNullException.ThrowIfNull(shares);
        if (shares.Length != width * height)
        {
            throw new ArgumentException($"Expected {width * height} shares, got {shares.Length}.", nameof(shares));
        }
        North = north;
        West = west;
        CellDegrees = cellDegrees;
        Width = width;
        Height = height;
        _shares = shares;
    }

    public double North { get; }
    public double West { get; }
    public double CellDegrees { get; }
    public int Width { get; }
    public int Height { get; }

    public double South => North - Height * CellDegrees;
    public double East => West + Width * CellDegrees;

    /// <summary>Built-up share of a cell, in [0, 1].</summary>
    public float this[int column, int row] => _shares[row * Width + column];

    /// <summary>The cell a position falls in, or null outside the grid.</summary>
    public (int Column, int Row)? CellOf(Coordinate position)
    {
        var column = (int)Math.Floor((position.Longitude - West) / CellDegrees);
        var row = (int)Math.Floor((North - position.Latitude) / CellDegrees);
        return column >= 0 && column < Width && row >= 0 && row < Height ? (column, row) : null;
    }

    /// <summary>Built-up share where a position falls; 0 outside the grid.</summary>
    public float ShareAt(Coordinate position) => CellOf(position) is var (column, row) ? this[column, row] : 0;

    /// <summary>The corner of the cell grid at a column and row of corners (0 to Width, 0 to Height).</summary>
    public Coordinate Corner(int column, int row) => new(North - row * CellDegrees, West + column * CellDegrees);

    /// <summary>Area of one cell of a row in km² (spherical Earth).</summary>
    public double CellAreaKm2(int row)
    {
        const double kmPerDegree = 6371.0088 * Math.PI / 180;
        var latitude = North - (row + 0.5) * CellDegrees;
        return Math.Pow(CellDegrees * kmPerDegree, 2) * Math.Cos(latitude * Math.PI / 180);
    }
}
