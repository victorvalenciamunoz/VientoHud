using VientoHud.Core;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>wind</c> command.</summary>
internal sealed record WindOptions(Coordinate? Location, int Hours)
{
    public const int DefaultHours = 6;
    public const int MaxHours = 168;

    public const string Usage = "vientohud wind [--lat <grados> --lon <grados>] [--hours <1-168>]";

    private static readonly HashSet<string> Known = ["--lat", "--lon", "--hours"];

    /// <summary>Parses the arguments after the command name. Returns null and an error message when invalid.</summary>
    public static WindOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var values = CommandArgs.Parse(args, Known, out error);
        if (values is null || !CommandArgs.TryGetLocation(values, out var location, out error))
        {
            return null;
        }

        var hours = DefaultHours;
        return CommandArgs.TryGetInt(values, "--hours", 1, MaxHours, ref hours, out error)
            ? new WindOptions(location, hours)
            : null;
    }
}
