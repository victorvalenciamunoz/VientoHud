namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Hourly wind forecast from the Open-Meteo forecast API (https://open-meteo.com/en/docs).</summary>
/// <remarks>
/// Models are requested one per call, in order of preference, and only while some hour is still missing.
/// Asking for several models in one call is ambiguous: when a model does not cover the location Open-Meteo
/// drops it silently and returns unsuffixed variables, so the data can no longer be traced to a model.
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
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hours, 1);
        if (_options.Models.Count == 0)
        {
            throw new InvalidOperationException("At least one Open-Meteo model must be configured.");
        }

        var start = OpenMeteoApi.TruncateToHour(from);
        var samples = new WindSample?[hours];
        (Coordinate GridPoint, double Elevation)? firstUsed = null;

        foreach (var model in _options.Models)
        {
            if (samples.All(s => s is not null))
            {
                break;
            }

            var uri = OpenMeteoApi.BuildUri(_options.ForecastEndpoint, [location], model, start, start.AddHours(hours - 1));
            using var document = await OpenMeteoApi.GetAsync(httpClient, uri, model, cancellationToken);
            if (document is null)
            {
                continue;
            }

            var root = document.RootElement;
            var available = OpenMeteoApi.HourlySamples(root, model);
            var filledAny = false;
            for (var h = 0; h < hours; h++)
            {
                if (samples[h] is null && available.TryGetValue(start.AddHours(h), out var sample))
                {
                    samples[h] = sample;
                    filledAny = true;
                }
            }
            if (filledAny)
            {
                firstUsed ??= (OpenMeteoApi.GridPoint(root), OpenMeteoApi.Elevation(root));
            }
        }

        var missing = Array.FindIndex(samples, s => s is null);
        if (missing >= 0)
        {
            throw new WindForecastException(
                $"Open-Meteo has no wind data for {start.AddHours(missing):yyyy-MM-dd HH:mm} UTC in any of the models " +
                $"{string.Join(", ", _options.Models)}.");
        }

        // Models use different grids; report the one that supplied the first hours.
        return new WindForecast(location, firstUsed!.Value.GridPoint, firstUsed.Value.Elevation, samples!, Attribution);
    }
}
