using VientoHud.ConsoleApp;
using VientoHud.Core.Location;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Usage = WindOptions.Usage;

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

switch (args[0])
{
    case "wind":
        var options = WindOptions.Parse(args[1..], out var error);
        if (options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(WindOptions.Usage);
            return 2;
        }

        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
        {
            var command = new WindCommand(
                new OpenMeteoWindForecastProvider(http), new WindowsLocationProvider(), Console.Out);
            try
            {
                await command.RunAsync(options, cancellation.Token);
                return 0;
            }
            catch (Exception ex) when (ex is WindForecastException or LocationUnavailableException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
            catch (OperationCanceledException)
            {
                return 130;
            }
        }

    default:
        Console.Error.WriteLine($"Comando desconocido: {args[0]}");
        Console.Error.WriteLine(Usage);
        return 2;
}
