namespace VientoHud.Core.Wind;

/// <summary>Wind at one place and moment, worked out from the hourly samples around it.</summary>
/// <param name="SpeedKmh">Mean wind speed in km/h.</param>
/// <param name="GustKmh">Gust in km/h, null when no sample around has one.</param>
/// <param name="DirectionFromDegrees">
/// Meteorological convention: degrees the wind blows FROM, clockwise from north, in [0, 360).
/// Not to be confused with route bearings, which point TO.
/// </param>
/// <param name="Model">Weather model(s) the samples came from, joined with "+" when they differ.</param>
public sealed record LocalWind(double SpeedKmh, double? GustKmh, double DirectionFromDegrees, string Model);

/// <summary>Hourly wind at one place of a <see cref="WindField"/>.</summary>
/// <param name="Position">Where the samples apply.</param>
/// <param name="Hours">Consecutive hourly samples, in chronological order.</param>
public sealed record WindFieldPoint(Coordinate Position, IReadOnlyList<WindSample> Hours)
{
    public IReadOnlyList<WindSample> Hours { get; } = Hours is not { Count: > 0 }
        ? throw new ArgumentException("A point needs at least one hour.", nameof(Hours))
        : Hours.Select((s, k) => s.Time == Hours[0].Time.AddHours(k)).All(ok => ok)
            ? Hours
            : throw new ArgumentException("Hours must be consecutive and in order.", nameof(Hours));
}

/// <summary>
/// Hourly wind at a few places, e.g. every 5 km along a route, that gives the wind at any place and moment in between
/// (DECISIONS 020): it blends the two nearest places, by inverse squared distance, and the two hours around the moment,
/// linearly. Speeds are blended as speeds and directions as arrows, so two opposite winds do not cancel into a calm.
/// The gust is that of the hour the moment falls in: Open-Meteo gives the strongest of the hour before each time.
/// </summary>
public sealed class WindField
{
    private const double SamePlaceKm = 0.05;

    public WindField(IReadOnlyList<WindFieldPoint> points)
    {
        Points = points is { Count: > 0 } ? points : throw new ArgumentException("A wind field needs at least one point.", nameof(points));
    }

    public IReadOnlyList<WindFieldPoint> Points { get; }

    /// <summary>
    /// Forecast from <paramref name="provider"/> at <paramref name="positions"/>, in one request, for every hour from the
    /// one that contains <paramref name="from"/> to the first one at or after <paramref name="to"/>.
    /// </summary>
    /// <exception cref="WindForecastException">The provider failed or lacks some place or hour.</exception>
    public static async Task<WindField> ForecastAsync(
        IWindForecastProvider provider,
        IReadOnlyList<Coordinate> positions,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The end cannot be before the start.", nameof(to));
        }
        var firstHour = FloorToHour(from);
        var hours = (int)Math.Ceiling((to - firstHour).TotalHours) + 1;
        var forecasts = await provider.GetHourlyAsync(positions, firstHour, hours, cancellationToken);
        return new WindField(forecasts.Select(f => new WindFieldPoint(f.Requested, f.Hours)).ToList());
    }

    /// <summary>The wind at <paramref name="position"/> at <paramref name="time"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The time falls outside the hours of the nearest places.</exception>
    public LocalWind At(Coordinate position, DateTimeOffset time)
    {
        var nearest = Points
            .Select(p => (Point: p, Km: p.Position.DistanceKmTo(position)))
            .OrderBy(p => p.Km)
            .Take(2)
            .ToList();
        if (nearest[0].Km < SamePlaceKm)
        {
            nearest = [nearest[0]];
        }

        double weightSum = 0, speed = 0, east = 0, north = 0, gust = 0, gustWeight = 0;
        var models = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (point, km) in nearest)
        {
            var weight = nearest.Count == 1 ? 1 : 1 / (km * km);
            var wind = InTime(point, time);
            weightSum += weight;
            speed += weight * wind.SpeedKmh;
            east += weight * wind.East;
            north += weight * wind.North;
            if (wind.GustKmh is { } g)
            {
                gust += weight * g;
                gustWeight += weight;
            }
            models.UnionWith(wind.Models);
        }

        var direction = east == 0 && north == 0
            ? InTime(nearest[0].Point, time).DirectionFromDegrees
            : (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
        return new LocalWind(
            speed / weightSum,
            gustWeight > 0 ? gust / gustWeight : null,
            direction,
            string.Join('+', models));
    }

    // The wind at one place at a moment between two of its hours. East/North: unit arrow of where the wind comes FROM,
    // weighted by speed, so blending arrows gives the blended direction.
    private static (double SpeedKmh, double? GustKmh, double DirectionFromDegrees, double East, double North, IEnumerable<string> Models)
        InTime(WindFieldPoint point, DateTimeOffset time)
    {
        var hours = point.Hours;
        var first = hours[0].Time;
        var offset = (time - first).TotalHours;
        if (offset < 0 || offset > (hours[^1].Time - first).TotalHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time), time, $"No wind at {point.Position} for that time: hours from {first:u} to {hours[^1].Time:u}.");
        }

        var i = Math.Min((int)Math.Floor(offset), hours.Count - 1);
        var t = offset - i;
        var a = hours[i];
        var b = t > 0 ? hours[i + 1] : a;
        double Arrow(WindSample s, Func<double, double> f) => s.SpeedKmh * f(s.DirectionFromDegrees * Math.PI / 180);
        var east = (1 - t) * Arrow(a, Math.Sin) + t * Arrow(b, Math.Sin);
        var north = (1 - t) * Arrow(a, Math.Cos) + t * Arrow(b, Math.Cos);
        var direction = east == 0 && north == 0 ? a.DirectionFromDegrees : (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
        // The gust of hour h is the strongest from h - 1 to h, so a moment inside that span takes it.
        var gust = t > 0 ? b.GustKmh : a.GustKmh;
        return ((1 - t) * a.SpeedKmh + t * b.SpeedKmh, gust, direction, east, north, [a.Model, b.Model]);
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
