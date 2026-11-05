namespace VientoHud.Core.Wind;

/// <summary>Hourly wind forecast for one location.</summary>
/// <param name="Requested">Location that was asked for.</param>
/// <param name="GridPoint">Model grid cell the provider actually used.</param>
/// <param name="ElevationMeters">Elevation of the grid point, in metres.</param>
/// <param name="Hours">One sample per consecutive hour, in chronological order.</param>
/// <param name="Attribution">Credit line the data licence requires to be shown with the data.</param>
public sealed record WindForecast(
    Coordinate Requested,
    Coordinate GridPoint,
    double ElevationMeters,
    IReadOnlyList<WindSample> Hours,
    string Attribution);
