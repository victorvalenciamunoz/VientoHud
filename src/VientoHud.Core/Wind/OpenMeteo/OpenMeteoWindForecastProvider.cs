using System.Globalization;

namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Hourly wind forecast from the Open-Meteo forecast API (https://open-meteo.com/en/docs).</summary>
/// <remarks>
/// Models are requested one per call, in order of preference, and only while some hour is still missing.
/// Asking for several models in one call is ambiguous: when a model does not cover the location Open-Meteo
/// drops it silently and returns unsuffixed variables, so the data can no longer be traced to a model.
/// Several locations go in one call per model. Open-Meteo rejects the whole call when the model misses any of them,
/// and then all of them move on to the next model: a route near the edge of a model's domain gets one model
/// throughout instead of a seam.
/// </remarks>
public sealed class OpenMeteoWindForecastProvider(HttpClient httpClient, OpenMeteoOptions? options = null)
    : IWindForecastProvider
{
    public const string Attribution = OpenMeteoApi.Attribution;

    private readonly OpenMeteoOptions _options = options ?? new OpenMeteoOptions();

    public async Task<WindForecast> GetHourlyAsync(
        Coordinate location,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default) =>
        (await GetHourlyAsync([location], from, hours, cancellationToken))[0];

    public async Task<IReadOnlyList<WindForecast>> GetHourlyAsync(
        IReadOnlyList<Coordinate> locations,
        DateTimeOffset from,
        int hours,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hours, 1);
        if (locations.Count == 0)
        {
            return [];
        }
        if (_options.Models.Count == 0)
        {
            throw new InvalidOperationException("At least one Open-Meteo model must be configured.");
        }

        var start = OpenMeteoApi.TruncateToHour(from);
        var samples = locations.Select(_ => new WindSample?[hours]).ToArray();
        var firstUsed = new (Coordinate GridPoint, double Elevation)?[locations.Count];

        foreach (var model in _options.Models)
        {
            var pending = Enumerable.Range(0, locations.Count).Where(i => samples[i].Any(s => s is null)).ToList();
            if (pending.Count == 0)
            {
                break;
            }

            var uri = OpenMeteoApi.BuildUri(
                _options.ForecastEndpoint, pending.Select(i => locations[i]).ToList(), model, start, start.AddHours(hours - 1));
            using var document = await OpenMeteoApi.GetAsync(httpClient, uri, model, cancellationToken);
            if (document is null)
            {
                continue;
            }

            var elements = OpenMeteoApi.Locations(document.RootElement);
            if (elements.Count != pending.Count)
            {
                throw new WindForecastException(
                    $"Open-Meteo returned {elements.Count} locations for a request of {pending.Count}.");
            }
            foreach (var (i, element) in pending.Zip(elements))
            {
                var available = OpenMeteoApi.HourlySamples(element, model);
                var filledAny = false;
                for (var h = 0; h < hours; h++)
                {
                    if (samples[i][h] is null && available.TryGetValue(start.AddHours(h), out var sample))
                    {
                        samples[i][h] = sample;
                        filledAny = true;
                    }
                }
                if (filledAny)
                {
                    firstUsed[i] ??= (OpenMeteoApi.GridPoint(element), OpenMeteoApi.Elevation(element));
                }
            }
        }

        for (var i = 0; i < locations.Count; i++)
        {
            var missing = Array.FindIndex(samples[i], s => s is null);
            if (missing >= 0)
            {
                var where = locations.Count == 1 ? "" : " at " + string.Create(CultureInfo.InvariantCulture, $"{locations[i].Latitude:0.####},{locations[i].Longitude:0.####}");
                throw new WindForecastException(
                    $"Open-Meteo has no wind data{where} for {start.AddHours(missing):yyyy-MM-dd HH:mm} UTC in any of the models " +
                    $"{string.Join(", ", _options.Models)}.");
            }
        }

        // Models use different grids; report the one that supplied the first hours.
        return locations
            .Select((location, i) => new WindForecast(
                location, firstUsed[i]!.Value.GridPoint, firstUsed[i]!.Value.Elevation, samples[i]!, Attribution))
            .ToList();
    }
}
