namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Settings for <see cref="OpenMeteoPastWindForecastSource"/>.</summary>
public sealed record OpenMeteoArchiveOptions
{
    /// <summary>Historical Forecast API: first hours of each run stitched together (https://open-meteo.com/en/docs/historical-forecast-api).</summary>
    public Uri HistoricalForecastEndpoint { get; init; } = new("https://historical-forecast-api.open-meteo.com/v1/forecast");

    /// <summary>Previous Runs API: values at fixed lead offsets of whole days (https://open-meteo.com/en/docs/previous-runs-api).</summary>
    public Uri PreviousRunsEndpoint { get; init; } = new("https://previous-runs-api.open-meteo.com/v1/forecast");
}

/// <summary>Past wind forecasts from the Open-Meteo archives. Most models are archived from January 2024.</summary>
public sealed class OpenMeteoPastWindForecastSource(HttpClient httpClient, OpenMeteoArchiveOptions? options = null)
    : IPastWindForecastSource
{
    public const string Attribution = OpenMeteoApi.Attribution;

    private readonly OpenMeteoArchiveOptions _options = options ?? new OpenMeteoArchiveOptions();

    public async Task<IReadOnlyList<PastWindForecast>> GetAsync(
        IReadOnlyList<Coordinate> locations,
        string model,
        ForecastLead lead,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (locations.Count == 0)
        {
            return [];
        }

        var firstHour = CeilingToHour(from);
        var lastHour = CeilingToHour(to).AddHours(-1);
        if (lastHour < firstHour)
        {
            throw new ArgumentException("The range must contain at least one whole hour.", nameof(to));
        }

        var (endpoint, suffix) = lead switch
        {
            ForecastLead.ShortRange => (_options.HistoricalForecastEndpoint, ""),
            ForecastLead.DayAhead => (_options.PreviousRunsEndpoint, "_previous_day1"),
            _ => throw new ArgumentOutOfRangeException(nameof(lead), lead, null),
        };

        // All locations in one call; Open-Meteo rejects the whole call if the model misses any of them,
        // so then ask again location by location.
        var all = await FetchAsync(endpoint, suffix, locations, model, lead, firstHour, lastHour, cancellationToken);
        if (all is not null || locations.Count == 1)
        {
            return all ?? [NotCovered(locations[0], model, lead)];
        }

        var results = new List<PastWindForecast>(locations.Count);
        foreach (var location in locations)
        {
            var one = await FetchAsync(endpoint, suffix, [location], model, lead, firstHour, lastHour, cancellationToken);
            results.Add(one?[0] ?? NotCovered(location, model, lead));
        }
        return results;
    }

    private async Task<IReadOnlyList<PastWindForecast>?> FetchAsync(
        Uri endpoint,
        string suffix,
        IReadOnlyList<Coordinate> locations,
        string model,
        ForecastLead lead,
        DateTimeOffset firstHour,
        DateTimeOffset lastHour,
        CancellationToken cancellationToken)
    {
        var uri = OpenMeteoApi.BuildUri(endpoint, locations, model, firstHour, lastHour, suffix);
        using var document = await OpenMeteoApi.GetAsync(httpClient, uri, model, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var elements = OpenMeteoApi.Locations(document.RootElement);
        if (elements.Count != locations.Count)
        {
            throw new WindForecastException(
                $"Open-Meteo returned {elements.Count} locations for a request of {locations.Count}.");
        }

        return locations
            .Zip(elements, (location, element) => new PastWindForecast(
                location,
                model,
                lead,
                OpenMeteoApi.GridPoint(element),
                OpenMeteoApi.HourlySamples(element, model, suffix).Values.OrderBy(s => s.Time).ToList()))
            .ToList();
    }

    private static PastWindForecast NotCovered(Coordinate location, string model, ForecastLead lead) =>
        new(location, model, lead, null, []);

    private static DateTimeOffset CeilingToHour(DateTimeOffset time)
    {
        var floor = OpenMeteoApi.TruncateToHour(time);
        return floor < time ? floor.AddHours(1) : floor;
    }
}
