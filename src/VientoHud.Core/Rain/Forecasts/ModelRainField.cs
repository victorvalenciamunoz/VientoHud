namespace VientoHud.Core.Rain.Forecasts;

/// <summary>Settings for <see cref="ModelRainField.Probability"/>.</summary>
public sealed record ModelRainOptions
{
    /// <summary>Radius of the area looked at around the place. Provisional until the judge of step 5a-2.</summary>
    public double RadiusKm { get; init; } = 15;

    /// <summary>
    /// Time margin each way: intervals ending within this of the asked one count too, because the model often gets the
    /// pattern right but early or late. Provisional until the judge of step 5a-2.
    /// </summary>
    public TimeSpan TimeMargin { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Least rain rate that counts as rain (default: 12 dBZ, as with the radar).</summary>
    public double ThresholdMmPerHour { get; init; } = RainNearby.DefaultThresholdMmPerHour;
}

/// <summary>
/// A model's rain forecast on a grid of points around an area, interval by interval (DECISIONS 033). The chance of
/// rain at a place and time is the share, among the points within a radius and the intervals within a time margin,
/// of those with rain: the model is trusted for the pattern, not for the exact place and minute. Immutable.
/// </summary>
public sealed class ModelRainField
{
    private readonly IReadOnlyList<RainForecastSeries> _series;

    public ModelRainField(IReadOnlyList<RainForecastSeries> series)
    {
        if (series.Count == 0)
        {
            throw new ArgumentException("At least one point is needed.", nameof(series));
        }
        if (series.Select(s => s.Interval).Distinct().Count() > 1)
        {
            throw new ArgumentException("All points must have the same interval.", nameof(series));
        }
        _series = series;
        Interval = series[0].Interval;
        var ends = series.SelectMany(s => s.Samples).Select(s => s.End).ToList();
        LastTime = ends.Count == 0 ? null : ends.Max();
    }

    public IReadOnlyList<RainForecastSeries> Series => _series;

    public TimeSpan Interval { get; }

    /// <summary>End of the last interval with a value at any point: how far ahead the forecast goes. Null if empty.</summary>
    public DateTimeOffset? LastTime { get; }

    /// <summary>
    /// Points of a square grid every <paramref name="spacingKm"/> covering <paramref name="radiusKm"/> around
    /// <paramref name="center"/> (rows north-south, columns east-west), to ask the model for.
    /// </summary>
    public static IReadOnlyList<Coordinate> GridAround(Coordinate center, double radiusKm, double spacingKm)
    {
        if (!double.IsFinite(radiusKm) || radiusKm < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), radiusKm, "Radius must not be negative.");
        }
        if (!double.IsFinite(spacingKm) || spacingKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spacingKm), spacingKm, "Spacing must be positive.");
        }
        var steps = (int)Math.Ceiling(radiusKm / spacingKm);
        var points = new List<Coordinate>();
        for (var north = steps; north >= -steps; north--)
        {
            var row = center.Destination(north >= 0 ? 0 : 180, Math.Abs(north) * spacingKm);
            for (var east = -steps; east <= steps; east++)
            {
                points.Add(row.Destination(east >= 0 ? 90 : 270, Math.Abs(east) * spacingKm));
            }
        }
        return points;
    }

    /// <summary>
    /// Chance of rain at <paramref name="place"/> at <paramref name="time"/>, in [0, 1]; null when no point of the area
    /// has a value at those times (outside the grid or beyond the forecast).
    /// </summary>
    public double? Probability(Coordinate place, DateTimeOffset time, ModelRainOptions? options = null)
    {
        options ??= new ModelRainOptions();
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }

        // The interval containing the time, and those whose end is within the margin of its end.
        var ticks = Interval.Ticks;
        var end = new DateTimeOffset((time.UtcTicks + ticks - 1) / ticks * ticks, TimeSpan.Zero);
        var first = end - options.TimeMargin;
        var last = end + options.TimeMargin;

        int counted = 0, wet = 0;
        foreach (var series in _series)
        {
            if (series.Location.DistanceKmTo(place) > options.RadiusKm)
            {
                continue;
            }
            foreach (var sample in series.Samples)
            {
                if (sample.End < first || sample.End > last)
                {
                    continue;
                }
                counted++;
                if (sample.MmPerHour >= options.ThresholdMmPerHour)
                {
                    wet++;
                }
            }
        }
        return counted == 0 ? null : (double)wet / counted;
    }
}
