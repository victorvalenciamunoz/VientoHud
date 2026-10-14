using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Verification;

namespace VientoHud.Core.Tests.Wind;

public class ForecastJudgeTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(3);
    private static readonly Coordinate Here = new(40.4, -3.7);

    private static readonly WeatherStation Near = new("NEAR", "Near", new Coordinate(40.41, -3.7), 600, 1.1);
    private static readonly WeatherStation Far = new("FAR", "Far", new Coordinate(40.9, -3.7), 700, 55.6);

    private static JudgeRequest Request(params string[] models) =>
        new(Here, 5, 100, From, To, models, [ForecastLead.ShortRange, ForecastLead.DayAhead]);

    [Fact]
    public async Task Scores_every_model_and_lead_in_request_order()
    {
        var observations = new FakeObservations([Near, Far],
        [
            new WindObservation("NEAR", From, 10, null, 270),
            new WindObservation("NEAR", From.AddHours(1), 20, null, 270),
            new WindObservation("FAR", From, 30, null, 90),
        ]);
        // Every model forecasts 15 km/h from the west at both stations.
        var forecasts = new FakePastForecasts(covered: _ => true, speedKmh: 15, fromDegrees: 270);

        var result = await new ForecastJudge(observations, forecasts).RunAsync(Request("a", "b"));

        Assert.Equal([Near, Far], result.Stations);
        Assert.Equal(3, result.Observations);
        Assert.Equal(
            [("a", ForecastLead.ShortRange), ("a", ForecastLead.DayAhead), ("b", ForecastLead.ShortRange), ("b", ForecastLead.DayAhead)],
            result.Verdicts.Select(v => (v.Model, v.Lead)));

        var verdict = result.Verdicts[0];
        Assert.Equal(3, verdict.Overall.All.Hours);
        Assert.Equal(((15 - 10) + (15 - 20) + (15 - 30)) / 3.0, verdict.Overall.All.SpeedBiasKmh!.Value, precision: 9);
        Assert.Equal(2, verdict.ByStation["NEAR"].All.Hours);
        Assert.Equal(0, verdict.ByStation["NEAR"].All.DirectionMeanAbsoluteErrorDegrees);
        Assert.Equal(180, verdict.ByStation["FAR"].All.DirectionMeanAbsoluteErrorDegrees);
        Assert.Empty(verdict.NotCovered);
    }

    [Fact]
    public async Task Asks_for_forecasts_at_the_station_positions_for_the_period()
    {
        var forecasts = new FakePastForecasts(covered: _ => true, speedKmh: 15, fromDegrees: 270);

        await new ForecastJudge(new FakeObservations([Near, Far], []), forecasts).RunAsync(Request("a"));

        Assert.All(forecasts.Calls, call =>
        {
            Assert.Equal([Near.Position, Far.Position], call.Locations);
            Assert.Equal((From, To), (call.From, call.To));
        });
    }

    [Fact]
    public async Task Lists_stations_outside_a_model_and_leaves_them_out_of_its_score()
    {
        var observations = new FakeObservations([Near, Far],
        [
            new WindObservation("NEAR", From, 10, null, 270),
            new WindObservation("FAR", From, 30, null, 90),
        ]);
        var forecasts = new FakePastForecasts(covered: location => location == Near.Position, speedKmh: 10, fromDegrees: 270);

        var result = await new ForecastJudge(observations, forecasts).RunAsync(Request("a"));

        var verdict = result.Verdicts[0];
        Assert.Equal(["FAR"], verdict.NotCovered);
        Assert.Equal(["NEAR"], verdict.ByStation.Keys);
        Assert.Equal(1, verdict.Overall.All.Hours);
        Assert.Equal(0, verdict.Overall.All.SpeedBiasKmh);
    }

    [Fact]
    public async Task Without_stations_in_reach_returns_no_verdicts_and_asks_for_no_forecasts()
    {
        var forecasts = new FakePastForecasts(covered: _ => true, speedKmh: 10, fromDegrees: 0);

        var result = await new ForecastJudge(new FakeObservations([], []), forecasts).RunAsync(Request("a"));

        Assert.Empty(result.Stations);
        Assert.Empty(result.Verdicts);
        Assert.Empty(forecasts.Calls);
    }

    [Fact]
    public async Task Rejects_an_empty_period()
    {
        var judge = new ForecastJudge(
            new FakeObservations([], []), new FakePastForecasts(covered: _ => true, speedKmh: 10, fromDegrees: 0));

        await Assert.ThrowsAsync<ArgumentException>(() => judge.RunAsync(Request("a") with { To = From }));
    }

    private sealed class FakeObservations(IReadOnlyList<WeatherStation> stations, IReadOnlyList<WindObservation> observations)
        : IWindObservationSource
    {
        public Task<IReadOnlyList<WeatherStation>> FindNearestStationsAsync(
            Coordinate near, int count, double maxDistanceKm, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WeatherStation>>(stations.Take(count).ToList());

        public Task<IReadOnlyList<WindObservation>> GetObservationsAsync(
            IReadOnlyCollection<string> stationIds, DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WindObservation>>(
                observations.Where(o => stationIds.Contains(o.StationId) && o.Time >= from && o.Time < to).ToList());
    }

    /// <summary>Forecasts a constant wind every hour at the covered locations.</summary>
    private sealed class FakePastForecasts(Func<Coordinate, bool> covered, double speedKmh, double fromDegrees)
        : IPastWindForecastSource
    {
        public List<(IReadOnlyList<Coordinate> Locations, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = [];

        public Task<IReadOnlyList<PastWindForecast>> GetAsync(
            IReadOnlyList<Coordinate> locations, string model, ForecastLead lead, DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((locations, from, to));
            var hours = Enumerable.Range(0, (int)(to - from).TotalHours)
                .Select(h => new WindSample(from.AddHours(h), speedKmh, null, fromDegrees, model))
                .ToList();
            return Task.FromResult<IReadOnlyList<PastWindForecast>>(locations
                .Select(l => covered(l)
                    ? new PastWindForecast(l, model, lead, l, hours)
                    : new PastWindForecast(l, model, lead, null, []))
                .ToList());
        }
    }
}
