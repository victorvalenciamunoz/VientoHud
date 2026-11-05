using VientoHud.Core.Rain;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>
/// How one pass meets the wind at a place where the way out and the way back share the road: a small mark by
/// <paramref name="Road"/>, on the rider's right, pointing where they go (TO, degrees), colored by how the wind feels
/// then. The map sets it off the road by a few pixels, so the two passes never fall on each other at any zoom.
/// </summary>
public sealed record PassMark(Coordinate Road, double BearingDegrees, WindFeel Feel, double AtKm);

/// <summary>
/// Likely rain when passing there: a drop by a pass's mark at <paramref name="Road"/>, set off beyond it towards
/// <paramref name="SideDegrees"/> (the rider's right).
/// </summary>
public sealed record RainDrop(Coordinate Road, double SideDegrees);

/// <summary>
/// What the map draws of the wind and the rain along a ride (phase 10). The wind's arrows say how it blows (FROM, as
/// always), on the road: where both passes of an out and back share it, one arrow when the wind is alike at both times
/// and one on each pass's side when it changed (DECISIONS 064). How it feels goes in the marks of the way round
/// (<see cref="PassMark"/>), and a drop sits by the mark of each pass that meets likely rain.
/// </summary>
public sealed record RideMarks(IReadOnlyList<WindArrow> Arrows, IReadOnlyList<PassMark> Passes, IReadOnlyList<RainDrop> Drops)
{
    public static readonly RideMarks None = new([], [], []);
}

public static class RideMarking
{
    /// <summary>Two places closer than this, going opposite ways, are the same road ridden twice.</summary>
    public const double SameRoadKm = 0.6;

    /// <summary>Opposite ways: their bearings at least this far apart.</summary>
    public const double OppositeDegrees = 120;

    /// <summary>The wind is alike at both passes when it turned less than this and its speed changed less than <see cref="AlikeKmh"/>.</summary>
    public const double AlikeDegrees = 45;

    public const double AlikeKmh = 6;

    /// <summary>How far to the rider's right a pass's own arrow stands when the wind changed between passes.</summary>
    public const double PassArrowSideKm = 1.1;

    public static RideMarks Along(RideOption option, double everyKm = WindArrows.DefaultEveryKm, double lengthKm = WindArrows.DefaultLengthKm)
    {
        ArgumentNullException.ThrowIfNull(option);
        var winds = option.Winds;
        if (winds.Count == 0)
        {
            return RideMarks.None;
        }
        var track = new Track(option.Route);
        var total = Math.Min(winds[^1].Leg.EndKm, track.LengthKm);
        LegWind WindAt(double km) => winds.FirstOrDefault(w => w.Leg.EndKm >= km) ?? winds[^1];

        // Every few km along the ride, except where the road was already ridden the other way: there the way back is
        // looked at in the very places the way out was (the samples every 5 km do not fall on the same spots).
        var samples = new List<(double Km, int PairedWith)>();
        for (var km = everyKm / 2; km < total; km += everyKm)
        {
            if (!track.Before(km, out _))
            {
                samples.Add((km, -1));
            }
        }
        foreach (var i in Enumerable.Range(0, samples.Count).ToList())
        {
            if (track.After(samples[i].Km, total, out var back))
            {
                samples.Add((back, i));
                samples[i] = samples[i] with { PairedWith = samples.Count - 1 };
            }
        }

        var arrows = new List<WindArrow>();
        var passes = new List<PassMark>();
        var drops = new List<RainDrop>();
        // The marks of the way round, colored by how the wind feels on each pass (the map sets each off to its rider's
        // right, so the way out and the way back fall on opposite sides of a shared road).
        passes.AddRange(RouteMarks.Along(option.Route).Select(m =>
        {
            var wind = WindAt(m.AtKm);
            return new PassMark(m.Position, m.BearingDegrees, WindStretches.FeelOf(wind with { Leg = wind.Leg with { BearingDegrees = m.BearingDegrees } }), m.AtKm);
        }));
        for (var i = 0; i < samples.Count; i++)
        {
            var (km, pairedWith) = samples[i];
            var wind = WindAt(km);
            var road = track.PointAt(km);
            var bearing = track.BearingAt(km);
            // Rain: a drop by the nearest mark of this pass, beyond it, so the two never cover each other.
            if (RainNear(option.Rain, km - everyKm / 2, km + everyKm / 2) && passes.Count > 0)
            {
                var by = passes.MinBy(p => Math.Abs(p.AtKm - km))!;
                drops.Add(new RainDrop(by.Road, (by.BearingDegrees + 90) % 360));
            }
            if (pairedWith < 0)
            {
                // Ridden once: the arrow on the road.
                if (wind.Wind.SpeedKmh >= WindStretches.LightBelowKmh)
                {
                    arrows.Add(WindArrows.At(road, wind.Wind.DirectionFromDegrees, wind.Wind.SpeedKmh, lengthKm) with { AtKm = km });
                }
                continue;
            }
            var other = WindAt(samples[pairedWith].Km);
            if (Alike(wind.Wind, other.Wind))
            {
                // One arrow on the road for both passes, drawn once (by the way out).
                if (pairedWith > i && Math.Max(wind.Wind.SpeedKmh, other.Wind.SpeedKmh) >= WindStretches.LightBelowKmh)
                {
                    arrows.Add(WindArrows.At(road, MeanDirection(wind.Wind, other.Wind), (wind.Wind.SpeedKmh + other.Wind.SpeedKmh) / 2, lengthKm) with { AtKm = km });
                }
            }
            else if (wind.Wind.SpeedKmh >= WindStretches.LightBelowKmh)
            {
                // The wind changed between passes: each pass has its own arrow, on its side, where the change shows.
                var own = road.Destination((bearing + 90) % 360, PassArrowSideKm);
                arrows.Add(WindArrows.At(own, wind.Wind.DirectionFromDegrees, wind.Wind.SpeedKmh, lengthKm) with { AtKm = km });
            }
        }
        return new RideMarks(arrows, passes, drops);
    }

    /// <summary>The ride's track with its km, to find where it runs over itself the other way.</summary>
    private sealed class Track
    {
        private readonly Coordinate[] _points;
        private readonly double[] _alongKm;

        public Track(Route route)
        {
            _points = route.Points.Select(p => p.Position).ToArray();
            _alongKm = new double[_points.Length];
            for (var i = 1; i < _points.Length; i++)
            {
                _alongKm[i] = _alongKm[i - 1] + _points[i - 1].DistanceKmTo(_points[i]);
            }
        }

        public double LengthKm => _alongKm[^1];

        public Coordinate PointAt(double km) => RouteLegs.PointAt(_points, _alongKm, Math.Clamp(km, 0, LengthKm));

        /// <summary>Where the track goes at a km (TO, degrees), over the next and last 100 m.</summary>
        public double BearingAt(double km) => PointAt(km - 0.1).BearingTo(PointAt(km + 0.1));

        /// <summary>Whether the road at <paramref name="km"/> was ridden the other way earlier on.</summary>
        public bool Before(double km, out double earlierKm) => Nearest(km, 0, km - 2 * SameRoadKm, out earlierKm);

        /// <summary>Whether the road at <paramref name="km"/> is ridden again the other way later on, and where.</summary>
        public bool After(double km, double untilKm, out double laterKm) => Nearest(km, km + 2 * SameRoadKm, untilKm, out laterKm);

        /// <summary>
        /// The nearest place of the track between two km, going the other way, closer than <see cref="SameRoadKm"/>:
        /// on the lines between its points, which can be far apart on a straight road.
        /// </summary>
        private bool Nearest(double km, double fromKm, double toKm, out double foundKm)
        {
            var here = PointAt(km);
            var bearing = BearingAt(km);
            var kmPerLongitude = 111.32 * Math.Cos(here.Latitude * Math.PI / 180);
            (double X, double Y) Flat(Coordinate c) => ((c.Longitude - here.Longitude) * kmPerLongitude, (c.Latitude - here.Latitude) * 110.57);
            foundKm = double.NaN;
            var bestKm = SameRoadKm;
            for (var i = 0; i + 1 < _points.Length; i++)
            {
                if (_alongKm[i + 1] < fromKm || _alongKm[i] > toKm)
                {
                    continue;
                }
                var (ax, ay) = Flat(_points[i]);
                var (bx, by) = Flat(_points[i + 1]);
                var (dx, dy) = (bx - ax, by - ay);
                var squared = dx * dx + dy * dy;
                var t = squared > 0 ? Math.Clamp(-(ax * dx + ay * dy) / squared, 0, 1) : 0;
                var apart = Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2));
                var at = _alongKm[i] + t * (_alongKm[i + 1] - _alongKm[i]);
                if (apart < bestKm && at >= fromKm && at <= toKm
                    && Compass.AngleBetween(bearing, _points[i].BearingTo(_points[i + 1])) >= OppositeDegrees)
                {
                    bestKm = apart;
                    foundKm = at;
                }
            }
            return !double.IsNaN(foundKm);
        }
    }

    /// <summary>Likely rain anywhere between two km of the ride, when the rider passes there.</summary>
    private static bool RainNear(RideRain? rain, double fromKm, double toKm) =>
        rain is not null && rain.Legs.Any(l =>
            l.Rain.Likelihood == RainLikelihood.Likely && l.Leg.EndKm > fromKm && l.Leg.StartKm < toKm);

    private static bool Alike(LocalWind a, LocalWind b) =>
        Compass.AngleBetween(a.DirectionFromDegrees, b.DirectionFromDegrees) < AlikeDegrees
        && Math.Abs(a.SpeedKmh - b.SpeedKmh) < AlikeKmh;

    /// <summary>The mean of two directions FROM, the short way round (350° and 10° give 0°).</summary>
    private static double MeanDirection(LocalWind a, LocalWind b)
    {
        var x = Math.Cos(a.DirectionFromDegrees * Math.PI / 180) + Math.Cos(b.DirectionFromDegrees * Math.PI / 180);
        var y = Math.Sin(a.DirectionFromDegrees * Math.PI / 180) + Math.Sin(b.DirectionFromDegrees * Math.PI / 180);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }
}
