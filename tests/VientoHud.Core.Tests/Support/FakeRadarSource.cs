using VientoHud.Core.Rain;

namespace VientoHud.Core.Tests.Support;

/// <summary>Serves the frames it is given, by time, and remembers the times asked for.</summary>
internal sealed class FakeRadarSource(TimeSpan interval, params RainGrid[] frames) : IRadarSource
{
    public TimeSpan FrameInterval => interval;

    public string Attribution => "Fake radar";

    public List<DateTimeOffset> Requested { get; } = [];

    public Task<DateTimeOffset> GetLatestFrameTimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(frames.Max(f => f.Time));

    public Task<RainGrid?> GetFrameAsync(
        DateTimeOffset time, Coordinate center, double radiusKm, CancellationToken cancellationToken = default)
    {
        Requested.Add(time);
        return Task.FromResult(frames.FirstOrDefault(f => f.Time == time));
    }
}
