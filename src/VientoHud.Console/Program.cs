using VientoHud.ConsoleApp;
using VientoHud.Core.Location;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;
using VientoHud.Core.Wind.Verification;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Usage = "Uso:\n  " + WindOptions.Usage + "\n  " + VerifyOptions.Usage;

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

Func<Task>? run = null;
string? error = null;
switch (args[0])
{
    case "wind":
        if (WindOptions.Parse(args[1..], out error) is { } windOptions)
        {
            run = () => new WindCommand(new OpenMeteoWindForecastProvider(http), device, Console.Out)
                .RunAsync(windOptions, cancellation.Token);
        }
        break;

    case "verify":
        if (VerifyOptions.Parse(args[1..], DateOnly.FromDateTime(DateTime.UtcNow), out error) is { } verifyOptions)
        {
            var judge = new ForecastJudge(new IemWindObservationSource(http), new OpenMeteoPastWindForecastSource(http));
            run = () => new VerifyCommand(judge, device, Console.Out).RunAsync(verifyOptions, cancellation.Token);
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
    await run();
    return 0;
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
