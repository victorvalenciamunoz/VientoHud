namespace VientoHud.Core.Towns;

/// <summary>A map of built-up ground (towns, villages, industrial estates).</summary>
public interface IBuiltUpSource
{
    /// <summary>Credit to show wherever the data is used.</summary>
    string Attribution { get; }

    /// <summary>The built-up share of every cell within a square of half-side <paramref name="radiusKm"/>.</summary>
    /// <exception cref="BuiltUpException">The map could not be downloaded or read.</exception>
    Task<BuiltUpGrid> GetAroundAsync(Coordinate center, double radiusKm, CancellationToken cancellationToken = default);
}

/// <summary>The built-up map could not be downloaded or read.</summary>
public sealed class BuiltUpException(string message, Exception? innerException = null) : Exception(message, innerException);
