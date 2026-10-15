using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace VientoHud.Core.Routing.BRouter;

/// <summary>Settings for <see cref="BRouterRouteEngine"/>.</summary>
public sealed record BRouterOptions
{
    /// <summary>BRouter server. The public brouter.de has no published usage policy: use it sparingly.</summary>
    public Uri BaseUri { get; init; } = new("https://brouter.de/");

    /// <summary>
    /// Profile to upload before routing. Null uses <see cref="BRouterRouteEngine.StrictRoadProfile"/>.
    /// Ignored when <see cref="ServerProfile"/> is set.
    /// </summary>
    public string? ProfileText { get; init; }

    /// <summary>Name of a profile already on the server (e.g. "fastbike"). When set, nothing is uploaded.</summary>
    public string? ServerProfile { get; init; }

    /// <summary>Minimum time between two requests to the server (uploads included).</summary>
    public TimeSpan MinRequestInterval { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Routes through waypoints with a BRouter server (https://github.com/abrensch/brouter, MIT) and OpenStreetMap data.
/// See DECISIONS 013.
/// </summary>
/// <remarks>
/// By default it uploads the strict road-bike profile (<c>POST /brouter/profile</c>) once and routes with the id the
/// server returns. The server's built-in round-trip mode fails with uploaded profiles, so loops are requested as
/// ordinary routes through waypoints that end at the start. Requests are serialised and spaced by
/// <see cref="BRouterOptions.MinRequestInterval"/>. Not thread-safe across instances sharing a server: use one per app.
/// </remarks>
public sealed class BRouterRouteEngine(HttpClient httpClient, BRouterOptions? options = null) : IRouteEngine
{
    public const string Attribution = "Routing: BRouter (brouter.de) · Map data © OpenStreetMap contributors (ODbL)";

    private const string ProfileResource = "VientoHud.Core.Routing.BRouter.road-strict.brf";

    private readonly BRouterOptions _options = options ?? new BRouterOptions();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly SemaphoreSlim _profileGate = new(1, 1);
    private long? _lastRequestTimestamp;
    private string? _profileId;

    /// <summary>
    /// Strict road-bike profile shipped with Core: paved roads only, no motorways, trunk roads only when needed
    /// (DECISIONS 016).
    /// </summary>
    public static string StrictRoadProfile { get; } = LoadStrictRoadProfile();

    public async Task<Route> RouteAsync(IReadOnlyList<Coordinate> waypoints, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count < 2)
        {
            throw new ArgumentException("A route needs at least two waypoints.", nameof(waypoints));
        }

        var profile = await ProfileIdAsync(staleId: null, cancellationToken);
        var (status, body) = await SendAsync(new HttpRequestMessage(HttpMethod.Get, RouteUri(waypoints, profile)), cancellationToken);

        // An uploaded profile the server no longer has (or never stored) gives HTTP 500 with an empty body.
        // Input errors give the same, but the waypoints are already validated here, so upload again once.
        if (status == HttpStatusCode.InternalServerError && string.IsNullOrWhiteSpace(body) && _options.ServerProfile is null)
        {
            profile = await ProfileIdAsync(staleId: profile, cancellationToken);
            (status, body) = await SendAsync(new HttpRequestMessage(HttpMethod.Get, RouteUri(waypoints, profile)), cancellationToken);
        }

        if (status == HttpStatusCode.BadRequest)
        {
            // e.g. "target island detected for section 1": a waypoint snapped to a stretch cut off from the rest.
            throw new RouteException(RouteFailure.Unreachable, $"BRouter found no route: {Truncate(body)}");
        }
        if (status != HttpStatusCode.OK)
        {
            throw new RouteException(RouteFailure.Service, $"BRouter returned HTTP {(int)status}: {Truncate(body)}");
        }

        try
        {
            return ParseGeoJson(body);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or IndexOutOfRangeException or ArgumentException)
        {
            throw new RouteException(RouteFailure.Service, "BRouter returned an unexpected response.", ex);
        }
    }

    private Uri RouteUri(IReadOnlyList<Coordinate> waypoints, string profile)
    {
        var lonLats = string.Join('|', waypoints.Select(w => $"{FormatDegrees(w.Longitude)},{FormatDegrees(w.Latitude)}"));
        var query = string.Join('&',
            $"lonlats={Uri.EscapeDataString(lonLats)}",
            $"profile={Uri.EscapeDataString(profile)}",
            "alternativeidx=0",
            "format=geojson");
        return new Uri(_options.BaseUri, "brouter?" + query);
    }

    /// <summary>
    /// The profile to route with. Uploads it the first time, and again when <paramref name="staleId"/> is the
    /// current id (another caller may already have replaced it).
    /// </summary>
    private async Task<string> ProfileIdAsync(string? staleId, CancellationToken cancellationToken)
    {
        if (_options.ServerProfile is { } serverProfile)
        {
            return serverProfile;
        }

        await _profileGate.WaitAsync(cancellationToken);
        try
        {
            if (_profileId is null || _profileId == staleId)
            {
                _profileId = await UploadProfileAsync(_options.ProfileText ?? StrictRoadProfile, cancellationToken);
            }
            return _profileId;
        }
        finally
        {
            _profileGate.Release();
        }
    }

    private async Task<string> UploadProfileAsync(string profileText, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUri, "brouter/profile"))
        {
            Content = new StringContent(profileText, Encoding.UTF8, "text/plain"),
        };
        var (status, body) = await SendAsync(request, cancellationToken);
        if (status != HttpStatusCode.OK)
        {
            throw new RouteException(RouteFailure.Service, $"BRouter profile upload returned HTTP {(int)status}: {Truncate(body)}");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            // A profile that does not compile still gets an id, plus an "error" (HTTP 200).
            if (root.TryGetProperty("error", out var error))
            {
                throw new RouteException(RouteFailure.Service, $"BRouter rejected the profile: {error.GetString()}");
            }
            return root.GetProperty("profileid").GetString()
                ?? throw new RouteException(RouteFailure.Service, "BRouter profile upload returned no id.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new RouteException(RouteFailure.Service, "BRouter profile upload returned an unexpected response.", ex);
        }
    }

    /// <summary>Sends one request at a time, spaced by the minimum interval. Disposes the request.</summary>
    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            await _requestGate.WaitAsync(cancellationToken);
            try
            {
                if (_lastRequestTimestamp is { } last)
                {
                    var wait = _options.MinRequestInterval - Stopwatch.GetElapsedTime(last);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken);
                    }
                }
                _lastRequestTimestamp = Stopwatch.GetTimestamp();

                using var response = await httpClient.SendAsync(request, cancellationToken);
                return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (HttpRequestException ex)
            {
                throw new RouteException(RouteFailure.Service, $"BRouter request failed: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RouteException(RouteFailure.Service, "BRouter request timed out.", ex);
            }
            finally
            {
                _requestGate.Release();
            }
        }
    }

    /// <summary>
    /// Reads BRouter's GeoJSON: the first feature is the track (coordinates as [lon, lat, elevation]); its
    /// "messages" table has one row per stretch, ending at the row's position, with its length in metres and way tags.
    /// </summary>
    private static Route ParseGeoJson(string body)
    {
        using var document = JsonDocument.Parse(body);
        var track = document.RootElement.GetProperty("features")[0];
        var properties = track.GetProperty("properties");

        var points = track.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
            .Select(c => new RoutePoint(
                new Coordinate(c[1].GetDouble(), c[0].GetDouble()),
                c.GetArrayLength() > 2 ? c[2].GetDouble() : null))
            .ToList();

        var messages = properties.GetProperty("messages");
        var header = messages[0].EnumerateArray().Select(h => h.GetString()).ToList();
        var distanceColumn = ColumnIndex(header, "Distance");
        var tagsColumn = ColumnIndex(header, "WayTags");

        var segments = new List<RouteSegment>();
        var startMeters = 0.0;
        foreach (var row in messages.EnumerateArray().Skip(1))
        {
            var meters = double.Parse(row[distanceColumn].GetString()!, CultureInfo.InvariantCulture);
            var tags = ParseTags(row[tagsColumn].GetString() ?? "");
            if (segments.Count > 0 && SameTags(segments[^1].WayTags, tags))
            {
                // BRouter also splits stretches at nodes and cost changes; merge those with identical tags.
                var previous = segments[^1];
                segments[^1] = new RouteSegment(previous.StartKm, previous.LengthKm + meters / 1000, previous.WayTags);
            }
            else
            {
                segments.Add(new RouteSegment(startMeters / 1000, meters / 1000, tags));
            }
            startMeters += meters;
        }

        return new Route(
            points,
            segments,
            DistanceKm: ParseNumber(properties, "track-length")!.Value / 1000,
            AscentMeters: ParseNumber(properties, "filtered ascend"),
            Attribution);
    }

    private static int ColumnIndex(List<string?> header, string name)
    {
        var index = header.IndexOf(name);
        return index >= 0 ? index : throw new KeyNotFoundException($"BRouter messages have no '{name}' column.");
    }

    // "highway=tertiary surface=asphalt". BRouter adds "reversedirection=yes" when the way is ridden against its
    // OSM drawing direction; it is not an OSM tag but is kept as reported.
    private static Dictionary<string, string> ParseTags(string wayTags) =>
        wayTags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.First()[1]);

    private static bool SameTags(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);

    private static double? ParseNumber(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value) && value.GetString() is { } text
            ? double.Parse(text, CultureInfo.InvariantCulture)
            : null;

    private static string FormatDegrees(double degrees) => degrees.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Truncate(string text) =>
        string.IsNullOrWhiteSpace(text) ? "(empty body)" : text.Length <= 300 ? text.Trim() : text[..300] + "…";

    private static string LoadStrictRoadProfile()
    {
        using var stream = typeof(BRouterRouteEngine).Assembly.GetManifestResourceStream(ProfileResource)
            ?? throw new InvalidOperationException($"Embedded resource {ProfileResource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
