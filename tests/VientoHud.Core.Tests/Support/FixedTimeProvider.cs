namespace VientoHud.Core.Tests.Support;

/// <summary>A clock stopped at a given UTC instant.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
