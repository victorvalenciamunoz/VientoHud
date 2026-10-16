using System.Globalization;
using PureHDF;
using PureHDF.Selections;

namespace VientoHud.Core.Rain.Opera;

/// <summary>
/// Reads a radar composite in ODIM HDF5 (the OPERA/EUMETNET information model, versions 2.x) and cuts a square of
/// rain rates around a point. Supports reflectivity (<c>DBZH</c>, converted with <see cref="Reflectivity"/>) and
/// rain rate (<c>RATE</c>, mm/h).
/// </summary>
/// <remarks>
/// Only the cut is read from the file (an HDF5 hyperslab): the whole 1 km European composite has 16.7 million cells
/// and takes ~40 s to read, a 300 km square ~2 s. Raw values equal to <c>undetect</c> (radar saw nothing) become 0 and
/// those equal to <c>nodata</c> become NaN; the rest are <c>gain·raw + offset</c>.
/// </remarks>
public static class OdimComposite
{
    /// <exception cref="RadarException">The file is not a readable ODIM composite.</exception>
    public static RainGrid ReadAround(Stream hdf5, Coordinate center, double radiusKm)
    {
        if (!double.IsFinite(radiusKm) || radiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), radiusKm, "Radius must be positive.");
        }

        try
        {
            using var file = H5File.Open(hdf5, leaveOpen: true);
            return Read(file, center, radiusKm);
        }
        catch (Exception ex) when (ex is not RadarException and not ArgumentException)
        {
            throw new RadarException($"Not a readable ODIM composite: {ex.Message}", ex);
        }
    }

    private static RainGrid Read(PureHDF.VOL.Native.NativeFile file, Coordinate center, double radiusKm)
    {
        if (Text(file.Group("what"), "object") != "COMP")
        {
            throw new RadarException("The file is not a composite (what/object is not COMP).");
        }

        var time = ParseTime(Text(file.Group("what"), "date"), Text(file.Group("what"), "time"));
        var where = file.Group("where");
        var projection = LambertAzimuthalEqualArea.FromProj(Text(where, "projdef"));
        var xScale = Number(where, "xscale");
        var yScale = Number(where, "yscale");
        if (Math.Abs(xScale - yScale) > 1e-6 * xScale || xScale <= 0)
        {
            throw new RadarException($"Cells are not square ({xScale} × {yScale} m).");
        }
        var (left, top) = projection.Forward(new Coordinate(Number(where, "UL_lat"), Number(where, "UL_lon")));

        var dataWhat = file.Group("dataset1/data1/what");
        var quantity = Text(dataWhat, "quantity");
        Func<double, double> toRate = quantity switch
        {
            "DBZH" => Reflectivity.ToRainRate,
            "RATE" => rate => Math.Max(0, rate),
            _ => throw new RadarException($"Unsupported quantity {quantity}: expected DBZH or RATE."),
        };
        var gain = Number(dataWhat, "gain");
        var offset = Number(dataWhat, "offset");
        var nodata = Number(dataWhat, "nodata");
        var undetect = Number(dataWhat, "undetect");

        var dataset = file.Dataset("dataset1/data1/data");
        var dimensions = dataset.Space.Dimensions;
        if (dimensions.Length != 2)
        {
            throw new RadarException($"Data has {dimensions.Length} dimensions, expected 2.");
        }
        long fileRows = (long)dimensions[0], fileColumns = (long)dimensions[1];

        // A square of 2n + 1 cells with the point's cell in the middle.
        var (x, y) = projection.Forward(center);
        var centerColumn = (long)Math.Floor((x - left) / xScale);
        var centerRow = (long)Math.Floor((top - y) / xScale);
        var half = (int)Math.Ceiling(radiusKm * 1000 / xScale - 0.5);
        var size = 2 * half + 1;
        long firstRow = centerRow - half, firstColumn = centerColumn - half;

        var rates = new float[size * size];
        Array.Fill(rates, float.NaN);

        // Part of the square inside the file grid.
        long rowStart = Math.Max(firstRow, 0), rowEnd = Math.Min(firstRow + size, fileRows);
        long columnStart = Math.Max(firstColumn, 0), columnEnd = Math.Min(firstColumn + size, fileColumns);
        if (rowStart < rowEnd && columnStart < columnEnd)
        {
            var raw = ReadBlock(dataset, rowStart, columnStart, rowEnd - rowStart, columnEnd - columnStart);
            for (var r = 0; r < raw.GetLength(0); r++)
            {
                for (var c = 0; c < raw.GetLength(1); c++)
                {
                    var value = raw[r, c];
                    var index = (int)((rowStart - firstRow + r) * size + (columnStart - firstColumn + c));
                    rates[index] = value == nodata || double.IsNaN(value) ? float.NaN
                        : value == undetect ? 0f
                        : (float)toRate(gain * value + offset);
                }
            }
        }

        return new RainGrid(
            time, projection, left + firstColumn * xScale, top - firstRow * xScale, xScale, size, size, rates);
    }

    private static double[,] ReadBlock(IH5Dataset dataset, long row, long column, long rows, long columns)
    {
        var selection = new HyperslabSelection(
            rank: 2, starts: [(ulong)row, (ulong)column], blocks: [(ulong)rows, (ulong)columns]);
        ulong[] memory = [(ulong)rows, (ulong)columns];
        var type = dataset.Type;
        return (type.Class, type.Size) switch
        {
            (H5DataTypeClass.FloatingPoint, 8) => dataset.Read<double[,]>(selection, memoryDims: memory),
            (H5DataTypeClass.FloatingPoint, 4) => Widen(dataset.Read<float[,]>(selection, memoryDims: memory), v => v),
            (H5DataTypeClass.FixedPoint, 1) => Widen(dataset.Read<byte[,]>(selection, memoryDims: memory), v => v),
            (H5DataTypeClass.FixedPoint, 2) => Widen(dataset.Read<ushort[,]>(selection, memoryDims: memory), v => v),
            _ => throw new RadarException($"Unsupported data type {type.Class} of {type.Size} bytes."),
        };
    }

    private static double[,] Widen<T>(T[,] values, Func<T, double> widen)
    {
        var result = new double[values.GetLength(0), values.GetLength(1)];
        for (var r = 0; r < values.GetLength(0); r++)
        {
            for (var c = 0; c < values.GetLength(1); c++)
            {
                result[r, c] = widen(values[r, c]);
            }
        }
        return result;
    }

    private static DateTimeOffset ParseTime(string date, string time) =>
        DateTimeOffset.TryParseExact(
            date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new RadarException($"Bad date or time: {date} {time}.");

    private static string Text(IH5Group group, string name) =>
        Attribute(group, name).Read<string>().Trim('\0', ' ');

    // ODIM stores some numbers as integers (xsize) and others as doubles (xscale); reading one as the other gives
    // garbage, so the attribute's own type decides.
    private static double Number(IH5Group group, string name)
    {
        var attribute = Attribute(group, name);
        return (attribute.Type.Class, attribute.Type.Size) switch
        {
            (H5DataTypeClass.FloatingPoint, 8) => attribute.Read<double>(),
            (H5DataTypeClass.FloatingPoint, 4) => attribute.Read<float>(),
            (H5DataTypeClass.FixedPoint, 8) => attribute.Read<long>(),
            (H5DataTypeClass.FixedPoint, 4) => attribute.Read<int>(),
            (H5DataTypeClass.FixedPoint, 2) => attribute.Read<short>(),
            (H5DataTypeClass.FixedPoint, 1) => attribute.Read<sbyte>(),
            (H5DataTypeClass.String or H5DataTypeClass.VariableLength, _) =>
                double.TryParse(attribute.Read<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? v
                    : throw new RadarException($"Attribute {name} is not a number."),
            var (cls, sz) => throw new RadarException($"Attribute {name} has an unsupported type {cls} of {sz} bytes."),
        };
    }

    private static IH5Attribute Attribute(IH5Group group, string name) =>
        group.AttributeExists(name) ? group.Attribute(name) : throw new RadarException($"Missing attribute {name}.");
}
