#:project ../../src/VientoHud.Core
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Towns;
using VientoHud.Core.Towns.Ghsl;
// dotnet run tools/investigacion/towns.cs -- <carpeta salida> [radio km] [lugar:lat:lon ...]
// Fase 6, paso 6e-1: las manchas de pueblo (TownMap) de GHSL alrededor de los sitios de prueba. Por sitio, un GPX con el
// contorno de cada pueblo (un tramo por pueblo, para gpx.studio) y una imagen: gris lo construido, rojo los pueblos,
// negro su contorno simplificado. Los cuadros de GHSL se guardan en %LOCALAPPDATA%\VientoHud\ghsl (26 MB el de Madrid
// y León, se bajan una vez).
var output = args[0];
var radiusKm = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 40;
Directory.CreateDirectory(output);
(string Name, Coordinate Start)[] places = args.Length > 2
    ? args.Skip(2).Select(a => a.Split(':')).Select(p => (p[0], new Coordinate(
        double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture)))).ToArray()
    :
    [
        ("leon", new(42.5987, -5.5671)), ("labaneza", new(42.3000, -5.8980)), ("humanes", new(40.2526, -3.8299)),
        ("trescantos", new(40.6006, -3.7083)), ("fuenlabrada", new(40.2842, -3.7942)),
    ];

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VientoHud", "ghsl");
var source = new GhslBuiltUpSource(http, new GhslOptions { CacheDirectory = cache });
XNamespace gpx = "http://www.topografix.com/GPX/1/1";

Console.WriteLine("sitio         casillas    leer   pueblos  pueblos km²  esquinas  el mayor km²  ¿salida en pueblo?");
foreach (var (name, start) in places)
{
    var watch = Stopwatch.StartNew();
    var grid = await source.GetAroundAsync(start, radiusKm);
    var read = watch.ElapsedMilliseconds;
    watch.Restart();
    var map = TownMap.From(grid);
    var build = watch.ElapsedMilliseconds;
    Console.WriteLine($"{name,-12} {grid.Width,4}x{grid.Height,-4} {read,5} ms {build,5} ms {map.Towns.Count,5} {map.Towns.Sum(t => t.AreaKm2),10:0} " +
                      $"{map.Towns.Sum(t => t.Outline.Count),9} {map.Towns.FirstOrDefault()?.AreaKm2,12:0}   {(map.Contains(start) ? "sí" : "no")}");

    var document = new XDocument(new XElement(gpx + "gpx", new XAttribute("version", "1.1"), new XAttribute("creator", "VientoHud"),
        new XElement(gpx + "metadata", new XElement(gpx + "name", $"pueblos {name}"),
            new XElement(gpx + "copyright", new XAttribute("author", source.Attribution))),
        map.Towns.Select((t, i) => new XElement(gpx + "trk",
            new XElement(gpx + "name", $"pueblo {i + 1} ({t.AreaKm2:0.0} km²)"),
            new XElement(gpx + "trkseg", t.Outline.Append(t.Outline[0]).Select(c => new XElement(gpx + "trkpt",
                new XAttribute("lat", c.Latitude.ToString("0.######", CultureInfo.InvariantCulture)),
                new XAttribute("lon", c.Longitude.ToString("0.######", CultureInfo.InvariantCulture)))))))));
    document.Save(Path.Combine(output, $"pueblos-{name}.gpx"));

    // Image: one pixel per cell.
    var pixels = new byte[grid.Width * grid.Height * 4];
    for (var y = 0; y < grid.Height; y++)
    {
        for (var x = 0; x < grid.Width; x++)
        {
            var centre = new Coordinate(grid.North - (y + 0.5) * grid.CellDegrees, grid.West + (x + 0.5) * grid.CellDegrees);
            var grey = (byte)(255 - 160 * Math.Min(1, grid[x, y] / 0.15));
            var (r, g, b) = map.Contains(centre) ? ((byte)235, (byte)150, (byte)150) : (grey, grey, grey);
            var i = (y * grid.Width + x) * 4;
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (r, g, b, 255);
        }
    }
    foreach (var town in map.Towns)
    {
        for (var k = 0; k < town.Outline.Count; k++)
        {
            var a = town.Outline[k];
            var b = town.Outline[(k + 1) % town.Outline.Count];
            for (var s = 0.0; s <= 1; s += 0.02)
            {
                var p = new Coordinate(a.Latitude + (b.Latitude - a.Latitude) * s, a.Longitude + (b.Longitude - a.Longitude) * s);
                if (grid.CellOf(p) is var (x, y))
                {
                    var i = (y * grid.Width + x) * 4;
                    (pixels[i], pixels[i + 1], pixels[i + 2]) = ((byte)0, (byte)0, (byte)0);
                }
            }
        }
    }
    await using var png = File.Create(Path.Combine(output, $"pueblos-{name}.png"));
    PngWriter.Write(png, new RgbaImage(grid.Width, grid.Height, pixels));
}
