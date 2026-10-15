#:project ../../src/VientoHud.Core
// Phase 2 judge (STATUS, step 6): generates loops at several places and distances with Core's LoopGenerator and checks
// each one: valid or not, distance error, km per kind of road and with no surface reported, repetition, turn-backs,
// and that its GPX passes the official GPX 1.1 schema. Meant for the local BRouter (tools/brouter-local, DECISIONS 018):
// it makes hundreds of requests.
// dotnet run tools/investigacion/judge.cs -- <outDir> <brouterUrl> <name>:<lat>:<lon> ... -- <km> ...
using System.Globalization;
using System.Xml;
using System.Xml.Schema;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;

var inv = CultureInfo.InvariantCulture;
var outDir = args[0];
Directory.CreateDirectory(outDir);
var places = args.Skip(2).TakeWhile(a => a != "--").Select(a => a.Split(':'))
    .Select(p => (Name: p[0], Start: new Coordinate(double.Parse(p[1], inv), double.Parse(p[2], inv)))).ToList();
var distances = args.SkipWhile(a => a != "--").Skip(1).Select(a => double.Parse(a, inv)).ToList();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var engine = new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(args[1]), MinRequestInterval = TimeSpan.Zero });
var generator = new LoopGenerator(engine);

var schemas = new XmlSchemaSet();
schemas.Add("http://www.topografix.com/GPX/1/1", Path.Combine("tests", "VientoHud.Core.Tests", "Fixtures", "Gpx", "gpx.xsd"));

var rows = new List<string>();
int loops = 0, valid = 0, requests = 0, schemaErrors = 0;
double forbiddenKm = 0, unknownKm = 0, trunkKm = 0, pavedTrackKm = 0, noSurfaceKm = 0, totalKm = 0;
var noSurfaceByHighway = new Dictionary<string, double>();
foreach (var (name, start) in places)
{
    foreach (var km in distances)
    {
        var results = await generator.GenerateManyAsync(start, LoopGenerator.SpreadBearings(3), km);
        foreach (var r in results.OrderBy(r => r.BearingDegrees))
        {
            loops++;
            requests += r.Requests;
            if (r.Best is not { } best)
            {
                rows.Add($"| {name} | {km} | {r.BearingDegrees:0} | sin ruta | | | | | | | | {r.Requests} |");
                continue;
            }
            var a = best.Analysis;
            var noSurface = best.Route.Segments.Where(s => s.Surface is null).Sum(s => s.LengthKm);
            var xml = GpxWriter.ToXml(GpxTrack.FromRoute(best.Route, $"{name} {km} km {r.BearingDegrees:0}", DateTimeOffset.UtcNow));
            File.WriteAllText(Path.Combine(outDir, $"{name}-{km}-{r.BearingDegrees:0}{(r.Found ? "" : "-descartada")}.gpx"), xml);
            var errors = SchemaErrors(xml);
            if (r.Found)
            {
                valid++;
                totalKm += best.Route.DistanceKm;
                forbiddenKm += a.ForbiddenKm;
                unknownKm += a.UnknownKm;
                trunkKm += a.TrunkKm;
                pavedTrackKm += a.PavedTrackKm;
                noSurfaceKm += noSurface;
                foreach (var s in best.Route.Segments.Where(s => s.Surface is null))
                {
                    noSurfaceByHighway[s.Highway ?? "?"] = noSurfaceByHighway.GetValueOrDefault(s.Highway ?? "?") + s.LengthKm;
                }
            }
            schemaErrors += errors;
            rows.Add($"| {name} | {km} | {r.BearingDegrees:0} | {(r.Found ? "✅" : "❌ " + best.Problems)} | {best.Route.DistanceKm:0.0} | " +
                $"{best.DistanceError * 100:+0;-0;0} % | {a.ForbiddenKm + a.UnknownKm:0.0} | {a.TrunkKm:0.0} | {a.PavedTrackKm:0.0} | " +
                $"{noSurface:0.0} | {a.RepeatedFraction * 100:0} % / {a.TurnBacks.Count(t => !t.IsDestination)} | {r.Requests} |");
        }
        Console.Error.WriteLine($"{name} {km} km done");
    }
}

Console.WriteLine("| Salida | Objetivo (km) | Rumbo (°) | Válida | Km | Error | Prohibido o desconocido (km) | Trunk (km) | Camino asfaltado (km) | Sin dato de firme (km) | Repetido / medias vueltas | Peticiones |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
rows.ForEach(Console.WriteLine);
Console.WriteLine();
Console.WriteLine($"Vueltas: {loops}; válidas: {valid}; peticiones: {requests}; GPX con errores de esquema: {schemaErrors}.");
Console.WriteLine($"En las válidas ({totalKm:0} km): prohibido {forbiddenKm:0.0} km, desconocido {unknownKm:0.0} km, trunk {trunkKm:0.0} km, " +
    $"caminos asfaltados {pavedTrackKm:0.0} km, sin dato de firme {noSurfaceKm:0.0} km ({noSurfaceKm / Math.Max(1, totalKm) * 100:0} %).");
Console.WriteLine("Sin dato de firme, por tipo de vía: " + string.Join(", ", noSurfaceByHighway.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.0} km")));

int SchemaErrors(string xml)
{
    var count = 0;
    var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
    settings.ValidationEventHandler += (_, _) => count++;
    using var reader = XmlReader.Create(new StringReader(xml), settings);
    while (reader.Read()) { }
    return count;
}
