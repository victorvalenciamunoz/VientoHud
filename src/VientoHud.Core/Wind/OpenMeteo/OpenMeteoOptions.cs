namespace VientoHud.Core.Wind.OpenMeteo;

/// <summary>Settings for <see cref="OpenMeteoWindForecastProvider"/>.</summary>
public sealed record OpenMeteoOptions
{
    public Uri ForecastEndpoint { get; init; } = new("https://api.open-meteo.com/v1/forecast");

    /// <summary>
    /// Models in order of preference. For each hour the first model with data wins, so a short-range,
    /// high-resolution model can be listed before a long-range one that covers the rest, or the places outside
    /// its domain (see DECISIONS 005).
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = ["meteofrance_arome_france", "best_match"];
}
