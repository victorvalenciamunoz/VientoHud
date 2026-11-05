namespace VientoHud.Core.Tests.Support;

/// <summary>
/// Made-up places for tests that only need "somewhere": an arbitrary point in Europe (inside the radar and model
/// domains), unrelated to anyone's home or rides. Tests built on real recordings live outside the repo (see the
/// test project file).
/// </summary>
internal static class TestPlaces
{
    /// <summary>The start of synthetic rides and the centre of synthetic grids.</summary>
    public static readonly Coordinate Origin = new(45.0, 1.0);
}
