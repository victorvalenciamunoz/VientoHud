using System.Reflection;

namespace VientoHud.App.Services;

/// <summary>Values baked into the app at build time from the developer's environment (never in the repo).</summary>
public static class AppSettings
{
    /// <summary>The Thunderforest key (DECISIONS 050), or null to use OpenStreetMap.</summary>
    public static string? ThunderforestKey => Metadata("ThunderforestKey");

    /// <summary>A BRouter for development, or null for brouter.de.</summary>
    public static Uri? BRouterUrl =>
        Metadata("BRouterUrl") is { } url && Uri.TryCreate(url.EndsWith('/') ? url : url + "/", UriKind.Absolute, out var uri) ? uri : null;

    private static string? Metadata(string key)
    {
        var value = typeof(AppSettings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
