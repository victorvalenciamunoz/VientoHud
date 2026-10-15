#:project ../../src/VientoHud.Core
using System.Net;
using VientoHud.Core;
using VientoHud.Core.Gpx;
using VientoHud.Core.Routing.BRouter;
// dotnet run togpx.cs -- <in.geojson> <out.gpx> <name>
var body = File.ReadAllText(args[0]);
var engine = new BRouterRouteEngine(new HttpClient(new Stub(body)), new BRouterOptions { ServerProfile = "x", MinRequestInterval = TimeSpan.Zero });
var route = await engine.RouteAsync([new Coordinate(0, 0), new Coordinate(0, 1)]);
using var f = File.Create(args[1]);
GpxWriter.Write(new GpxTrack(args[2], route.Points.Select(p => new GpxTrackPoint(p.Position, p.ElevationMeters)).ToList(), DateTimeOffset.UtcNow, Copyright: GpxCopyright.OpenStreetMap), f);
Console.WriteLine($"{args[1]}: {route.DistanceKm:0.0} km");
class Stub(string b) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(b) }); }
