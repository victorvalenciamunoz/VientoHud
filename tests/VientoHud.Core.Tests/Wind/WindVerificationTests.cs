using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;
using VientoHud.Core.Wind.OpenMeteo;
using VientoHud.Core.Wind.Verification;

namespace VientoHud.Core.Tests.Wind;

public class WindVerificationTests
{
    private static readonly DateTimeOffset Ten = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static WindSample Forecast(DateTimeOffset time, double speed, double from) =>
        new(time, speed, speed + 10, from, "model");

    private static WindObservation Observed(string station, DateTimeOffset time, double speed, double? from) =>
        new(station, time, speed, null, from);

    private static MatchedHour Pair(double forecastSpeed, double forecastFrom, double observedSpeed, double? observedFrom) =>
        new("LEMD", Forecast(Ten, forecastSpeed, forecastFrom), Observed("LEMD", Ten, observedSpeed, observedFrom));

    [Fact]
    public void Matches_the_observation_on_the_hour_rather_than_the_half_hour()
    {
        var matches = WindVerification.Match(
            "LEMD",
            [Forecast(Ten, 10, 270)],
            [Observed("LEMD", Ten.AddMinutes(-30), 1, 0), Observed("LEMD", Ten, 2, 0), Observed("LEMD", Ten.AddMinutes(30), 3, 0)]);

        Assert.Equal(2, Assert.Single(matches).Observed.SpeedKmh);
    }

    [Fact]
    public void Matches_the_closest_observation_within_the_window()
    {
        var matches = WindVerification.Match(
            "LEMD",
            [Forecast(Ten, 10, 270)],
            [Observed("LEMD", Ten.AddMinutes(-5), 1, 0), Observed("LEMD", Ten.AddMinutes(12), 2, 0)]);

        Assert.Equal(Ten.AddMinutes(-5), Assert.Single(matches).Observed.Time);
    }

    [Fact]
    public void On_a_tie_prefers_the_earlier_observation()
    {
        var matches = WindVerification.Match(
            "LEMD",
            [Forecast(Ten, 10, 270)],
            [Observed("LEMD", Ten.AddMinutes(10), 2, 0), Observed("LEMD", Ten.AddMinutes(-10), 1, 0)]);

        Assert.Equal(Ten.AddMinutes(-10), Assert.Single(matches).Observed.Time);
    }

    [Fact]
    public void Leaves_out_hours_without_a_close_enough_observation_of_that_station()
    {
        var matches = WindVerification.Match(
            "LEMD",
            [Forecast(Ten, 10, 270), Forecast(Ten.AddHours(1), 10, 270)],
            [Observed("LEMD", Ten.AddMinutes(16), 1, 0), Observed("LEVS", Ten.AddHours(1), 2, 0)]);

        Assert.Empty(matches);
    }

    [Fact]
    public void Rejects_a_window_that_could_reuse_an_observation() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WindVerification.Match(
            "LEMD", [], [], new VerificationOptions { MaxTimeOffset = TimeSpan.FromMinutes(30) }));

    [Fact]
    public void Speed_error_is_forecast_minus_observed()
    {
        Assert.Equal(-4, Pair(10, 0, 14, 0).SpeedErrorKmh);
        Assert.Equal(3, Pair(10, 0, 7, 0).SpeedErrorKmh);
    }

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(350, 10, 20)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 90, 0)]
    [InlineData(359, 1, 2)]
    [InlineData(270, 45, 135)]
    public void Direction_error_is_the_smallest_angle(double forecastFrom, double observedFrom, double expected) =>
        Assert.Equal(expected, Pair(10, forecastFrom, 10, observedFrom).DirectionErrorDegrees!.Value, precision: 9);

    [Fact]
    public void Direction_error_is_null_without_an_observed_direction() =>
        Assert.Null(Pair(10, 270, 2, null).DirectionErrorDegrees);

    [Fact]
    public void Scores_speed_and_direction()
    {
        MatchedHour[] hours =
        [
            Pair(10, 270, 14, 280), // speed -4, direction 10
            Pair(12, 0, 10, 300),   // speed +2, direction 60 (miss)
            Pair(8, 90, 6, null),   // speed +2, no observed direction
            Pair(20, 180, 26, 170), // speed -6, direction 10
        ];

        var score = WindVerification.Score(hours);

        Assert.Equal(4, score.Hours);
        Assert.Equal(-1.5, score.SpeedBiasKmh!.Value, precision: 9);
        Assert.Equal(3.5, score.SpeedMeanAbsoluteErrorKmh!.Value, precision: 9);
        Assert.Equal(3, score.DirectionHours);
        Assert.Equal(80.0 / 3, score.DirectionMeanAbsoluteErrorDegrees!.Value, precision: 9);
        Assert.Equal(2.0 / 3, score.DirectionHitRate!.Value, precision: 9);
    }

    [Fact]
    public void A_direction_exactly_at_the_hit_angle_counts_as_a_hit()
    {
        var score = WindVerification.Score([Pair(10, 0, 10, 45), Pair(10, 0, 10, 46)]);

        Assert.Equal(0.5, score.DirectionHitRate);
    }

    [Fact]
    public void Scoring_nothing_gives_no_metrics()
    {
        var score = WindVerification.Score([]);

        Assert.Equal(new VerificationScore(0, null, null, 0, null, null), score);
    }

    [Fact]
    public void Report_splits_hours_by_observed_speed()
    {
        MatchedHour[] hours = [Pair(10, 0, 11.9, 0), Pair(10, 0, 12, 0), Pair(10, 0, 30, 0)];

        var report = WindVerification.Report(hours);

        Assert.Equal(3, report.All.Hours);
        Assert.Equal(1, report.Light.Hours);
        Assert.Equal(2, report.Windy.Hours);
        Assert.Equal((-2 + -20) / 2.0, report.Windy.SpeedBiasKmh!.Value, precision: 9);
    }

    // Real data: AROME short-range archive and METAR for Madrid-Barajas and León, 1-2 October 2026.
    // Not a judgement of AROME (two days are far too few); a check that pairing real data shows no gross error
    // such as a reversed direction convention or a time shift.
    [Fact]
    public async Task Real_data_pairs_every_hour_and_shows_no_gross_error()
    {
        var hours = await MatchRecordedFixturesAsync();

        Assert.Equal(96, hours.Count);
        Assert.Equal(48, hours.Count(h => h.StationId == "LEMD"));
        Assert.All(hours, h => Assert.Equal(TimeSpan.Zero, h.Observed.Time - h.Forecast.Time));

        var report = WindVerification.Report(hours);
        Assert.InRange(report.All.SpeedMeanAbsoluteErrorKmh!.Value, 0, 10);
        Assert.InRange(report.All.DirectionMeanAbsoluteErrorDegrees!.Value, 0, 60);

        // Flipping the forecast direction (FROM read as TO) must make the direction error clearly worse.
        var flipped = hours
            .Select(h => h with
            {
                Forecast = new WindSample(
                    h.Forecast.Time, h.Forecast.SpeedKmh, h.Forecast.GustKmh, h.Forecast.DirectionFromDegrees + 180, h.Forecast.Model),
            })
            .ToList();
        Assert.True(
            WindVerification.Score(flipped).DirectionMeanAbsoluteErrorDegrees > report.All.DirectionMeanAbsoluteErrorDegrees + 60);
    }

    private static async Task<List<MatchedHour>> MatchRecordedFixturesAsync()
    {
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(2);

        var observationSource = new IemWindObservationSource(new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK, Fixture.Read("Iem/asos-LEMD-LELN-2026-10-01-to-03.csv"))));
        var observations = await observationSource.GetObservationsAsync(["LEMD", "LELN"], from, to);

        var forecastSource = new OpenMeteoPastWindForecastSource(new HttpClient(new StubHttpMessageHandler(
            HttpStatusCode.OK, Fixture.Read("OpenMeteo/archive-short-arome-lemd-leln-2026-10-01.json"))));
        var forecasts = await forecastSource.GetAsync(
            [new Coordinate(40.46667, -3.55556), new Coordinate(42.58889, -5.64944)],
            "meteofrance_arome_france",
            ForecastLead.ShortRange,
            from,
            to);

        return
        [
            .. WindVerification.Match("LEMD", forecasts[0].Hours, observations),
            .. WindVerification.Match("LELN", forecasts[1].Hours, observations),
        ];
    }
}
