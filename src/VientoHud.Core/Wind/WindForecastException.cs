namespace VientoHud.Core.Wind;

/// <summary>A wind forecast could not be obtained.</summary>
public sealed class WindForecastException(string message, Exception? innerException = null)
    : Exception(message, innerException);
