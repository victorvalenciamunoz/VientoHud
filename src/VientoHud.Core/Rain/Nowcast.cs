namespace VientoHud.Core.Rain;

/// <summary>How likely rain is, in words (thresholds chosen by the author, DECISIONS 030).</summary>
public enum RainLikelihood
{
    /// <summary>Probability below 20 %.</summary>
    No,

    /// <summary>From 20 % to 50 %.</summary>
    Possible,

    /// <summary>Above 50 %.</summary>
    Likely,
}

public static class RainLikelihoods
{
    public const double PossibleFrom = 0.2;
    public const double LikelyAbove = 0.5;

    public static RainLikelihood Classify(double probability)
    {
        if (!double.IsFinite(probability) || probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability, "Probability must be in [0, 1].");
        }
        return probability > LikelyAbove ? RainLikelihood.Likely
            : probability >= PossibleFrom ? RainLikelihood.Possible
            : RainLikelihood.No;
    }
}

/// <summary>Chance of rain at a point at one time.</summary>
/// <param name="Time">The time it is for, in UTC.</param>
/// <param name="Probability">
/// Share of the cells with rain around the place the rain comes from, in [0, 1]; null when the radar has no data
/// for most of that area.
/// </param>
public sealed record RainChance(DateTimeOffset Time, double? Probability)
{
    public RainLikelihood? Likelihood => Probability is { } p ? RainLikelihoods.Classify(p) : null;
}

/// <summary>Rain forecast at a point for the next minutes, by moving the latest radar frame.</summary>
/// <param name="IssuedAt">"Now" for the forecast, in UTC: the steps count from here, not from the frame.</param>
/// <param name="FrameTime">Time of the radar frame it moves, in UTC (a few minutes before <paramref name="IssuedAt"/>).</param>
/// <param name="Motion">The motion used; null when unknown, and then the rain is assumed to stay still.</param>
/// <param name="Steps">One per step from <paramref name="IssuedAt"/> (the first is "now") to the horizon.</param>
public sealed record PointNowcast(
    Coordinate Point,
    DateTimeOffset IssuedAt,
    DateTimeOffset FrameTime,
    RainMotion? Motion,
    IReadOnlyList<RainChance> Steps)
{
    /// <summary>First step when rain becomes likely, if it is not likely now.</summary>
    public DateTimeOffset? RainArrives =>
        Steps[0].Likelihood == RainLikelihood.Likely
            ? null
            : Steps.Skip(1).FirstOrDefault(s => s.Likelihood == RainLikelihood.Likely)?.Time;

    /// <summary>First step when rain is no longer expected (below 20 %), if it is likely now.</summary>
    public DateTimeOffset? RainStops =>
        Steps[0].Likelihood != RainLikelihood.Likely
            ? null
            : Steps.Skip(1).FirstOrDefault(s => s.Likelihood == RainLikelihood.No)?.Time;

    /// <summary>The step nearest to <paramref name="time"/>.</summary>
    public RainChance At(DateTimeOffset time) => Steps.MinBy(s => Math.Abs((s.Time - time).Ticks))!;
}

/// <summary>Settings for <see cref="Nowcast"/>.</summary>
public sealed record NowcastOptions
{
    /// <summary>How far ahead to forecast, from "now".</summary>
    public TimeSpan Horizon { get; init; } = TimeSpan.FromMinutes(90);

    public TimeSpan Step { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Radius of the area looked at around the place the rain comes from, at the time of the frame. It grows with
    /// <see cref="RadiusGrowthKmPerHour"/>, because rain grows, fades and the motion has errors.
    /// 4 km + 15 km/h (≈ 13, 20 and 28 km at +30, +60 and +90 min), chosen by the author from the phase 4 judge
    /// (DECISIONS 032): the percentages match what happens better than with smaller circles.
    /// </summary>
    public double RadiusKm { get; init; } = 4;

    public double RadiusGrowthKmPerHour { get; init; } = 15;

    /// <summary>Least rain rate that counts as rain (default: 12 dBZ, as in <see cref="RainNearby"/>).</summary>
    public double ThresholdMmPerHour { get; init; } = RainNearby.DefaultThresholdMmPerHour;
}

/// <summary>
/// Short-range rain forecast by moving the latest radar frame with the rain's motion (DECISIONS 030): the rain over
/// a point in Δt is the rain that is now Δt·velocity upwind of it. Rain is assumed not to grow or fade; the
/// uncertainty shows as a probability over an area that grows with Δt.
/// </summary>
public static class Nowcast
{
    /// <summary>
    /// Chance of rain at <paramref name="point"/> every <see cref="NowcastOptions.Step"/> from
    /// <paramref name="now"/> to the horizon. Without a motion the rain stays where it is.
    /// </summary>
    public static PointNowcast AtPoint(
        RainGrid latest, RainMotion? motion, Coordinate point, DateTimeOffset now, NowcastOptions? options = null)
    {
        options ??= new NowcastOptions();
        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Now must be in UTC.", nameof(now));
        }
        if (now < latest.Time)
        {
            throw new ArgumentException("Now cannot be before the frame.", nameof(now));
        }
        if (options.Step <= TimeSpan.Zero || options.Horizon < TimeSpan.Zero)
        {
            throw new ArgumentException("Step must be positive and the horizon not negative.", nameof(options));
        }

        var steps = new List<RainChance>();
        for (var lead = TimeSpan.Zero; lead <= options.Horizon; lead += options.Step)
        {
            var time = now + lead;
            steps.Add(new RainChance(time, ChanceAt(latest, motion, point, time, options)));
        }
        return new PointNowcast(point, now, latest.Time, motion, steps);
    }

    /// <summary>
    /// Chance of rain at <paramref name="point"/> at <paramref name="time"/> (UTC, not before the frame), in [0, 1];
    /// null when the radar has no data for most of the area it comes from. The same as one step of
    /// <see cref="AtPoint"/>, for places and times that change together (the legs of a route).
    /// </summary>
    public static double? ChanceAt(
        RainGrid latest, RainMotion? motion, Coordinate point, DateTimeOffset time, NowcastOptions? options = null)
    {
        options ??= new NowcastOptions();
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        if (time < latest.Time)
        {
            throw new ArgumentException("Time cannot be before the frame.", nameof(time));
        }
        var (x, y) = latest.Projection.Forward(point);
        var hours = (time - latest.Time).TotalHours;
        var (upwindX, upwindY) = Upwind(x, y, motion, hours);
        var radiusMeters = (options.RadiusKm + options.RadiusGrowthKmPerHour * hours) * 1000;
        return WetShare(latest, upwindX, upwindY, radiusMeters, options.ThresholdMmPerHour);
    }

    /// <summary>
    /// The frame moved to <paramref name="time"/> with the motion (nearest cell): what the radar would show then if the
    /// rain only moved. Cells whose rain comes from outside the frame have no data.
    /// </summary>
    public static RainGrid Advect(RainGrid latest, RainMotion motion, DateTimeOffset time)
    {
        if (time.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Time must be in UTC.", nameof(time));
        }
        var hours = (time - latest.Time).TotalHours;
        var shiftColumns = motion.EastKmh * hours * 1000 / latest.CellSizeMeters;
        var shiftRows = -motion.NorthKmh * hours * 1000 / latest.CellSizeMeters;
        var rates = new float[latest.Rows * latest.Columns];
        for (var row = 0; row < latest.Rows; row++)
        {
            for (var column = 0; column < latest.Columns; column++)
            {
                var fromRow = (int)Math.Round(row - shiftRows);
                var fromColumn = (int)Math.Round(column - shiftColumns);
                rates[row * latest.Columns + column] =
                    fromRow >= 0 && fromRow < latest.Rows && fromColumn >= 0 && fromColumn < latest.Columns
                        ? latest[fromRow, fromColumn]
                        : float.NaN;
            }
        }
        return new RainGrid(
            time, latest.Projection, latest.Left, latest.Top, latest.CellSizeMeters, latest.Rows, latest.Columns, rates);
    }

    // Where the rain that will be over (x, y) in `hours` is now: upwind along the motion.
    private static (double X, double Y) Upwind(double x, double y, RainMotion? motion, double hours) =>
        motion is null ? (x, y) : (x - motion.EastKmh * hours * 1000, y - motion.NorthKmh * hours * 1000);

    // Share of cells with rain among those with data whose centre is within the radius; null if fewer than half of
    // the cells there have data (or the area is outside the frame).
    private static double? WetShare(RainGrid grid, double x, double y, double radiusMeters, double threshold)
    {
        var cell = grid.CellSizeMeters;
        // At least the cell under the point, whatever the radius.
        var radius = Math.Max(radiusMeters, cell / 2);
        var firstColumn = (int)Math.Floor((x - radius - grid.Left) / cell);
        var lastColumn = (int)Math.Floor((x + radius - grid.Left) / cell);
        var firstRow = (int)Math.Floor((grid.Top - (y + radius)) / cell);
        var lastRow = (int)Math.Floor((grid.Top - (y - radius)) / cell);

        int inside = 0, withData = 0, wet = 0;
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var dx = grid.Left + (column + 0.5) * cell - x;
                var dy = grid.Top - (row + 0.5) * cell - y;
                if (dx * dx + dy * dy > radius * radius
                    && !(row == (int)Math.Floor((grid.Top - y) / cell) && column == (int)Math.Floor((x - grid.Left) / cell)))
                {
                    continue;
                }
                inside++;
                if (row < 0 || row >= grid.Rows || column < 0 || column >= grid.Columns)
                {
                    continue;
                }
                var rate = grid[row, column];
                if (float.IsNaN(rate))
                {
                    continue;
                }
                withData++;
                if (rate >= threshold)
                {
                    wet++;
                }
            }
        }
        return inside == 0 || withData * 2 < inside ? null : (double)wet / withData;
    }
}
