using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Rain;

public class RainImageTests
{
    private static readonly LambertAzimuthalEqualArea Opera =
        LambertAzimuthalEqualArea.FromProj("+proj=laea +lat_0=55 +lon_0=10 +x_0=1950000 +y_0=-2100000 +ellps=WGS84");

    private static readonly Coordinate Madrid = new(40.4168, -3.7038);
    private static readonly DateTimeOffset Time = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    // 121 × 121 cells of 1 km with Madrid in the middle; rain and no data in the corners.
    private static RainGrid Grid()
    {
        var (x, y) = Opera.Forward(Madrid);
        var left = Math.Floor(x / 1000) * 1000 - 60_000;
        var top = Math.Ceiling(y / 1000) * 1000 + 60_000;
        var rates = new float[121 * 121];
        rates[0] = 3f; // north-west corner: 30.7 dBZ
        rates[120] = float.NaN; // north-east corner
        rates[120 * 121] = 100f; // south-west corner: capped
        return new RainGrid(Time, Opera, left, top, 1000, 121, 121, rates);
    }

    [Theory]
    [InlineData(0.2f, 0, 0, 0)] // 11.8 dBZ: below 12, dry
    [InlineData(0.21f, 0, 0, 252)] // 12.2 dBZ
    [InlineData(1f, 0, 148, 252)] // 23 dBZ, in the 18-24 step
    [InlineData(40f, 255, 255, 0)] // 48.6 dBZ
    public void Colours_follow_the_aemet_legend(float mmPerHour, byte r, byte g, byte b)
    {
        Assert.Equal((r, g, b), RainImage.ColorOf(mmPerHour));
    }

    [Fact]
    public void Each_rate_gets_the_colour_of_its_reflectivity_step()
    {
        foreach (var (fromDbz, r, g, b) in RainImage.Legend.Where(l => l.FromDbz < Reflectivity.CapDbz))
        {
            var rate = (float)Reflectivity.ToRainRate(fromDbz + 3);
            Assert.Equal((r, g, b), RainImage.ColorOf(rate));
        }
        Assert.Equal(((byte)127, (byte)127, (byte)127), RainImage.ColorOf(float.NaN));
        Assert.Equal(((byte)0, (byte)0, (byte)0), RainImage.ColorOf(0));
    }

    [Fact]
    public void Draws_two_pixels_per_km_north_up_with_a_cross_on_the_mark()
    {
        var image = RainImage.Render(Grid(), Madrid);

        Assert.Equal(242, image.Width);
        Assert.Equal(242, image.Height);
        Assert.Equal(((byte)67, (byte)131, (byte)35, (byte)255), image[0, 0]); // 30 dBZ, north-west
        Assert.Equal(((byte)127, (byte)127, (byte)127, (byte)255), image[241, 1]); // no data, north-east
        Assert.Equal(((byte)255, (byte)187, (byte)0, (byte)255), image[1, 241]); // capped: 54-60 dBZ colour
        // Madrid falls in cell (60, 60): pixels 120-121.
        var (x, y) = Opera.Forward(Madrid);
        var cx = 120 + (int)Math.Floor(x % 1000 / 500);
        var cy = 120 + (int)Math.Floor((1000 - y % 1000) % 1000 / 500);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), image[cx, cy]);
        Assert.Equal(((byte)255, (byte)255, (byte)255, (byte)255), image[cx + 7, cy]);
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), image[cx + 7, cy + 1]);
    }

    [Fact]
    public void Draws_a_ring_50_km_from_the_mark()
    {
        var image = RainImage.Render(Grid(), Madrid);
        var ring = ((byte)90, (byte)90, (byte)90, (byte)255);

        // Going east from the mark, the first ring pixel is ~50 km (100 pixels) away.
        var east = Enumerable.Range(122, 119).Select(x => image[x, 121]).ToList();
        Assert.InRange(east.IndexOf(ring) + 1, 98, 102);
    }

    [Fact]
    public void Two_km_cells_are_four_pixels()
    {
        var grid = new RainGrid(Time, Opera, 0, 0, 2000, 3, 5, new float[15]);

        var image = RainImage.Render(grid, Opera.Inverse(5000, -3000));

        Assert.Equal(20, image.Width);
        Assert.Equal(12, image.Height);
    }

    [Fact]
    public void Writes_a_valid_png_with_the_same_pixels()
    {
        var image = RainImage.Render(Grid(), Madrid);
        using var stream = new MemoryStream();

        PngWriter.Write(stream, image);

        var png = stream.ToArray();
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        var chunks = ReadChunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(242, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(242, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 6, 0, 0, 0], header[8..]);

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }
        var raw = inflated.ToArray();
        var expected = image.ToArray();
        Assert.Equal(242 * (1 + 242 * 4), raw.Length);
        for (var y = 0; y < 242; y++)
        {
            Assert.Equal(0, raw[y * (1 + 242 * 4)]);
            Assert.Equal(expected.AsSpan(y * 242 * 4, 242 * 4), raw.AsSpan(y * (1 + 242 * 4) + 1, 242 * 4));
        }
    }

    // Splits a PNG into its chunks, checking each CRC with an independent bitwise CRC-32.
    private static List<(string Type, byte[] Data)> ReadChunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        var position = 8;
        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            var typeAndData = png.AsSpan(position + 4, 4 + length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length));
            Assert.Equal(BitwiseCrc32(typeAndData), crc);
            chunks.Add((Encoding.ASCII.GetString(typeAndData[..4]), typeAndData[4..].ToArray()));
            position += 12 + length;
        }
        return chunks;
    }

    private static uint BitwiseCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
