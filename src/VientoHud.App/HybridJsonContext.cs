using System.Text.Json.Serialization;

namespace VientoHud.App;

/// <summary>JSON serialization info for what C# hands the 3D view's JavaScript (HybridWebView needs it to stay trim-safe).</summary>
[JsonSerializable(typeof(string))]
internal partial class HybridJsonContext : JsonSerializerContext
{
}
