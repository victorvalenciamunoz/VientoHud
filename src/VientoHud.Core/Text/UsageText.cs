using VientoHud.Core.Net;
using static VientoHud.Core.Text.SpanishText;

namespace VientoHud.Core.Text;

/// <summary>The CONSUMO panel's lines (phase 9): what the app took from each service today and this month.</summary>
public static class UsageText
{
    public static string Name(UsageService service) => service switch
    {
        UsageService.Thunderforest => "Mapa (Thunderforest)",
        UsageService.OpenStreetMap => "Mapa (OpenStreetMap)",
        UsageService.OpenMeteo => "Viento y lluvia (Open-Meteo)",
        UsageService.BRouter => "Rutas (BRouter)",
        UsageService.OperaRadar => "Radar (OPERA)",
        UsageService.Terrain => "Relieve 3D",
        _ => "Otros",
    };

    /// <summary>What a service counts in, for the lines.</summary>
    public static string Unit(UsageService service) => service switch
    {
        UsageService.Thunderforest or UsageService.OpenStreetMap or UsageService.Terrain => "teselas",
        UsageService.OpenMeteo => "llamadas",
        _ => "peticiones",
    };

    /// <summary>
    /// "Hoy 325 de 10.000 llamadas (3 %) · mes 425 de 300.000 · 3,1 MB": the day and month against their limits when
    /// the service has them, and the data received this month when it was measured.
    /// </summary>
    public static string Line(UsageRow row)
    {
        var limit = row.Limit;
        var unit = Unit(row.Service);
        var today = limit.PerDay is { } day
            ? $"hoy {Count(row.Today.Calls)} de {Count(day)} {unit} ({Percent(row.Today.Calls / day)})"
            : $"hoy {Count(row.Today.Calls)} {unit}";
        var month = limit.PerMonth is { } perMonth
            ? $"mes {Count(row.Month.Calls)} de {Count(perMonth)} ({Percent(row.Month.Calls / perMonth)})"
            : $"mes {Count(row.Month.Calls)}";
        var parts = new List<string> { Capitalized(today), month };
        if (row.Month.Bytes > 0)
        {
            parts.Add(Megabytes(row.Month.Bytes));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>The services used this month, busiest first; the rest are left out.</summary>
    public static IReadOnlyList<UsageRow> Used(IReadOnlyList<UsageRow> rows) =>
        rows.Where(r => r.Month.Calls > 0 || r.LastHour.Calls > 0)
            .OrderByDescending(r => r.Busiest ?? -1)
            .ThenByDescending(r => r.Month.Calls)
            .ToList();

    private static string Count(double calls) => calls.ToString("#,0", Spanish);

    private static string Megabytes(long bytes) => $"{(bytes / 1024.0 / 1024.0).ToString("0.0", Spanish)} MB";
}
