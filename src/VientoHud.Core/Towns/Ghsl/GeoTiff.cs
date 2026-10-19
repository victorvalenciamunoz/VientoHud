using System.Buffers.Binary;

namespace VientoHud.Core.Towns.Ghsl;

/// <summary>
/// Reads windows of a single-band 16-bit GeoTIFF on a latitude/longitude grid, as GHSL publishes them: little-endian
/// BigTIFF, square tiles (blocks), uncompressed or LZW without predictor, georeferenced by ModelPixelScale and
/// ModelTiepoint. Only the blocks a window touches are read and decompressed.
/// </summary>
internal sealed class GeoTiff
{
    public const ushort NoData = 65535;

    private const int ImageWidthTag = 256;
    private const int ImageLengthTag = 257;
    private const int BitsPerSampleTag = 258;
    private const int CompressionTag = 259;
    private const int SamplesPerPixelTag = 277;
    private const int PredictorTag = 317;
    private const int TileWidthTag = 322;
    private const int TileLengthTag = 323;
    private const int TileOffsetsTag = 324;
    private const int TileByteCountsTag = 325;
    private const int SampleFormatTag = 339;
    private const int ModelPixelScaleTag = 33550;
    private const int ModelTiepointTag = 33922;

    private const int Uncompressed = 1;
    private const int Lzw = 5;

    private readonly Stream _stream;
    private readonly long[] _offsets;
    private readonly long[] _byteCounts;
    private readonly int _compression;

    private GeoTiff(Stream stream, IReadOnlyDictionary<int, Entry> entries)
    {
        _stream = stream;
        Width = (int)Single(entries, ImageWidthTag);
        Height = (int)Single(entries, ImageLengthTag);
        BlockWidth = (int)Single(entries, TileWidthTag);
        BlockHeight = (int)Single(entries, TileLengthTag);
        _compression = (int)Single(entries, CompressionTag);
        if (Single(entries, BitsPerSampleTag) != 16
            || (entries.ContainsKey(SamplesPerPixelTag) && Single(entries, SamplesPerPixelTag) != 1)
            || (entries.ContainsKey(SampleFormatTag) && Single(entries, SampleFormatTag) != 1))
        {
            throw new BuiltUpException("GeoTIFF is not a single band of unsigned 16-bit values.");
        }
        if (_compression is not (Uncompressed or Lzw))
        {
            throw new BuiltUpException($"GeoTIFF compression {_compression} is not supported (only none and LZW).");
        }
        if (entries.ContainsKey(PredictorTag) && Single(entries, PredictorTag) != 1)
        {
            throw new BuiltUpException("GeoTIFF with a predictor is not supported.");
        }
        _offsets = Integers(entries[TileOffsetsTag]);
        _byteCounts = Integers(entries[TileByteCountsTag]);
        if (_offsets.Length != BlocksAcross * BlocksDown || _byteCounts.Length != _offsets.Length)
        {
            throw new BuiltUpException("GeoTIFF block tables do not match its size.");
        }

        var scale = Doubles(entries[ModelPixelScaleTag]);
        var tiepoint = Doubles(entries[ModelTiepointTag]);
        if (scale.Length < 2 || tiepoint.Length < 6 || tiepoint[0] != 0 || tiepoint[1] != 0)
        {
            throw new BuiltUpException("GeoTIFF georeference is not a pixel scale and a tie point at the corner.");
        }
        CellLonDegrees = scale[0];
        CellLatDegrees = scale[1];
        West = tiepoint[3];
        North = tiepoint[4];
    }

    public int Width { get; }
    public int Height { get; }
    public int BlockWidth { get; }
    public int BlockHeight { get; }
    public double West { get; }
    public double North { get; }
    public double CellLonDegrees { get; }
    public double CellLatDegrees { get; }

    private int BlocksAcross => (Width + BlockWidth - 1) / BlockWidth;
    private int BlocksDown => (Height + BlockHeight - 1) / BlockHeight;

    /// <exception cref="BuiltUpException">Not a GeoTIFF this reader understands.</exception>
    public static GeoTiff Open(Stream stream)
    {
        try
        {
            var header = ReadAt(stream, 0, 16);
            if (header[0] != 'I' || header[1] != 'I' || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)) != 43)
            {
                throw new BuiltUpException("Not a little-endian BigTIFF file.");
            }
            var ifd = (long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
            var count = (int)BinaryPrimitives.ReadUInt64LittleEndian(ReadAt(stream, ifd, 8));
            var table = ReadAt(stream, ifd + 8, count * 20);
            var entries = new Dictionary<int, Entry>();
            for (var i = 0; i < count; i++)
            {
                var e = table.AsSpan(i * 20, 20);
                var type = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
                var length = (long)BinaryPrimitives.ReadUInt64LittleEndian(e[4..]);
                var size = TypeSize(type);
                // Values of up to 8 bytes sit in the entry itself; longer ones elsewhere in the file.
                var data = size * length <= 8
                    ? e.Slice(12, (int)(size * length)).ToArray()
                    : ReadAt(stream, (long)BinaryPrimitives.ReadUInt64LittleEndian(e[12..]), checked((int)(size * length)));
                entries[BinaryPrimitives.ReadUInt16LittleEndian(e)] = new Entry(type, length, data);
            }
            foreach (var tag in new[] { ImageWidthTag, ImageLengthTag, BitsPerSampleTag, CompressionTag, TileWidthTag,
                         TileLengthTag, TileOffsetsTag, TileByteCountsTag, ModelPixelScaleTag, ModelTiepointTag })
            {
                if (!entries.ContainsKey(tag))
                {
                    throw new BuiltUpException($"GeoTIFF lacks tag {tag} (only tiled GeoTIFFs are supported).");
                }
            }
            return new GeoTiff(stream, entries);
        }
        catch (Exception ex) when (ex is EndOfStreamException or OverflowException or ArgumentException)
        {
            throw new BuiltUpException("GeoTIFF file is truncated or malformed.", ex);
        }
    }

    /// <summary>
    /// Values of the pixels from (<paramref name="column"/>, <paramref name="row"/>) over the given size, row by row;
    /// <see cref="NoData"/> where the window falls outside the image.
    /// </summary>
    public ushort[] ReadWindow(int column, int row, int width, int height)
    {
        var values = new ushort[width * height];
        Array.Fill(values, NoData);
        var firstColumn = Math.Max(column, 0);
        var lastColumn = Math.Min(column + width, Width) - 1;
        var firstRow = Math.Max(row, 0);
        var lastRow = Math.Min(row + height, Height) - 1;
        if (firstColumn > lastColumn || firstRow > lastRow)
        {
            return values;
        }

        for (var blockRow = firstRow / BlockHeight; blockRow <= lastRow / BlockHeight; blockRow++)
        {
            for (var blockColumn = firstColumn / BlockWidth; blockColumn <= lastColumn / BlockWidth; blockColumn++)
            {
                var block = ReadBlock(blockRow * BlocksAcross + blockColumn);
                var top = blockRow * BlockHeight;
                var left = blockColumn * BlockWidth;
                for (var y = Math.Max(firstRow, top); y <= Math.Min(lastRow, top + BlockHeight - 1); y++)
                {
                    for (var x = Math.Max(firstColumn, left); x <= Math.Min(lastColumn, left + BlockWidth - 1); x++)
                    {
                        var at = ((y - top) * BlockWidth + (x - left)) * 2;
                        values[(y - row) * width + (x - column)] = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at));
                    }
                }
            }
        }
        return values;
    }

    private byte[] ReadBlock(int index)
    {
        var size = BlockWidth * BlockHeight * 2;
        var stored = ReadAt(_stream, _offsets[index], checked((int)_byteCounts[index]));
        var block = _compression == Lzw ? LzwDecoder.Decode(stored, size) : stored;
        if (block.Length < size)
        {
            throw new BuiltUpException($"GeoTIFF block {index} is short ({block.Length} of {size} bytes).");
        }
        return block;
    }

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        var buffer = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 or 16 or 17 or 18 => 8,
        _ => throw new BuiltUpException($"GeoTIFF field type {type} is unknown."),
    };

    private static long Single(IReadOnlyDictionary<int, Entry> entries, int tag) =>
        Integers(entries[tag]) is [var value, ..] ? value : throw new BuiltUpException($"GeoTIFF tag {tag} is empty.");

    private static long[] Integers(Entry entry)
    {
        var values = new long[entry.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = entry.Type switch
            {
                3 => BinaryPrimitives.ReadUInt16LittleEndian(entry.Data.AsSpan(i * 2)),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.AsSpan(i * 4)),
                16 => checked((long)BinaryPrimitives.ReadUInt64LittleEndian(entry.Data.AsSpan(i * 8))),
                _ => throw new BuiltUpException($"GeoTIFF field of type {entry.Type} is not an integer."),
            };
        }
        return values;
    }

    private static double[] Doubles(Entry entry) => entry.Type == 12
        ? Enumerable.Range(0, (int)entry.Count).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(entry.Data.AsSpan(i * 8))).ToArray()
        : throw new BuiltUpException("GeoTIFF georeference is not in doubles.");

    private sealed record Entry(ushort Type, long Count, byte[] Data);
}

/// <summary>TIFF LZW: codes of 9 to 12 bits, most significant bit first; 256 clears, 257 ends; widths grow one code early.</summary>
internal static class LzwDecoder
{
    private const int Clear = 256;
    private const int End = 257;

    public static byte[] Decode(byte[] input, int expectedLength)
    {
        var output = new byte[expectedLength];
        var length = 0;
        // Each code is a previous code plus one byte: kept as (prefix, last byte, first byte, length).
        var prefix = new int[4096];
        var last = new byte[4096];
        var first = new byte[4096];
        var size = new int[4096];
        for (var i = 0; i < 256; i++)
        {
            last[i] = first[i] = (byte)i;
            size[i] = 1;
            prefix[i] = -1;
        }

        var next = 258;
        var width = 9;
        var previous = -1;
        long buffer = 0;
        var bits = 0;
        var position = 0;
        while (true)
        {
            while (bits < width && position < input.Length)
            {
                buffer = (buffer << 8) | input[position++];
                bits += 8;
            }
            if (bits < width)
            {
                break;
            }
            var code = (int)((buffer >> (bits - width)) & ((1 << width) - 1));
            bits -= width;
            if (code == End)
            {
                break;
            }
            if (code == Clear)
            {
                next = 258;
                width = 9;
                previous = -1;
                continue;
            }

            int written;
            if (code < next && (code < 256 || code > End))
            {
                written = Emit(code, output, length, prefix, last, size);
                if (previous >= 0)
                {
                    Add(previous, first[code]);
                }
            }
            else if (code == next && previous >= 0)
            {
                // The code being defined: previous string plus its own first byte.
                Add(previous, first[previous]);
                written = Emit(code, output, length, prefix, last, size);
            }
            else
            {
                throw new BuiltUpException("GeoTIFF block has a bad LZW code.");
            }
            length += written;
            previous = code;
            if (next + 1 >= (1 << width) && width < 12)
            {
                width++;
            }
        }
        return length == expectedLength ? output : output[..length];

        void Add(int previousCode, byte b)
        {
            if (next >= 4096)
            {
                return;
            }
            prefix[next] = previousCode;
            last[next] = b;
            first[next] = first[previousCode];
            size[next] = size[previousCode] + 1;
            next++;
        }
    }

    // Writes the string of a code at an offset (cut at the end of the output); returns how many bytes it wrote.
    private static int Emit(int code, byte[] output, int offset, int[] prefix, byte[] last, int[] size)
    {
        var length = size[code];
        var room = Math.Min(length, output.Length - offset);
        for (var i = length - 1; i >= 0; i--, code = prefix[code])
        {
            if (i < room)
            {
                output[offset + i] = last[code];
            }
        }
        return room;
    }
}
