using VientoHud.Core.Towns;

namespace VientoHud.Core.Tests.Support;

/// <summary>A built-up map with round towns (all built inside, nothing outside), or one that always fails.</summary>
internal sealed class FakeBuiltUpSource(IReadOnlyList<(Coordinate Centre, double RadiusKm)> towns, bool fail = false) : IBuiltUpSource
{
    public const double CellDegrees = 0.002;

    public List<(Coordinate Center, double RadiusKm)> Requests { get; } = [];

    public string Attribution => "fake towns";

    public Task<BuiltUpGrid> GetAroundAsync(Coordinate center, double radiusKm, CancellationToken cancellationToken = default)
    {
        Requests.Add((center, radiusKm));
        if (fail)
        {
            throw new BuiltUpException("no towns today");
        }
        return Task.FromResult(Grid(center, radiusKm, towns));
    }

    /// <summary>A grid of <see cref="CellDegrees"/> cells around <paramref name="center"/> with the round towns built.</summary>
    public static BuiltUpGrid Grid(Coordinate center, double radiusKm, IReadOnlyList<(Coordinate Centre, double RadiusKm)> towns)
    {
        var half = (int)Math.Ceiling(radiusKm / 111.0 / Math.Cos(center.Latitude * Math.PI / 180) / CellDegrees);
        var size = 2 * half;
        var north = center.Latitude + half * CellDegrees;
        var west = center.Longitude - half * CellDegrees;
        var shares = new float[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var cell = new Coordinate(north - (y + 0.5) * CellDegrees, west + (x + 0.5) * CellDegrees);
                shares[y * size + x] = towns.Any(t => t.Centre.DistanceKmTo(cell) <= t.RadiusKm) ? 1 : 0;
            }
        }
        return new BuiltUpGrid(north, west, CellDegrees, size, size, shares);
    }
}
