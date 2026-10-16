namespace VientoHud.Core.Rain.Observations;

/// <summary>Source of precipitation reported at weather stations, to check rain forecasts against what happened.</summary>
public interface IPrecipitationObservationSource
{
    /// <summary>Every report of the given stations with <c>from &lt;= Time &lt; to</c>, ordered by station and time.</summary>
    /// <exception cref="PrecipitationObservationException">The source failed.</exception>
    Task<IReadOnlyList<PrecipitationReport>> GetReportsAsync(
        IReadOnlyCollection<string> stationIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>A station report and whether it says it is raining (or snowing, drizzling…) there.</summary>
/// <param name="StationId">ICAO code of the station, e.g. LEMD.</param>
/// <param name="Time">Report time, in UTC.</param>
/// <param name="Precipitation">True when some precipitation falls at the station (not only in the vicinity).</param>
/// <param name="Codes">METAR present weather groups as reported, e.g. "-RA BR"; empty when there are none.</param>
public sealed record PrecipitationReport(string StationId, DateTimeOffset Time, bool Precipitation, string Codes)
{
    public DateTimeOffset Time { get; } = Time.Offset == TimeSpan.Zero
        ? Time
        : throw new ArgumentException("Time must be in UTC.", nameof(Time));
}

/// <summary>Precipitation observations could not be obtained.</summary>
public sealed class PrecipitationObservationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Reading METAR present weather.</summary>
public static class MetarWeather
{
    // Precipitation types in METAR (WMO code table 4678): drizzle, rain, snow, snow grains, ice pellets, hail, small hail,
    // unknown precipitation.
    private static readonly string[] PrecipitationTypes = ["DZ", "RA", "SN", "SG", "PL", "GR", "GS", "UP"];

    /// <summary>
    /// True when the present weather groups report precipitation at the station. Groups "in the vicinity" (VC…) and
    /// "recent" (RE…) do not count, nor snow blown or drifting from the ground (BLSN, DRSN); intensity (-, +) and other
    /// descriptors (SH, TS, FZ) do not matter.
    /// </summary>
    public static bool HasPrecipitation(string codes) =>
        codes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(group => !new[] { "VC", "RE", "BL", "DR" }.Any(p => group.StartsWith(p, StringComparison.Ordinal)))
            .Select(group => group.TrimStart('-', '+'))
            .Any(group => PrecipitationTypes.Any(type => ContainsPair(group, type)));

    // Groups are made of two-letter pieces ("TSRA" = TS + RA); look only at whole pieces so "BR" + "A..." never matches.
    private static bool ContainsPair(string group, string type)
    {
        for (var i = 0; i + 2 <= group.Length; i += 2)
        {
            if (string.CompareOrdinal(group, i, type, 0, 2) == 0)
            {
                return true;
            }
        }
        return false;
    }
}
