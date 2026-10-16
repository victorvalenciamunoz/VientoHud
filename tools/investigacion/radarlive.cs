#:project ../../src/VientoHud.Core
using System.Diagnostics;
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Opera;
// dotnet run tools/investigacion/radarlive.cs -- <lat> <lon> [dbzh|rate] [hora UTC, p. ej. 2026-10-03T06:00Z] [radio km]
// Prueba en vivo de OperaRadarSource: la última imagen (o la de la hora dada), recortada alrededor del punto, con la
// lluvia en el punto, cuánta zona tiene datos y moja, y el tiempo de cada parte.
var center = new Coordinate(
    double.Parse(args[0], CultureInfo.InvariantCulture), double.Parse(args[1], CultureInfo.InvariantCulture));
var product = args.Length > 2 && args[2] == "rate" ? OperaProduct.RainRate : OperaProduct.Reflectivity;
var radius = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 150;

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var source = new OperaRadarSource(http, new OperaOptions { Product = product });

var clock = Stopwatch.StartNew();
var time = args.Length > 3
    ? DateTimeOffset.Parse(args[3], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)
    : await source.GetLatestFrameTimeAsync();
Console.WriteLine($"Imagen: {time:u} ({product}); buscada en {clock.ElapsedMilliseconds} ms; ahora {DateTimeOffset.UtcNow:u}");

clock.Restart();
var grid = await source.GetFrameAsync(time, center, radius);
if (grid is null)
{
    Console.WriteLine("No hay imagen para esa hora.");
    return;
}
Console.WriteLine($"Descargada y recortada en {clock.ElapsedMilliseconds} ms: {grid.Rows}×{grid.Columns} celdas de {grid.CellSizeMeters / 1000} km");

var cells = (from r in Enumerable.Range(0, grid.Rows) from c in Enumerable.Range(0, grid.Columns) select grid[r, c]).ToList();
var withData = cells.Where(v => !float.IsNaN(v)).ToList();
Console.WriteLine($"Con datos: {100.0 * withData.Count / cells.Count:F0} %; mojado: {100.0 * withData.Count(v => v > 0.1) / Math.Max(1, withData.Count):F0} % (> 0,1 mm/h); máx. {(withData.Count > 0 ? withData.Max() : 0):F1} mm/h");
Console.WriteLine($"En el punto: {grid.RateAt(center):F2} mm/h");
Console.WriteLine(source.Attribution);
