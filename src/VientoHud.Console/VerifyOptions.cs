using System.Globalization;
using VientoHud.Core;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>verify</c> command.</summary>
internal sealed record VerifyOptions(
    Coordinate? Location,
    int Days,
    DateOnly Until,
    int Stations,
    int RadiusKm,
    IReadOnlyList<string> Models)
{
    public static readonly IReadOnlyList<string> DefaultModels = ["meteofrance_arome_france", "best_match", "ecmwf_ifs"];

    public const string Usage =
        "vientohud verify [--lat <grados> --lon <grados>] [--days <1-730>] [--until <aaaa-mm-dd>]\n" +
        "                 [--stations <1-20>] [--radius <km>] [--models <modelo,modelo,...>]";

    private static readonly HashSet<string> Known =
        ["--lat", "--lon", "--days", "--until", "--stations", "--radius", "--models"];

    /// <summary>
    /// Parses the arguments after the command name. <paramref name="today"/> (UTC) is the default end:
    /// the period stops at 00:00 UTC of <c>--until</c>, so the last day judged is the one before.
    /// </summary>
    public static VerifyOptions? Parse(IReadOnlyList<string> args, DateOnly today, out string? error)
    {
        var values = CommandArgs.Parse(args, Known, out error);
        if (values is null || !CommandArgs.TryGetLocation(values, out var location, out error))
        {
            return null;
        }

        int days = 30, stations = 5, radius = 100;
        if (!CommandArgs.TryGetInt(values, "--days", 1, 730, ref days, out error)
            || !CommandArgs.TryGetInt(values, "--stations", 1, 20, ref stations, out error)
            || !CommandArgs.TryGetInt(values, "--radius", 1, 1000, ref radius, out error))
        {
            return null;
        }

        var until = today;
        if (values.TryGetValue("--until", out var untilText)
            && !DateOnly.TryParseExact(untilText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out until))
        {
            error = $"Fecha no válida para --until: {untilText} (formato aaaa-mm-dd).";
            return null;
        }
        if (until > today)
        {
            error = "--until no puede ser posterior a hoy: el juez solo mira días pasados.";
            return null;
        }

        var models = values.TryGetValue("--models", out var modelsText)
            ? modelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultModels;
        if (models.Count == 0)
        {
            error = "--models necesita al menos un modelo.";
            return null;
        }

        return new VerifyOptions(location, days, until, stations, radius, models);
    }
}
