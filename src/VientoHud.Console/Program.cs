using VientoHud.ConsoleApp;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Routing.BRouter;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;
using VientoHud.Core.Wind.Verification;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Usage = "Uso:\n  " + WindOptions.Usage + "\n  " + VerifyOptions.Usage + "\n  " + RouteOptions.Usage;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(Usage);
    return args.Length == 0 ? 2 : 0;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var device = new WindowsLocationProvider();

// Each command returns its exit code.
Func<Task<int>>? run = null;
string? error = null;
switch (args[0])
{
    case "wind":
        if (WindOptions.Parse(args[1..], out error) is { } windOptions)
        {
            run = async () =>
            {
                await new WindCommand(new OpenMeteoWindForecastProvider(http), device, Console.Out)
                    .RunAsync(windOptions, cancellation.Token);
                return 0;
            };
        }
        break;

    case "verify":
        if (VerifyOptions.Parse(args[1..], DateOnly.FromDateTime(DateTime.UtcNow), out error) is { } verifyOptions)
        {
            var judge = new ForecastJudge(new IemWindObservationSource(http), new OpenMeteoPastWindForecastSource(http));
            run = async () =>
            {
                await new VerifyCommand(judge, device, Console.Out).RunAsync(verifyOptions, cancellation.Token);
                return 0;
            };
        }
        break;

    case "route":
        if (RouteOptions.Parse(args[1..], out error) is { } routeOptions && BRouterServer(out error) is { } brouter)
        {
            var planner = new RidePlanner(
                new LoopGenerator(new BRouterRouteEngine(http, brouter.Options)), new OpenMeteoWindForecastProvider(http));
            run = () => new RouteCommand(planner, device, Console.Out, brouter.Name, OpenMeteoWindForecastProvider.Attribution)
                .RunAsync(routeOptions, cancellation.Token);
        }
        break;

    default:
        error = $"Comando desconocido: {args[0]}";
        break;
}

if (run is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    return await run();
}
catch (Exception ex) when (ex is WindForecastException or WindObservationException or LocationUnavailableException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    return 130;
}

// The BRouter server: brouter.de, or the one in VIENTOHUD_BROUTER_URL (a local one for development, see
// tools/brouter-local and DECISIONS 018), asked without waiting between requests. Null and an error if the URL is bad.
static (BRouterOptions Options, string Name)? BRouterServer(out string? error)
{
    error = null;
    var url = Environment.GetEnvironmentVariable("VIENTOHUD_BROUTER_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        return (new BRouterOptions(), "brouter.de");
    }
    if (!Uri.TryCreate(url.EndsWith('/') ? url : url + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
    {
        error = $"VIENTOHUD_BROUTER_URL no es una URL válida: {url}";
        return null;
    }
    return (new BRouterOptions { BaseUri = uri, MinRequestInterval = TimeSpan.Zero }, uri.ToString());
}
