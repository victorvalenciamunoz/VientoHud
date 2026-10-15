using VientoHud.Core;
using VientoHud.Core.Routing;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>route</c> command. Exactly one of <see cref="Km"/> and <see cref="Hours"/> is set.</summary>
internal sealed record RouteOptions(
    Coordinate? Location, double? Km, double? Hours, double SpeedKmh, int Count, double? Bearing, string OutputDirectory)
{
    public const int DefaultCount = 3;
    public const int MaxCount = 6;

    public const string Usage =
        "vientohud route (--km <10-300> | --hours <0,5-12> [--speed <10-45 km/h>]) [--lat <grados> --lon <grados>]\n" +
        "                [--count <1-6>] [--bearing <0-360>] [--out <carpeta>]";

    private static readonly HashSet<string> Known = ["--km", "--hours", "--speed", "--lat", "--lon", "--count", "--bearing", "--out"];

    /// <summary>Parses the arguments after the command name. Returns null and an error message when invalid.</summary>
    public static RouteOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var values = CommandArgs.Parse(args, Known, out error);
        if (values is null
            || !CommandArgs.TryGetLocation(values, out var location, out error)
            || !CommandArgs.TryGetDouble(values, "--km", 10, 300, out var km, out error)
            || !CommandArgs.TryGetDouble(values, "--hours", 0.5, 12, out var hours, out error)
            || !CommandArgs.TryGetDouble(values, "--speed", 10, 45, out var speed, out error)
            || !CommandArgs.TryGetDouble(values, "--bearing", 0, 360, out var bearing, out error))
        {
            return null;
        }
        if (km is null == hours is null)
        {
            error = "Indica la distancia (--km) o el tiempo (--hours), uno de los dos.";
            return null;
        }
        if (speed is not null && hours is null)
        {
            error = "--speed solo vale con --hours.";
            return null;
        }

        var count = DefaultCount;
        if (!CommandArgs.TryGetInt(values, "--count", 1, MaxCount, ref count, out error))
        {
            return null;
        }
        return new RouteOptions(
            location, km, hours, speed ?? RideDistance.DefaultAverageSpeedKmh, count, bearing,
            values.GetValueOrDefault("--out", "."));
    }
}
