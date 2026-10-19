// dotnet run tools/investigacion/ghslfixture.cs -- <tif de GHSL> <fila de bloque> <columna de bloque> <filas> <columnas> <salida.zip>
// Fase 6, paso 6e-1: recorta unos bloques de un cuadro de GHSL BUILT-S (BigTIFF, bloques de 256 × 256 con LZW) en un
// BigTIFF pequeño, copiando los bloques comprimidos tal cual y moviendo el punto de anclaje, y lo mete en un zip como los
// de GHSL. Es la fixture de los tests de GhslBuiltUpSource (datos reales, © European Union, CC BY 4.0).
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

var bytes = File.ReadAllBytes(args[0]);
int blockRow0 = int.Parse(args[1]), blockColumn0 = int.Parse(args[2]), rows = int.Parse(args[3]), columns = int.Parse(args[4]);
var output = args[5];

var ifd = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8));
var count = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(ifd));
var tags = new Dictionary<int, (ushort Type, long Count, byte[] Data)>();
for (var i = 0; i < count; i++)
{
    var e = bytes.AsSpan(ifd + 8 + i * 20, 20);
    var type = BinaryPrimitives.ReadUInt16LittleEndian(e[2..]);
    var n = (long)BinaryPrimitives.ReadUInt64LittleEndian(e[4..]);
    var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, _ => 8 };
    var data = size * n <= 8 ? e.Slice(12, (int)(size * n)).ToArray()
        : bytes.AsSpan((int)BinaryPrimitives.ReadUInt64LittleEndian(e[12..]), (int)(size * n)).ToArray();
    tags[BinaryPrimitives.ReadUInt16LittleEndian(e)] = (type, n, data);
}
long Integer(int tag, int index) => tags[tag].Type switch
{
    3 => BinaryPrimitives.ReadUInt16LittleEndian(tags[tag].Data.AsSpan(index * 2)),
    4 => BinaryPrimitives.ReadUInt32LittleEndian(tags[tag].Data.AsSpan(index * 4)),
    _ => (long)BinaryPrimitives.ReadUInt64LittleEndian(tags[tag].Data.AsSpan(index * 8)),
};
double Double(int tag, int index) => BinaryPrimitives.ReadDoubleLittleEndian(tags[tag].Data.AsSpan(index * 8));

var block = (int)Integer(322, 0);
var across = (int)((Integer(256, 0) + block - 1) / block);
var blocks = new List<byte[]>();
for (var r = 0; r < rows; r++)
{
    for (var c = 0; c < columns; c++)
    {
        var index = (blockRow0 + r) * across + blockColumn0 + c;
        blocks.Add(bytes.AsSpan((int)Integer(324, index), (int)Integer(325, index)).ToArray());
    }
}
var scaleX = Double(33550, 0);
var scaleY = Double(33550, 1);
var west = Double(33922, 3) + blockColumn0 * block * scaleX;
var north = Double(33922, 4) - blockRow0 * block * scaleY;

// Layout: header (16), blocks, then the directory and its long values.
var file = new MemoryStream();
file.Write(new byte[16]);
var offsets = new List<long>();
foreach (var b in blocks)
{
    offsets.Add(file.Position);
    file.Write(b);
}
var entries = new SortedDictionary<int, (ushort Type, long Count, byte[] Data)>
{
    [256] = Short(columns * block), [257] = Short(rows * block), [258] = Short(16), [259] = Short(5), [262] = Short(1),
    [277] = Short(1), [284] = Short(1), [317] = Short(1), [322] = Short(block), [323] = Short(block), [339] = Short(1),
    [324] = Long8s(offsets), [325] = Long8s(blocks.Select(b => (long)b.Length)),
    [33550] = Doubles(scaleX, scaleY, 0), [33922] = Doubles(0, 0, 0, west, north, 0),
};
var extra = new MemoryStream();
var ifdOffset = file.Position;
var extraStart = ifdOffset + 8 + entries.Count * 20 + 8;
var directory = new MemoryStream();
directory.Write(BitConverter.GetBytes((ulong)entries.Count));
foreach (var (tag, (type, n, data)) in entries)
{
    directory.Write(BitConverter.GetBytes((ushort)tag));
    directory.Write(BitConverter.GetBytes(type));
    directory.Write(BitConverter.GetBytes((ulong)n));
    var value = new byte[8];
    if (data.Length <= 8)
    {
        data.CopyTo(value, 0);
    }
    else
    {
        BinaryPrimitives.WriteUInt64LittleEndian(value, (ulong)(extraStart + extra.Position));
        extra.Write(data);
    }
    directory.Write(value);
}
directory.Write(new byte[8]); // no next directory
file.Write(directory.ToArray());
file.Write(extra.ToArray());
var tiff = file.ToArray();
"II"u8.CopyTo(tiff);
BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 43);
BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(4), 8);
BinaryPrimitives.WriteUInt64LittleEndian(tiff.AsSpan(8), (ulong)ifdOffset);

File.Delete(output);
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
{
    var entry = zip.CreateEntry(Path.GetFileNameWithoutExtension(output) + ".tif", CompressionLevel.Optimal);
    using var stream = entry.Open();
    stream.Write(tiff);
}
Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"{blocks.Count} blocks, {tiff.Length} bytes; west {west:0.######}, north {north:0.######}, {columns * block}x{rows * block} cells"));

static (ushort, long, byte[]) Short(int value) => (3, 1, BitConverter.GetBytes((ushort)value));
static (ushort, long, byte[]) Long8s(IEnumerable<long> values)
{
    var list = values.ToList();
    return (16, list.Count, list.SelectMany(v => BitConverter.GetBytes((ulong)v)).ToArray());
}
static (ushort, long, byte[]) Doubles(params double[] values) => (12, values.Length, values.SelectMany(BitConverter.GetBytes).ToArray());
