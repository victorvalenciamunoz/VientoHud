using System.Text;
using System.Text.Json;
using VientoHud.Core.Routing;

namespace VientoHud.Core.Flyover;

/// <summary>A piece of the track to draw in one colour: the points of one grade band, in riding order.</summary>
/// <param name="Points">The track points of the piece, cut exactly at its ends.</param>
/// <param name="Band">Its grade band; null when the route has no elevations (then the piece is the whole track).</param>
/// <param name="StartKm">Where it starts, along the route.</param>
/// <param name="EndKm">Where it ends, along the route.</param>
public sealed record FlyoverPiece(IReadOnlyList<Coordinate> Points, GradeBand? Band, double StartKm, double EndKm)
{
    /// <summary>The colour of the band ("#rrggbb"), or null when there is none: the view uses its route colour.</summary>
    public string? Color => Band is { } band ? GradeBands.Color(band) : null;
}

/// <summary>
/// What the 3D view draws (phase 8, DECISIONS 055): the track in pieces coloured by grade, where it starts and the
/// box it fits in. Core composes it and writes it as JSON; the JavaScript in the app only draws it.
/// </summary>
/// <param name="Pieces">The track, in riding order, one piece per stretch of the profile.</param>
/// <param name="Start">Where the ride starts.</param>
/// <param name="South">Southern edge of the track (latitude).</param>
/// <param name="West">Western edge of the track (longitude).</param>
/// <param name="North">Northern edge of the track (latitude).</param>
/// <param name="East">Eastern edge of the track (longitude).</param>
/// <param name="LengthKm">Length of the route.</param>
public sealed record FlyoverScene(
    IReadOnlyList<FlyoverPiece> Pieces,
    Coordinate Start,
    double South,
    double West,
    double North,
    double East,
    double LengthKm)
{
    /// <summary>
    /// The scene of <paramref name="route"/>: one piece per stretch of <paramref name="profile"/> (DECISIONS 056:
    /// the same bands and colours as the profile view), or the whole track in one piece without a profile.
    /// </summary>
    public static FlyoverScene From(Route route, RouteProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(route);
        var points = route.Points.Select(p => p.Position).ToArray();
        var alongKm = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            alongKm[i] = alongKm[i - 1] + points[i - 1].DistanceKmTo(points[i]);
        }
        var lengthKm = alongKm[^1];
        var pieces = profile is null
            ? [new FlyoverPiece(points, null, 0, lengthKm)]
            : profile.Stretches.Select(s => new FlyoverPiece(Cut(points, alongKm, s.StartKm, Math.Min(s.EndKm, lengthKm)), s.Band, s.StartKm, s.EndKm)).ToList();
        return new FlyoverScene(
            pieces,
            points[0],
            points.Min(p => p.Latitude),
            points.Min(p => p.Longitude),
            points.Max(p => p.Latitude),
            points.Max(p => p.Longitude),
            lengthKm);
    }

    /// <summary>
    /// The scene as JSON for the view: <c>{"pieces": GeoJSON FeatureCollection of LineStrings with "color", "band",
    /// "startKm", "endKm"; "start": [lon, lat]; "bounds": [[west, south], [east, north]]; "lengthKm": n}</c>.
    /// Coordinates are [longitude, latitude], as GeoJSON wants them.
    /// </summary>
    public string ToJson()
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WritePropertyName("pieces");
            json.WriteStartObject();
            json.WriteString("type", "FeatureCollection");
            json.WritePropertyName("features");
            json.WriteStartArray();
            foreach (var piece in Pieces)
            {
                json.WriteStartObject();
                json.WriteString("type", "Feature");
                json.WritePropertyName("properties");
                json.WriteStartObject();
                if (piece.Color is { } color)
                {
                    json.WriteString("color", color);
                    json.WriteString("band", piece.Band.ToString());
                }
                json.WriteNumber("startKm", Math.Round(piece.StartKm, 3));
                json.WriteNumber("endKm", Math.Round(piece.EndKm, 3));
                json.WriteEndObject();
                json.WritePropertyName("geometry");
                json.WriteStartObject();
                json.WriteString("type", "LineString");
                json.WritePropertyName("coordinates");
                json.WriteStartArray();
                foreach (var p in piece.Points)
                {
                    WritePosition(json, p);
                }
                json.WriteEndArray();
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
            json.WritePropertyName("start");
            WritePosition(json, Start);
            json.WritePropertyName("bounds");
            json.WriteStartArray();
            WritePosition(json, new Coordinate(South, West));
            WritePosition(json, new Coordinate(North, East));
            json.WriteEndArray();
            json.WriteNumber("lengthKm", Math.Round(LengthKm, 3));
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WritePosition(Utf8JsonWriter json, Coordinate c)
    {
        json.WriteStartArray();
        json.WriteNumberValue(Math.Round(c.Longitude, 6));
        json.WriteNumberValue(Math.Round(c.Latitude, 6));
        json.WriteEndArray();
    }

    // The track between fromKm and toKm along it, with its two ends on the track exactly there.
    private static IReadOnlyList<Coordinate> Cut(Coordinate[] points, double[] alongKm, double fromKm, double toKm)
    {
        var cut = new List<Coordinate> { RouteLegs.PointAt(points, alongKm, fromKm) };
        for (var i = 0; i < points.Length; i++)
        {
            if (alongKm[i] > fromKm && alongKm[i] < toKm)
            {
                cut.Add(points[i]);
            }
        }
        cut.Add(RouteLegs.PointAt(points, alongKm, toKm));
        return cut;
    }
}
