using System.Text;
using System.Text.Json;
using VientoHud.Core.Routing;

namespace VientoHud.Core.Flyover;

/// <summary>Where the flyover camera looks at one point of the route.</summary>
/// <param name="AtKm">Distance along the route.</param>
/// <param name="Center">The point of the track the camera looks at.</param>
/// <param name="BearingDegrees">
/// Route bearing the camera faces: degrees TOWARDS where the rider goes, clockwise from north, smoothed so that bends
/// do not jerk the view.
/// </param>
/// <param name="ElevationMeters">Smoothed elevation of the profile there; null without elevations.</param>
/// <param name="GradePercent">Grade of the profile's stretch there, positive uphill; null without elevations.</param>
public readonly record struct CameraFrame(
    double AtKm,
    Coordinate Center,
    double BearingDegrees,
    double? ElevationMeters,
    double? GradePercent);

/// <summary>How far and how high the camera follows: a map zoom and a tilt (degrees from looking straight down).</summary>
public readonly record struct CameraView(double Zoom, double PitchDegrees)
{
    /// <summary>Close behind and low: the road and its ramps (CERCA).</summary>
    public static readonly CameraView Close = new(14.5, 72);

    /// <summary>Further and higher: the valley around (LEJOS).</summary>
    public static readonly CameraView Far = new(12.8, 58);
}

/// <summary>
/// The camera's path for the 3D flyover (phase 8, step 5): a frame every <see cref="DefaultStepKm"/> along the
/// route, facing ahead, and how long the whole flight takes. Core computes it; the view only plays it, interpolating
/// between frames.
/// </summary>
/// <param name="Frames">From the start of the route to its end, in riding order.</param>
/// <param name="Duration">How long playing the whole route takes.</param>
/// <param name="LengthKm">Length of the route.</param>
public sealed record FlyoverPath(IReadOnlyList<CameraFrame> Frames, TimeSpan Duration, double LengthKm)
{
    /// <summary>Distance between frames.</summary>
    public const double DefaultStepKm = 0.05;

    /// <summary>The camera faces the point this far ahead on the track.</summary>
    public const double DefaultLookAheadKm = 1;

    /// <summary>The bearing is averaged over this much of the route, so a hairpin turns the view over a few seconds.</summary>
    public const double DefaultSmoothingKm = 1.5;

    /// <summary>
    /// The camera turns at most this much per km flown: a turn back on itself (the far point of an out and back, a
    /// hairpin) takes 2 km instead of a jump (the smoothing alone flipped the view in one frame there).
    /// </summary>
    public const double MaxTurnDegreesPerKm = 90;

    /// <summary>Seconds of flight per km of route, between <see cref="MinDuration"/> and <see cref="MaxDuration"/>.</summary>
    public const double SecondsPerKm = 1.5;

    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(40);

    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(150);

    /// <summary>The path along <paramref name="route"/>; the profile adds the elevation and the grade of each frame.</summary>
    public static FlyoverPath Along(
        Route route,
        RouteProfile? profile,
        double stepKm = DefaultStepKm,
        double lookAheadKm = DefaultLookAheadKm,
        double smoothingKm = DefaultSmoothingKm)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!double.IsFinite(stepKm) || stepKm is < 0.01 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stepKm), stepKm, "The step must be 10 m to 1 km.");
        }
        if (!double.IsFinite(lookAheadKm) || lookAheadKm is < 0.05 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(lookAheadKm), lookAheadKm, "The look-ahead must be 50 m to 10 km.");
        }
        if (!double.IsFinite(smoothingKm) || smoothingKm is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingKm), smoothingKm, "The smoothing must be 0 to 10 km.");
        }

        var points = route.Points.Select(p => p.Position).ToArray();
        var alongKm = new double[points.Length];
        for (var i = 1; i < points.Length; i++)
        {
            alongKm[i] = alongKm[i - 1] + points[i - 1].DistanceKmTo(points[i]);
        }
        var lengthKm = alongKm[^1];
        if (lengthKm == 0)
        {
            throw new ArgumentException("The route has no length.", nameof(route));
        }

        var count = Math.Max(1, (int)Math.Round(lengthKm / stepKm));
        var step = lengthKm / count;
        var centers = Enumerable.Range(0, count + 1).Select(k => RouteLegs.PointAt(points, alongKm, Math.Min(k * step, lengthKm))).ToArray();

        // Facing the point lookAheadKm ahead (near the end, the last bearing that had somewhere to look at).
        var raw = new double?[centers.Length];
        for (var k = 0; k < centers.Length; k++)
        {
            var aheadKm = k * step + lookAheadKm;
            if (aheadKm <= lengthKm + 1e-9)
            {
                var ahead = RouteLegs.PointAt(points, alongKm, Math.Min(aheadKm, lengthKm));
                raw[k] = centers[k].DistanceKmTo(ahead) > 0.01 ? centers[k].BearingTo(ahead) : null;
            }
        }
        var bearings = Limit(Smooth(raw, (int)Math.Round(smoothingKm / 2 / step)), MaxTurnDegreesPerKm * step);

        var frames = new CameraFrame[centers.Length];
        for (var k = 0; k < centers.Length; k++)
        {
            var km = k * step;
            frames[k] = new CameraFrame(
                km,
                centers[k],
                bearings[k],
                profile?.ElevationAt(km),
                profile?.StretchAt(km).GradePercent);
        }
        var seconds = Math.Clamp(lengthKm * SecondsPerKm, MinDuration.TotalSeconds, MaxDuration.TotalSeconds);
        return new FlyoverPath(frames, TimeSpan.FromSeconds(seconds), lengthKm);
    }

    /// <summary>The frame nearest to a position of the flight given as a fraction, from 0 (start) to 1 (end).</summary>
    public CameraFrame FrameAt(double fraction) =>
        Frames[(int)Math.Round(Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1) * (Frames.Count - 1))];

    /// <summary>
    /// The path as JSON for the view: <c>{"frames": [[lon, lat, bearing], …], "durationMs": n, "lengthKm": n,
    /// "views": {"close": {"zoom", "pitch"}, "far": {…}}}</c>.
    /// </summary>
    public string ToJson()
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WritePropertyName("frames");
            json.WriteStartArray();
            foreach (var frame in Frames)
            {
                json.WriteStartArray();
                json.WriteNumberValue(Math.Round(frame.Center.Longitude, 6));
                json.WriteNumberValue(Math.Round(frame.Center.Latitude, 6));
                json.WriteNumberValue(Math.Round(frame.BearingDegrees, 1));
                json.WriteEndArray();
            }
            json.WriteEndArray();
            json.WriteNumber("durationMs", (long)Duration.TotalMilliseconds);
            json.WriteNumber("lengthKm", Math.Round(LengthKm, 3));
            json.WritePropertyName("views");
            json.WriteStartObject();
            WriteView(json, "close", CameraView.Close);
            WriteView(json, "far", CameraView.Far);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteView(Utf8JsonWriter json, string name, CameraView view)
    {
        json.WritePropertyName(name);
        json.WriteStartObject();
        json.WriteNumber("zoom", view.Zoom);
        json.WriteNumber("pitch", view.PitchDegrees);
        json.WriteEndObject();
    }

    // Follows the bearings turning no more than maxPerFrame degrees from one frame to the next, the short way round
    // (clockwise when it is exactly behind).
    private static double[] Limit(double[] bearings, double maxPerFrame)
    {
        var limited = new double[bearings.Length];
        var current = bearings[0];
        for (var k = 0; k < bearings.Length; k++)
        {
            var turn = (bearings[k] - current + 540) % 360 - 180;
            if (turn == -180)
            {
                turn = 180;
            }
            current = (current + Math.Clamp(turn, -maxPerFrame, maxPerFrame) + 360) % 360;
            limited[k] = current;
        }
        return limited;
    }

    // Circular moving average of the bearings, half points either side: the sum of their unit vectors. Where there is
    // no bearing (the end, a standstill) or the vectors cancel out (a turn back on itself), the previous one stays.
    private static double[] Smooth(double?[] bearings, int half)
    {
        var first = bearings.FirstOrDefault(b => b is not null) ?? 0;
        var smooth = new double[bearings.Length];
        var previous = first;
        for (var k = 0; k < bearings.Length; k++)
        {
            double east = 0, north = 0;
            for (var j = Math.Max(0, k - half); j <= Math.Min(bearings.Length - 1, k + half); j++)
            {
                if (bearings[j] is { } b)
                {
                    east += Math.Sin(b * Math.PI / 180);
                    north += Math.Cos(b * Math.PI / 180);
                }
            }
            if (Math.Sqrt(east * east + north * north) > 1e-6)
            {
                previous = (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
            }
            smooth[k] = previous;
        }
        return smooth;
    }
}
