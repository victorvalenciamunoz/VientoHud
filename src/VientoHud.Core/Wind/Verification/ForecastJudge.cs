using VientoHud.Core.Wind.Observations;

namespace VientoHud.Core.Wind.Verification;

/// <summary>What to judge: which stations, period, models and leads.</summary>
/// <param name="StationCount">How many stations, nearest first.</param>
/// <param name="MaxDistanceKm">Ignore stations farther than this.</param>
/// <param name="From">Start of the period, inclusive.</param>
/// <param name="To">End of the period, exclusive.</param>
public sealed record JudgeRequest(
    Coordinate Near,
    int StationCount,
    double MaxDistanceKm,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<string> Models,
    IReadOnlyList<ForecastLead> Leads)
{
    public VerificationOptions Options { get; init; } = new();
}

/// <summary>Scores of one model at one lead.</summary>
/// <param name="Overall">All stations together.</param>
/// <param name="ByStation">Per station id, only for stations the model covers.</param>
/// <param name="NotCovered">Station ids outside the model's domain.</param>
public sealed record ModelVerdict(
    string Model,
    ForecastLead Lead,
    VerificationReport Overall,
    IReadOnlyDictionary<string, VerificationReport> ByStation,
    IReadOnlyList<string> NotCovered);

/// <param name="Stations">Stations used, nearest first. Empty when there is none within reach.</param>
/// <param name="Observations">Observations downloaded for those stations in the period.</param>
/// <param name="Verdicts">One per model and lead, in request order (models first, then leads).</param>
public sealed record JudgeResult(
    JudgeRequest Request,
    IReadOnlyList<WeatherStation> Stations,
    int Observations,
    IReadOnlyList<ModelVerdict> Verdicts);

/// <summary>
/// The judge of phase 1: how well each model forecast the wind that the nearest stations measured
/// (DECISIONS 009 to 011).
/// </summary>
public sealed class ForecastJudge(IWindObservationSource observations, IPastWindForecastSource forecasts)
{
    public async Task<JudgeResult> RunAsync(JudgeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.To <= request.From)
        {
            throw new ArgumentException("The period must end after it starts.", nameof(request));
        }

        var stations = await observations.FindNearestStationsAsync(
            request.Near, request.StationCount, request.MaxDistanceKm, cancellationToken);
        if (stations.Count == 0)
        {
            return new JudgeResult(request, stations, 0, []);
        }

        var observed = await observations.GetObservationsAsync(
            stations.Select(s => s.Id).ToList(), request.From, request.To, cancellationToken);
        var positions = stations.Select(s => s.Position).ToList();

        var verdicts = new List<ModelVerdict>();
        foreach (var model in request.Models)
        {
            foreach (var lead in request.Leads)
            {
                var past = await forecasts.GetAsync(positions, model, lead, request.From, request.To, cancellationToken);
                verdicts.Add(Judge(model, lead, stations, past, observed, request.Options));
            }
        }

        return new JudgeResult(request, stations, observed.Count, verdicts);
    }

    private static ModelVerdict Judge(
        string model,
        ForecastLead lead,
        IReadOnlyList<WeatherStation> stations,
        IReadOnlyList<PastWindForecast> past,
        IReadOnlyList<WindObservation> observed,
        VerificationOptions options)
    {
        var byStation = new Dictionary<string, VerificationReport>();
        var notCovered = new List<string>();
        var all = new List<MatchedHour>();

        // The past forecasts come back in the order of the requested positions, i.e. of the stations.
        foreach (var (station, forecast) in stations.Zip(past))
        {
            if (forecast.GridPoint is null)
            {
                notCovered.Add(station.Id);
                continue;
            }

            var matched = WindVerification.Match(station.Id, forecast.Hours, observed, options);
            byStation[station.Id] = WindVerification.Report(matched, options);
            all.AddRange(matched);
        }

        return new ModelVerdict(model, lead, WindVerification.Report(all, options), byStation, notCovered);
    }
}
