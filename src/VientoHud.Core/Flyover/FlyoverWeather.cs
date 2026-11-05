using System.Text;
using System.Text.Json;
using VientoHud.Core.Rain;
using VientoHud.Core.Routing;

namespace VientoHud.Core.Flyover;

/// <summary>What the rider meets at one point of the flight: the wind of that leg at the time they pass, and the alert there.</summary>
/// <param name="Wind">The leg's wind; null when the ride has no wind (not judged yet).</param>
/// <param name="Feel">How it feels there.</param>
/// <param name="Time">When the rider passes, in UTC.</param>
/// <param name="Alert">The GPX alert whose stretch covers the point (the area warning from where it begins); null if none.</param>
public sealed record FlyoverMoment(LegWind? Wind, WindFeel? Feel, DateTimeOffset? Time, RideAlert? Alert);

/// <summary>
/// A mark of the way round in the 3D view (phase 10, DECISIONS 065): a small arrow on the track pointing where the
/// rider goes (TO, degrees), colored by how the wind feels there; <paramref name="Rain"/> when rain is likely there as
/// they pass, for a blue drop at <paramref name="Drop"/>, beside it on the rider's right.
/// </summary>
public sealed record FlyoverMark(Coordinate Position, double BearingDegrees, WindFeel Feel, double AtKm, bool Rain, Coordinate Drop);

/// <summary>
/// The weather the 3D view puts over the route (phase 8, step 6): the wind arrows at the time the rider passes each
/// stretch, as on the map (phase 7e, the same reckoning of phase 3), and the GPX's alerts where they begin. Core
/// composes them; the view only draws them.
/// </summary>
/// <param name="Arrows">Wind arrows along the ride for the far view (LEJOS), coloured by how the wind feels.</param>
/// <param name="CloseArrows">The same for the close view (CERCA): one every km, short and next to the road.</param>
/// <param name="Alerts">The GPX's alerts, in riding order.</param>
/// <param name="Winds">The wind leg by leg, for <see cref="At"/>.</param>
public sealed record FlyoverWeather(
    IReadOnlyList<WindArrow> Arrows, IReadOnlyList<WindArrow> CloseArrows, IReadOnlyList<RideAlert> Alerts, IReadOnlyList<LegWind> Winds)
{
    /// <summary>
    /// The marks of the way round, as on the map (phase 10, DECISIONS 065): how the wind feels goes in them, the arrows
    /// only say how it blows, and the rain is a drop by them. They replaced the alerts' tinted stretches and circles.
    /// </summary>
    public IReadOnlyList<FlyoverMark> Marks { get; init; } = [];

    /// <summary>One mark every km, and a drop 60 m to its right where it rains.</summary>
    public const double MarkEveryKm = 1;

    public const double DropSideKm = 0.06;

    /// <summary>
    /// Arrows closer together and shorter than on the map: the camera flies low and sees a couple of km at a time
    /// (the map's 1.6 km arrows every 5 km would fill the screen or be out of it).
    /// </summary>
    public const double ArrowEveryKm = 2.5;

    public const double ArrowLengthKm = 0.8;

    /// <summary>
    /// In the close view the camera sees little more than a km either side: the far arrows were all out of the picture
    /// there (the author, 2026-10-26). One every km, short, 100 m from the road.
    /// </summary>
    public const double CloseArrowEveryKm = 1;

    public const double CloseArrowLengthKm = 0.25;

    public const double CloseArrowSideKm = 0.1;

    /// <summary>
    /// The view shows only the arrows of the stretch the camera is flying: from this far behind it to
    /// <see cref="CloseAheadKm"/> (CERCA) or <see cref="FarAheadKm"/> (LEJOS) ahead, in km along the ride. On an out
    /// and back both ways' arrows stand side by side, and the way out's red one read as the way back's (the author,
    /// 2026-10-26).
    /// </summary>
    public const double BehindKm = 0.5;

    public const double CloseAheadKm = 3;

    public const double FarAheadKm = 8;

    /// <summary>Nothing to show: the ride is not judged yet.</summary>
    public static readonly FlyoverWeather None = new([], [], [], []);

    /// <summary>
    /// The weather of a judged ride: its wind arrows and, when the rain is in, its alerts (the same as the GPX's).
    /// </summary>
    public static FlyoverWeather From(RideOption option, bool withAlerts)
    {
        ArgumentNullException.ThrowIfNull(option);
        var alerts = withAlerts ? RideAlerts.For(option) : [];
        var winds = option.Winds;
        return new FlyoverWeather(
            WindArrows.AlongRide(winds, ArrowEveryKm, ArrowLengthKm),
            WindArrows.AlongRide(winds, CloseArrowEveryKm, CloseArrowLengthKm, CloseArrowSideKm),
            alerts,
            winds)
        {
            Marks = winds.Count == 0 ? [] : RouteMarks.Along(option.Route, MarkEveryKm).Select(m =>
            {
                var wind = winds.FirstOrDefault(w => w.Leg.EndKm >= m.AtKm) ?? winds[^1];
                var feel = WindStretches.FeelOf(wind with { Leg = wind.Leg with { BearingDegrees = m.BearingDegrees } });
                var rain = withAlerts && option.Rain is { } r && r.Legs.Any(l => l.Rain.Likelihood == RainLikelihood.Likely
                    && l.Leg.EndKm > m.AtKm - MarkEveryKm / 2 && l.Leg.StartKm < m.AtKm + MarkEveryKm / 2);
                return new FlyoverMark(m.Position, m.BearingDegrees, feel, m.AtKm, rain,
                    m.Position.Destination((m.BearingDegrees + 90) % 360, DropSideKm));
            }).ToList(),
        };
    }

    /// <summary>What the rider meets at <paramref name="km"/> along the ride.</summary>
    public FlyoverMoment At(double km)
    {
        RideAlert? alert = Alerts.LastOrDefault(a => a.Kind == RideAlertKind.AreaShowers
            ? km >= a.StartKm
            : km >= a.StartKm && km <= a.EndKm);
        // A stretch's warning goes before the area one, which only says the rest of the ride is a guess.
        alert = Alerts.FirstOrDefault(a => a.Kind != RideAlertKind.AreaShowers && km >= a.StartKm && km <= a.EndKm) ?? alert;
        if (Winds.Count == 0)
        {
            return new FlyoverMoment(null, null, null, alert);
        }
        var wind = Winds.FirstOrDefault(w => w.Leg.EndKm >= km) ?? Winds[^1];
        var leg = wind.Leg;
        var share = leg.LengthKm > 0 ? Math.Clamp((km - leg.StartKm) / leg.LengthKm, 0, 1) : 0;
        var time = leg.StartTime + (leg.EndTime - leg.StartTime) * share;
        return new FlyoverMoment(wind, WindStretches.FeelOf(wind), time, alert);
    }

    /// <summary>
    /// As JSON for the view: <c>{"arrows" and "closeArrows": GeoJSON FeatureCollections of MultiLineStrings (shaft and
    /// head) with "feel" and "atKm", "marks": GeoJSON FeatureCollection of Points with "feel", "bearing" and "atKm",
    /// "drops": Points with "atKm"}</c>. Colours are the app's: the view picks the marks' by feel.
    /// </summary>
    public string ToJson()
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WritePropertyName("arrows");
            WriteArrows(json, Arrows);
            json.WritePropertyName("closeArrows");
            WriteArrows(json, CloseArrows);
            json.WritePropertyName("window");
            json.WriteStartObject();
            json.WriteNumber("behindKm", BehindKm);
            json.WriteNumber("closeAheadKm", CloseAheadKm);
            json.WriteNumber("farAheadKm", FarAheadKm);
            json.WriteEndObject();
            json.WritePropertyName("marks");
            WriteCollection(json, Marks, (j, mark) =>
            {
                j.WriteString("feel", mark.Feel.ToString());
                j.WriteNumber("bearing", Math.Round(mark.BearingDegrees, 1));
                j.WriteNumber("atKm", Math.Round(mark.AtKm, 2));
            }, (j, mark) =>
            {
                j.WriteString("type", "Point");
                j.WritePropertyName("coordinates");
                WritePosition(j, mark.Position);
            });
            json.WritePropertyName("drops");
            WriteCollection(json, Marks.Where(m => m.Rain), (j, mark) => j.WriteNumber("atKm", Math.Round(mark.AtKm, 2)), (j, mark) =>
            {
                j.WriteString("type", "Point");
                j.WritePropertyName("coordinates");
                WritePosition(j, mark.Drop);
            });
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteArrows(Utf8JsonWriter json, IEnumerable<WindArrow> arrows) =>
        WriteCollection(json, arrows, (j, arrow) =>
        {
            j.WriteString("feel", arrow.Feel?.ToString() ?? "Light");
            j.WriteNumber("atKm", Math.Round(arrow.AtKm, 2));
        }, (j, arrow) =>
        {
            j.WriteString("type", "MultiLineString");
            j.WritePropertyName("coordinates");
            j.WriteStartArray();
            WriteLine(j, [arrow.Tail, arrow.Tip]);
            WriteLine(j, [arrow.HeadLeft, arrow.Tip, arrow.HeadRight]);
            j.WriteEndArray();
        });

    private static void WriteCollection<T>(
        Utf8JsonWriter json, IEnumerable<T> items, Action<Utf8JsonWriter, T> properties, Action<Utf8JsonWriter, T> geometry)
    {
        json.WriteStartObject();
        json.WriteString("type", "FeatureCollection");
        json.WritePropertyName("features");
        json.WriteStartArray();
        foreach (var item in items)
        {
            json.WriteStartObject();
            json.WriteString("type", "Feature");
            json.WritePropertyName("properties");
            json.WriteStartObject();
            properties(json, item);
            json.WriteEndObject();
            json.WritePropertyName("geometry");
            json.WriteStartObject();
            geometry(json, item);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteLine(Utf8JsonWriter json, IEnumerable<Coordinate> points)
    {
        json.WriteStartArray();
        foreach (var p in points)
        {
            WritePosition(json, p);
        }
        json.WriteEndArray();
    }

    private static void WritePosition(Utf8JsonWriter json, Coordinate c)
    {
        json.WriteStartArray();
        json.WriteNumberValue(Math.Round(c.Longitude, 6));
        json.WriteNumberValue(Math.Round(c.Latitude, 6));
        json.WriteEndArray();
    }
}
