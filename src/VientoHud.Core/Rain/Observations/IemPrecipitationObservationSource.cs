using System.Globalization;
using System.Net;
using VientoHud.Core.Wind.Observations.Iem;

namespace VientoHud.Core.Rain.Observations;

/// <summary>
/// METAR present weather from the Iowa Environmental Mesonet archive (public domain, worldwide airports; the same
/// archive as the wind observations, DECISIONS 009). At most one request per second.
/// </summary>
public sealed class IemPrecipitationObservationSource(HttpClient httpClient, IemOptions? options = null)
    : IPrecipitationObservationSource
{
    public const string Attribution = IemWindObservationSource.Attribution;

    private readonly IemOptions _options = options ?? new IemOptions();

    public async Task<IReadOnlyList<PrecipitationReport>> GetReportsAsync(
        IReadOnlyCollection<string> stationIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (stationIds.Count == 0)
        {
            return [];
        }
        if (to <= from)
        {
            throw new ArgumentException("The end must be after the start.", nameof(to));
        }

        var query = string.Join('&',
            stationIds.Select(id => $"station={Uri.EscapeDataString(id)}")
                .Concat(
                [
                    "data=wxcodes", $"sts={FormatUtc(from)}", $"ets={FormatUtc(to)}",
                    "tz=Etc/UTC", "format=onlycomma", "missing=M", "latlon=no",
                ]));
        var uri = new Uri(_options.BaseUri, $"cgi-bin/request/asos.py?{query}");

        string csv;
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            csv = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode == HttpStatusCode.TooManyRequests ? " (rate limit: at most one request per second)" : "";
                throw new PrecipitationObservationException(
                    $"IEM returned HTTP {(int)response.StatusCode}{hint} for {uri.AbsolutePath}: {Truncate(csv)}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new PrecipitationObservationException($"IEM request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PrecipitationObservationException("IEM request timed out.", ex);
        }

        return Parse(csv)
            .Where(r => r.Time >= from && r.Time < to)
            .OrderBy(r => r.StationId, StringComparer.Ordinal)
            .ThenBy(r => r.Time)
            .ToList();
    }

    /// <summary>asos.py "onlycomma" output: a header row, then one row per METAR; "M" when there is no weather group.</summary>
    private static IEnumerable<PrecipitationReport> Parse(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            yield break;
        }

        var header = lines[0].Split(',');
        int Column(string name) => Array.IndexOf(header, name) is var i and >= 0
            ? i
            : throw new PrecipitationObservationException($"IEM reports have no '{name}' column. Header: {lines[0]}");
        var station = Column("station");
        var valid = Column("valid");
        var wxcodes = Column("wxcodes");

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (!DateTimeOffset.TryParseExact(fields[valid], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                throw new PrecipitationObservationException($"IEM report with a bad time: {line}");
            }
            var codes = fields[wxcodes] == "M" ? "" : fields[wxcodes];
            yield return new PrecipitationReport(fields[station], time, MetarWeather.HasPrecipitation(codes), codes);
        }
    }

    private static string FormatUtc(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string text) => text.Length <= 200 ? text.Trim() : text[..200].Trim() + "…";
}
