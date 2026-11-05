using System.Globalization;
using System.Net;
using System.Text.Json;

namespace VientoHud.Core.Wind.Observations.Iem;

/// <summary>Settings for <see cref="IemWindObservationSource"/>.</summary>
public sealed record IemOptions
{
    public Uri BaseUri { get; init; } = new("https://mesonet.agron.iastate.edu/");
}

/// <summary>
/// METAR wind observations from the Iowa Environmental Mesonet archive (public domain, worldwide airports).
/// See https://mesonet.agron.iastate.edu/cgi-bin/request/asos.py?help and DECISIONS 009.
/// </summary>
/// <remarks>
/// IEM has no "stations near a point" query. Stations are grouped in networks (one per country, or per
/// state in the US), and each network has a bounding box, so only the networks whose box is close enough
/// to the point are downloaded.
/// </remarks>
public sealed class IemWindObservationSource(HttpClient httpClient, IemOptions? options = null) : IWindObservationSource
{
    public const string Attribution = "Observations: Iowa Environmental Mesonet, Iowa State University";

    private const double KmPerDegreeLatitude = 111.195;
    private const double KmhPerKnot = 1.852;

    private readonly IemOptions _options = options ?? new IemOptions();

    public async Task<IReadOnlyList<WeatherStation>> FindNearestStationsAsync(
        Coordinate near,
        int count,
        double maxDistanceKm,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDistanceKm);

        var networks = await GetJsonAsync("api/1/networks.json", ParseNearbyNetworks, cancellationToken);
        var candidates = new List<WeatherStation>();
        foreach (var network in networks.Where(n => n.Box.IsWithin(near, maxDistanceKm)))
        {
            var stations = await GetJsonAsync(
                $"api/1/network/{Uri.EscapeDataString(network.Id)}.json", ParseStations, cancellationToken);
            candidates.AddRange(stations.Select(s => s with { DistanceKm = near.DistanceKmTo(s.Position) }));
        }

        return candidates
            .Where(s => s.DistanceKm <= maxDistanceKm)
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.DistanceKm)
            .Take(count)
            .ToList();
    }

    public async Task<IReadOnlyList<WindObservation>> GetObservationsAsync(
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
                    "data=drct", "data=sknt", "data=gust",
                    $"sts={FormatUtc(from)}", $"ets={FormatUtc(to)}",
                    "tz=Etc/UTC", "format=onlycomma", "missing=M", "latlon=no",
                ]));

        var csv = await GetStringAsync($"cgi-bin/request/asos.py?{query}", cancellationToken);
        return ParseObservations(csv)
            .Where(o => o.Time >= from && o.Time < to)
            .OrderBy(o => o.StationId, StringComparer.Ordinal)
            .ThenBy(o => o.Time)
            .ToList();
    }

    private async Task<T> GetJsonAsync<T>(string relative, Func<JsonElement, T> parse, CancellationToken cancellationToken)
    {
        var body = await GetStringAsync(relative, cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            return parse(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new WindObservationException($"IEM returned an unexpected response for {relative}.", ex);
        }
    }

    private async Task<string> GetStringAsync(string relative, CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseUri, relative);
        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => " (rate limit: at most one request per second)",
                    HttpStatusCode.UnprocessableEntity => " (request too large)",
                    _ => "",
                };
                throw new WindObservationException(
                    $"IEM returned HTTP {(int)response.StatusCode}{hint} for {uri.AbsolutePath}: {Truncate(body)}");
            }
            return body;
        }
        catch (HttpRequestException ex)
        {
            throw new WindObservationException($"IEM request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WindObservationException("IEM request timed out.", ex);
        }
    }

    // IEM tables come as {"schema": ..., "data": [ {row}, ... ]}.
    private static List<Network> ParseNearbyNetworks(JsonElement root) =>
        root.GetProperty("data").EnumerateArray()
            .Select(row => (Id: row.GetProperty("id").GetString()!, Extent: row.GetProperty("extent")))
            // ASOS networks hold the airport METAR stations: "ES__ASOS" for Spain, "IA_ASOS" for Iowa...
            .Where(n => n.Id.EndsWith("_ASOS", StringComparison.Ordinal) && n.Extent.ValueKind == JsonValueKind.String)
            .Select(n => BoundingBox.FromEwkbHex(n.Extent.GetString()!) is { } box ? new Network(n.Id, box) : null)
            .OfType<Network>()
            .ToList();

    private static List<WeatherStation> ParseStations(JsonElement root) =>
        root.GetProperty("data").EnumerateArray()
            .Where(row => row.GetProperty("online").ValueKind == JsonValueKind.True
                          && row.GetProperty("latitude").ValueKind == JsonValueKind.Number
                          && row.GetProperty("longitude").ValueKind == JsonValueKind.Number)
            .Select(row => new WeatherStation(
                row.GetProperty("id").GetString()!,
                row.GetProperty("name").GetString() ?? "",
                new Coordinate(row.GetProperty("latitude").GetDouble(), row.GetProperty("longitude").GetDouble()),
                row.GetProperty("elevation").ValueKind == JsonValueKind.Number ? row.GetProperty("elevation").GetDouble() : null,
                DistanceKm: 0))
            .ToList();

    /// <summary>Parses asos.py "onlycomma" output: a header row, then one row per METAR.</summary>
    private static IEnumerable<WindObservation> ParseObservations(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            yield break;
        }

        var header = lines[0].Split(',');
        int Column(string name) => Array.IndexOf(header, name) is var i and >= 0
            ? i
            : throw new WindObservationException($"IEM observations have no '{name}' column. Header: {lines[0]}");
        var station = Column("station");
        var valid = Column("valid");
        var drct = Column("drct");
        var sknt = Column("sknt");
        var gust = Column("gust");

        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            var speedKnots = Number(fields[sknt]);
            if (speedKnots is null)
            {
                continue;
            }

            var time = DateTimeOffset.ParseExact(
                fields[valid], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            // Calm (00000KT) comes as direction 0, which would read as "from the north".
            var direction = speedKnots > 0 ? Number(fields[drct]) : null;

            yield return new WindObservation(
                fields[station],
                time.ToUniversalTime(),
                speedKnots.Value * KmhPerKnot,
                Number(fields[gust]) * KmhPerKnot,
                direction);
        }
    }

    private static double? Number(string field) =>
        double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string FormatUtc(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string text) => text.Length <= 200 ? text.Trim() : text[..200].Trim() + "…";

    private sealed record Network(string Id, BoundingBox Box);

    /// <summary>Longitude/latitude box of a network, read from the PostGIS EWKB hex that IEM publishes.</summary>
    internal sealed record BoundingBox(double MinLon, double MinLat, double MaxLon, double MaxLat)
    {
        /// <summary>True when the point is inside the box grown by <paramref name="marginKm"/>.</summary>
        public bool IsWithin(Coordinate point, double marginKm)
        {
            var marginLat = marginKm / KmPerDegreeLatitude;
            var cosLat = Math.Cos(point.Latitude * Math.PI / 180);
            var marginLon = cosLat > 1e-6 ? marginKm / (KmPerDegreeLatitude * cosLat) : 360;
            return point.Latitude >= MinLat - marginLat && point.Latitude <= MaxLat + marginLat
                && point.Longitude >= MinLon - marginLon && point.Longitude <= MaxLon + marginLon;
        }

        /// <summary>Box around every vertex of a Point or Polygon geometry. Null for other geometries or bad input.</summary>
        public static BoundingBox? FromEwkbHex(string hex)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                return null;
            }

            try
            {
                var reader = new EwkbReader(bytes);
                var type = reader.UInt32();
                const uint sridFlag = 0x20000000;
                if ((type & sridFlag) != 0)
                {
                    reader.UInt32();
                }

                const uint point = 1, polygon = 3;
                var points = new List<(double X, double Y)>();
                switch (type & 0xFF)
                {
                    case point:
                        points.Add(reader.Point());
                        break;
                    case polygon:
                        var rings = reader.UInt32();
                        for (var ring = 0; ring < rings; ring++)
                        {
                            var vertices = reader.UInt32();
                            for (var vertex = 0; vertex < vertices; vertex++)
                            {
                                points.Add(reader.Point());
                            }
                        }
                        break;
                }
                return points.Count == 0
                    ? null
                    : new BoundingBox(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private sealed class EwkbReader(byte[] bytes)
        {
            private readonly bool _littleEndian = bytes.Length > 0 && bytes[0] == 1;
            private int _position = 1;

            public uint UInt32() => _littleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(Take(4))
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(Take(4));

            public (double X, double Y) Point() => (Double(), Double());

            private double Double() => _littleEndian
                ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(Take(8))
                : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(Take(8));

            private ReadOnlySpan<byte> Take(int count)
            {
                if (_position + count > bytes.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "EWKB ended early.");
                }
                var span = bytes.AsSpan(_position, count);
                _position += count;
                return span;
            }
        }
    }
}
