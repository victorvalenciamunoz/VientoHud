using VientoHud.Core.Rain;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Routing;

/// <summary>What the planner found for a ride.</summary>
/// <param name="Departure">When the ride starts, in UTC.</param>
/// <param name="Wind">The wind at the start over the hours the ride lasts, and which way it says to head.</param>
/// <param name="Bearings">
/// The bearings loops were looked for in (degrees TOWARDS), in the order tried, including those of a wider fan when
/// none into the wind gave a valid loop.
/// </param>
/// <param name="Loops">
/// What the generator found for each bearing, as it ranks them: valid loops first (<see cref="LoopGenerator.GenerateManyAsync"/>).
/// </param>
/// <param name="Ranked">
/// The valid loops, each in its better way round, best first for the wind (DECISIONS 022); their
/// <see cref="RideOption.RouteIndex"/> is the position in <paramref name="Loops"/>. Empty when no loop was valid.
/// </param>
public sealed record RidePlan(
    DateTimeOffset Departure,
    StartDirectionAdvice Wind,
    IReadOnlyList<double> Bearings,
    IReadOnlyList<LoopResult> Loops,
    IReadOnlyList<RideOption> Ranked);

/// <summary>
/// Plans a ride for the wind (phase 3): looks at the wind for the hours of the ride, looks for loops where it helps
/// (<see cref="RideBearings"/>) and ranks them both ways round (<see cref="RideRanking"/>).
/// </summary>
public sealed class RidePlanner(LoopGenerator generator, IWindForecastProvider forecasts)
{
    /// <param name="start">Where the ride starts and ends.</param>
    /// <param name="targetKm">How long the loops should be.</param>
    /// <param name="departure">When the ride starts, in UTC.</param>
    /// <param name="averageSpeedKmh">Average speed of the whole ride.</param>
    /// <param name="count">How many loops to look for.</param>
    /// <param name="firstBearing">
    /// A bearing chosen by the rider: the loops are spread evenly from it and the wind does not choose them.
    /// </param>
    /// <param name="progress">Told about each bearing as soon as it is done.</param>
    /// <param name="rain">The rain to come; when given, it counts in the ranking (DECISIONS 035).</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <exception cref="WindForecastException">The forecast failed.</exception>
    public async Task<RidePlan> PlanAsync(
        Coordinate start,
        double targetKm,
        DateTimeOffset departure,
        double averageSpeedKmh = RideDistance.DefaultAverageSpeedKmh,
        int count = 3,
        double? firstBearing = null,
        IProgress<LoopResult>? progress = null,
        RainOutlook? rain = null,
        CancellationToken cancellationToken = default)
    {
        if (departure.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Departure must be in UTC.", nameof(departure));
        }

        // The wind at the start during the ride: from the hour it starts to the one it should end in.
        var hours = Math.Max(1, (int)Math.Ceiling(targetKm / averageSpeedKmh + departure.Minute / 60.0));
        var forecast = await forecasts.GetHourlyAsync(start, departure, hours, cancellationToken);
        var wind = StartDirection.Recommend(forecast.Hours);
        var bearings = (firstBearing is { } bearing
            ? LoopGenerator.SpreadBearings(count, bearing)
            : RideBearings.For(wind, count)).ToList();

        var loops = (await generator.GenerateManyAsync(start, bearings, targetKm, progress, cancellationToken)).ToList();
        // Nothing valid into the wind: widen the fan, one bearing at a time, until one is (the author's choice,
        // DECISIONS 023). Not when the rider chose the bearing, nor when the bearings already go all round.
        if (firstBearing is null && wind.StartBearingDegrees is { } intoWind)
        {
            foreach (var wider in RideBearings.Wider(intoWind, bearings))
            {
                if (loops.Any(l => l.Found) || loops.Any(l => l.EngineFailure is not null))
                {
                    break;
                }
                bearings.Add(wider);
                loops.AddRange(await generator.GenerateManyAsync(start, [wider], targetKm, progress, cancellationToken));
            }
            // Ranked as the generator ranks them: valid loops first, best first.
            loops = loops.OrderBy(l => l.Found ? 0 : 1).ThenBy(l => l.Best?.Badness ?? double.MaxValue).ToList();
        }

        var found = Enumerable.Range(0, loops.Count).Where(i => loops[i].Found).ToList();
        var ranked = found.Count == 0
            ? []
            : RideRanking.BestWayRound(await RideRanking.RankAsync(
                    forecasts, found.Select(i => loops[i].Best!.Route).ToList(), departure, averageSpeedKmh,
                    rain: rain, cancellationToken: cancellationToken))
                // Point back to the loop each option comes from.
                .Select(o => o with { RouteIndex = found[o.RouteIndex] })
                .ToList();

        return new RidePlan(departure, wind, bearings, loops, ranked);
    }
}

/// <summary>Departure times the rider gives as a time of day.</summary>
public static class DepartureTime
{
    /// <summary>
    /// The next time the clock in <paramref name="zone"/> shows <paramref name="localTime"/>, from
    /// <paramref name="now"/> on: today if it is still to come, otherwise tomorrow. In UTC.
    /// </summary>
    public static DateTimeOffset Next(TimeOnly localTime, DateTimeOffset now, TimeZoneInfo zone)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        for (var day = 0; day <= 2; day++)
        {
            var wall = localNow.Date.AddDays(day) + localTime.ToTimeSpan();
            // A time skipped by a clock change (spring forward) does not exist that day: take the same time an hour on.
            if (zone.IsInvalidTime(wall))
            {
                wall = wall.AddHours(1);
            }
            var candidate = new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
            if (candidate >= now.ToUniversalTime().AddMinutes(-1))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("Unreachable: some day within two has that time ahead.");
    }
}
