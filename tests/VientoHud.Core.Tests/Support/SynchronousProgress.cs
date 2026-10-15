namespace VientoHud.Core.Tests.Support;

/// <summary>
/// Reports on the caller's thread, at once. <see cref="Progress{T}"/> posts to the thread pool, so tests could not
/// check the order or the moment of each report.
/// </summary>
internal sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
