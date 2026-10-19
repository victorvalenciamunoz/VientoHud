#:project ../../src/VientoHud.Core
using System.Globalization;
using VientoHud.Core;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Wind.OpenMeteo;
// dotnet run tools/investigacion/requests.cs -- [km] [hh:mm local]
// Fase 7: en qué se van las peticiones a BRouter de una recomendación (RidePlanner, como `route`), en Fuenlabrada y
// León: cuántas por rumbo y forma, cuántas piden exactamente los mismos puntos que otra anterior (se podrían ahorrar sin
// cambiar nada) y cuánto tarda cada una. BRouter local (VIENTOHUD_BROUTER_URL) sin esperas entre peticiones.
var km = args.Length > 0 ? double.Parse(args[0], CultureInfo.InvariantCulture) : 60;
var now = DateTimeOffset.UtcNow;
var departure = args.Length > 1 ? DepartureTime.Next(TimeOnly.Parse(args[1], CultureInfo.InvariantCulture), now, TimeZoneInfo.Local) : now;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var brouter = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL") ?? "http://localhost:17777/";

(string Name, Coordinate Start)[] places =
[
    ("fuenlabrada", new(40.2842, -3.7942)), ("leon", new(42.5987, -5.5671)), ("humanes", new(40.2531, -3.8287)),
    ("trescantos", new(40.6006, -3.7083)), ("labaneza", new(42.3000, -5.8980)),
];

foreach (var (name, start) in places)
{
    var engine = new RecordingEngine(new BRouterRouteEngine(http, new BRouterOptions { BaseUri = new Uri(brouter), MinRequestInterval = TimeSpan.Zero }));
    var planner = new RidePlanner(new LoopGenerator(engine), new OpenMeteoWindForecastProvider(http));
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var plan = await planner.PlanAsync(start, km, departure, progress: new Progress(engine));
    Console.WriteLine($"== {name}: {engine.Calls.Count} peticiones en {clock.Elapsed.TotalSeconds:0} s, " +
                      $"{engine.Calls.Count(c => c.Repeated)} repetidas (mismos puntos), {engine.Calls.Count(c => c.Unreachable)} sin unir; " +
                      $"viento {plan.Wind.Kind}, rumbos {string.Join(", ", plan.Bearings.Select(b => b.ToString("0")))}");
    foreach (var group in engine.Calls.GroupBy(c => c.Bearing))
    {
        var line = string.Join(" ", group.Select(c => $"{c.Seconds:0.0}{(c.Repeated ? "r" : "")}{(c.Unreachable ? "x" : "")}"));
        Console.WriteLine($"   {group.Key,-28} {group.Count(),3} peticiones: {line}");
    }
}

/// <summary>Counts the requests, flags those with the same waypoints as an earlier one and times them.</summary>
sealed class RecordingEngine(IRouteEngine inner) : IRouteEngine
{
    public List<(string Bearing, double Seconds, bool Repeated, bool Unreachable)> Calls { get; } = [];
    private readonly HashSet<string> _seen = [];
    public string Current { get; set; } = "?";

    public async Task<Route> RouteAsync(IReadOnlyList<Coordinate> waypoints, CancellationToken cancellationToken = default)
    {
        var key = string.Join("|", waypoints.Select(w => $"{w.Latitude:0.00000},{w.Longitude:0.00000}"));
        var repeated = !_seen.Add(key);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var route = await inner.RouteAsync(waypoints, cancellationToken);
            Calls.Add((Current, clock.Elapsed.TotalSeconds, repeated, false));
            return route;
        }
        catch (RouteException ex) when (ex.Failure == RouteFailure.Unreachable)
        {
            Calls.Add((Current, clock.Elapsed.TotalSeconds, repeated, true));
            throw;
        }
    }
}

/// <summary>Labels the calls made so far with the bearing and shape just finished (the generator reports after each).</summary>
sealed class Progress(RecordingEngine engine) : IProgress<LoopResult>
{
    private int _labelled;

    public void Report(LoopResult value)
    {
        var label = $"{(value.Shape == RouteShape.OutAndBack ? "ida y vuelta" : "vuelta")} {value.BearingDegrees:0}°";
        for (; _labelled < engine.Calls.Count; _labelled++)
        {
            engine.Calls[_labelled] = engine.Calls[_labelled] with { Bearing = label };
        }
    }
}
