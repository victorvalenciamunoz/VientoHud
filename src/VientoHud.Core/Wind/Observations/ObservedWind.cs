namespace VientoHud.Core.Wind.Observations;

/// <summary>Station observations turned into the hourly wind a <see cref="WindField"/> takes (judge of phase 3).</summary>
public static class ObservedWind
{
    /// <summary>
    /// How far from each hour an observation may be to stand for it. METAR comes at :00 and :30, plus specials; as in
    /// the judge of phase 1 (<see cref="Verification.VerificationOptions.MaxTimeOffset"/>).
    /// </summary>
    public static readonly TimeSpan MaxTimeOffset = TimeSpan.FromMinutes(15);

    /// <summary>
    /// One sample per hour from <paramref name="firstHour"/> for <paramref name="hours"/> hours, each the observation
    /// closest to it within <see cref="MaxTimeOffset"/>; null if some hour has none. A calm or variable observation
    /// (no direction) takes the direction of the closest observation in time that has one, or north if none has: its
    /// speed is low, so the direction hardly matters. A missing gust means none was significant (METAR), and stays null.
    /// </summary>
    public static IReadOnlyList<WindSample>? Hourly(
        string stationId,
        IEnumerable<WindObservation> observations,
        DateTimeOffset firstHour,
        int hours)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hours, 1);
        var ofStation = observations.Where(o => o.StationId == stationId).OrderBy(o => o.Time).ToList();
        var model = $"METAR {stationId}";
        var samples = new List<WindSample>(hours);
        for (var h = 0; h < hours; h++)
        {
            var time = firstHour.AddHours(h);
            // Closest in time; on a tie the earlier one.
            var closest = ofStation
                .Where(o => (o.Time - time).Duration() <= MaxTimeOffset)
                .OrderBy(o => (o.Time - time).Duration())
                .ThenBy(o => o.Time)
                .FirstOrDefault();
            if (closest is null)
            {
                return null;
            }
            var direction = closest.DirectionFromDegrees
                ?? ofStation
                    .Where(o => o.DirectionFromDegrees is not null)
                    .OrderBy(o => (o.Time - time).Duration())
                    .FirstOrDefault()?.DirectionFromDegrees
                ?? 0;
            samples.Add(new WindSample(time, closest.SpeedKmh, closest.GustKmh, direction, model));
        }
        return samples;
    }

    /// <summary>
    /// The wind field measured by <paramref name="stations"/> for <paramref name="hours"/> hours from
    /// <paramref name="firstHour"/>. Stations lacking some hour are left out; null if none is left.
    /// </summary>
    public static WindField? Field(
        IReadOnlyList<WeatherStation> stations,
        IReadOnlyList<WindObservation> observations,
        DateTimeOffset firstHour,
        int hours)
    {
        var points = stations
            .Select(s => (Station: s, Hours: Hourly(s.Id, observations, firstHour, hours)))
            .Where(s => s.Hours is not null)
            .Select(s => new WindFieldPoint(s.Station.Position, s.Hours!))
            .ToList();
        return points.Count == 0 ? null : new WindField(points);
    }
}
