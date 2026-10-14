namespace VientoHud.Core.Tests.Support;

/// <summary>Reads recorded responses from the Fixtures folder copied next to the test assembly.</summary>
internal static class Fixture
{
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));
}
