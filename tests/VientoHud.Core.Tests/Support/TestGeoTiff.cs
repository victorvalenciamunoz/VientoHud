using System.Buffers.Binary;
using System.IO.Compression;

namespace VientoHud.Core.Tests.Support;

/// <summary>Writes small uncompressed tiled BigTIFFs like GHSL's (16-bit, 256-cell blocks), zipped as GHSL serves them.</summary>
internal static class TestGeoTiff
{
    public const double CellDegrees = 10.0 / 12000;

    /// <summary>A zip with one GeoTIFF of <paramref name="size"/> × <paramref name="size"/> cells (a multiple of 256).</summary>
    public static byte[] Zip(double west, double north, int size, Func<int, int, ushort> value)
    {
        var tiff = Tiff(west, north, size, value);
        var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("tile.tif").Open();
            stream.Write(tiff);
        }
        return memory.ToArray();
    }

    private static byte[] Tiff(double west, double north, int size, Func<int, int, ushort> value)
    {
        const int block = 256;
        var across = size / block;
        var file = new MemoryStream();
        file.Write(new byte[16]);
        var offsets = new List<long>();
        for (var by = 0; by < across; by++)
        {
            for (var bx = 0; bx < across; bx++)
            {
                offsets.Add(file.Position);
                var data = new byte[block * block * 2];
                for (var y = 0; y < block; y++)
                {
                    for (var x = 0; x < block; x++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((y * block + x) * 2), value(bx * block + x, by * block + y));
                    }
                }
                file.Write(data);
            }
        }

        var entries = new SortedDictionary<int, (ushort Type, long Count, byte[] Data)>
        {
            [256] = Short(size), [257] = Short(size), [258] = Short(16), [259] = Short(1), [277] = Short(1),
            [322] = Short(block), [323] = Short(block),
            [324] = (16, offsets.Count, offsets.SelectMany(o => BitConverter.GetBytes((ulong)o)).ToArray()),
            [325] = (16, offsets.Count, offsets.SelectMany(_ => BitConverter.GetBytes((ulong)(block * block * 2))).ToArray()),
            [33550] = Doubles(CellDegrees, CellDegrees, 0),
            [33922] = Doubles(0, 0, 0, west, north, 0),
        };
        var ifd = file.Position;
        var extraStart = ifd + 8 + entries.Count * 20 + 8;
        var extra = new MemoryStream();
        file.Write(BitConverter.GetBytes((ulong)entries.Count));
        foreach (var (tag, (type, count, data)) in entries)
        {
            file.Write(BitConverter.GetBytes((ushort)tag));
            file.Write(BitConverter.GetBytes(type));
            file.Write(BitConverter.GetBytes((ulong)count));
            var field = new byte[8];
            if (data.Length <= 8)
            {
                data.CopyTo(field, 0);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(field, (ulong)(extraStart + extra.Position));
                extra.Write(data);
            }
            file.Write(field);
        }
        file.Write(new byte[8]);
        file.Write(extra.ToArray());

        var bytes = file.ToArray();
        "II"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 43);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), (ulong)ifd);
        return bytes;
    }

    private static (ushort, long, byte[]) Short(int value) => (3, 1, BitConverter.GetBytes((ushort)value));

    private static (ushort, long, byte[]) Doubles(params double[] values) =>
        (12, values.Length, values.SelectMany(BitConverter.GetBytes).ToArray());
}
