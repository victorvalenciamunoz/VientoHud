using System.Buffers.Binary;
using System.IO.Compression;

namespace VientoHud.Core.Flyover;

/// <summary>
/// The elevations of one Terrarium tile (DECISIONS 056): a PNG whose pixels encode metres as
/// <c>R·256 + G + B/256 − 32768</c>. Read here without an image library, for the judge of phase 8 (the relief the
/// 3D view draws against the GPX's elevations): only what these tiles use is supported, 8-bit RGB or RGBA, not
/// interlaced.
/// </summary>
public sealed class TerrariumTile
{
    private readonly float[] _metres;

    private TerrariumTile(int size, float[] metres)
    {
        Size = size;
        _metres = metres;
    }

    /// <summary>Width and height in pixels.</summary>
    public int Size { get; }

    /// <summary>Elevation of the pixel at column <paramref name="x"/> and row <paramref name="y"/> from the north-west.</summary>
    public double At(int x, int y) => _metres[Math.Clamp(y, 0, Size - 1) * Size + Math.Clamp(x, 0, Size - 1)];

    /// <summary>
    /// Elevation at a point inside the tile given in pixels from its north-west corner (pixel centres at .5),
    /// interpolated between the four pixels around it.
    /// </summary>
    public double AtPixel(double px, double py)
    {
        var x = px - 0.5;
        var y = py - 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var tx = x - x0;
        var ty = y - y0;
        return (1 - ty) * ((1 - tx) * At(x0, y0) + tx * At(x0 + 1, y0)) + ty * ((1 - tx) * At(x0, y0 + 1) + tx * At(x0 + 1, y0 + 1));
    }

    /// <summary>Reads a Terrarium PNG.</summary>
    /// <exception cref="InvalidDataException">Not a PNG, or one these tiles never are.</exception>
    public static TerrariumTile Read(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 8 || !png[..8].SequenceEqual(signature))
        {
            throw new InvalidDataException("Not a PNG.");
        }
        int width = 0, height = 0, channels = 0;
        var compressed = new MemoryStream();
        var at = 8;
        while (at + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            var type = System.Text.Encoding.ASCII.GetString(png.Slice(at + 4, 4));
            if (at + 12 + length > png.Length)
            {
                throw new InvalidDataException("The PNG is cut short.");
            }
            var data = png.Slice(at + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    var bitDepth = data[8];
                    var colorType = data[9];
                    var interlace = data[12];
                    channels = colorType switch { 2 => 3, 6 => 4, _ => 0 };
                    if (bitDepth != 8 || channels == 0 || interlace != 0 || width != height || width is < 1 or > 4096)
                    {
                        throw new InvalidDataException($"Unsupported PNG: {width}×{height}, depth {bitDepth}, colour {colorType}, interlace {interlace}.");
                    }
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
            }
            at += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }
        if (channels == 0)
        {
            throw new InvalidDataException("The PNG has no header.");
        }

        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            var read = 0;
            int n;
            while (read < raw.Length && (n = zlib.Read(raw, read, raw.Length - read)) > 0)
            {
                read += n;
            }
            if (read < raw.Length)
            {
                throw new InvalidDataException("The PNG's image data is cut short.");
            }
        }

        // Undo each row's filter (PNG specification, section 9), then decode the metres.
        var previous = new byte[stride];
        var row = new byte[stride];
        var metres = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            Array.Copy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var i = 0; i < stride; i++)
            {
                int left = i >= channels ? row[i - channels] : 0;
                int up = previous[i];
                int upLeft = i >= channels ? previous[i - channels] : 0;
                row[i] = (byte)(row[i] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                });
            }
            for (var x = 0; x < width; x++)
            {
                var p = x * channels;
                metres[y * width + x] = row[p] * 256f + row[p + 1] + row[p + 2] / 256f - 32768f;
            }
            (previous, row) = (row, previous);
        }
        return new TerrariumTile(width, metres);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
