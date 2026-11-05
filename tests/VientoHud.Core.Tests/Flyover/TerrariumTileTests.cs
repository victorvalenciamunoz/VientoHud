using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using VientoHud.Core.Flyover;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Flyover;

public partial class TerrariumTileTests
{
    // A Terrarium PNG of the given size whose pixel (x, y) is metres(x, y), with the row filters cycling through the
    // five kinds so that all of them are undone.
    private static byte[] Png(int size, Func<int, int, double> metres, int channels = 3)
    {
        var raw = new MemoryStream();
        var previous = new byte[size * channels];
        for (var y = 0; y < size; y++)
        {
            var row = new byte[size * channels];
            for (var x = 0; x < size; x++)
            {
                var v = metres(x, y) + 32768;
                row[x * channels] = (byte)Math.Floor(v / 256);
                row[x * channels + 1] = (byte)Math.Floor(v % 256);
                row[x * channels + 2] = (byte)Math.Floor((v - Math.Floor(v)) * 256);
                if (channels == 4)
                {
                    row[x * channels + 3] = 255;
                }
            }
            var filter = (byte)(y % 5);
            raw.WriteByte(filter);
            for (var i = 0; i < row.Length; i++)
            {
                int left = i >= channels ? row[i - channels] : 0;
                int up = previous[i];
                int upLeft = i >= channels ? previous[i - channels] : 0;
                var predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                raw.WriteByte((byte)(row[i] - predicted));
            }
            previous = row;
        }
        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }
        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)size);
        header[8] = 8;
        header[9] = (byte)(channels == 4 ? 6 : 2);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        png.Write(length);
        png.Write(System.Text.Encoding.ASCII.GetBytes(type));
        png.Write(data);
        png.Write(new byte[4]); // CRC: not checked by the reader.
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Decodes_the_metres_of_every_pixel_whatever_the_filter(int channels)
    {
        var tile = TerrariumTile.Read(Png(16, (x, y) => 700 + x * 10 + y * 0.5 - (x == 3 ? 900 : 0), channels));

        Assert.Equal(16, tile.Size);
        Assert.Equal(700, tile.At(0, 0), 2);
        Assert.Equal(700 + 150 + 7.5, tile.At(15, 15), 2);
        Assert.Equal(700 + 30 + 2 - 900, tile.At(3, 4), 2);
    }

    [Fact]
    public void Interpolates_between_pixel_centres()
    {
        var tile = TerrariumTile.Read(Png(4, (x, y) => x * 100));

        Assert.Equal(0, tile.AtPixel(0.5, 0.5), 6);
        Assert.Equal(50, tile.AtPixel(1, 2), 6);
        Assert.Equal(150, tile.AtPixel(2, 1), 6);
        // Beyond the last centre it keeps the edge.
        Assert.Equal(300, tile.AtPixel(4, 1), 6);
    }

    [Fact]
    public void Rejects_what_is_not_a_terrarium_png()
    {
        Assert.Throws<InvalidDataException>(() => TerrariumTile.Read([1, 2, 3]));
        var png = Png(4, (_, _) => 0);
        png[24] = 16; // bit depth 16
        Assert.Throws<InvalidDataException>(() => TerrariumTile.Read(png));
    }
}
