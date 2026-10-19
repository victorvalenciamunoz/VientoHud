using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Support;

/// <summary>A radar that is down: every call throws "Radar down".</summary>
internal sealed class FailingRadarSource : IRadarSource
{
    public TimeSpan FrameInterval => TimeSpan.FromMinutes(5);

    public string Attribution => "Fake radar";

    public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) =>
        throw new RadarException("Radar down");

    public Task<RainGrid?> GetFrameAsync(
        DateTimeOffset time, Coordinate center, double radiusKm, CancellationToken cancellationToken = default) =>
        throw new RadarException("Radar down");
}
