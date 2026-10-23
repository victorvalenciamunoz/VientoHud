using System.Diagnostics;
using VientoHud.App.Hud;
using VientoHud.App.Maps;
using VientoHud.Core.Flyover;

namespace VientoHud.App;

/// <summary>
/// The 3D view of a route (phase 8, step 3: the prototype). The page hands MapLibre the scene Core composed
/// (<see cref="FlyoverScene"/>) and the base map's tiles, and shows what the JavaScript reports (frames per second,
/// tiles) for the judge of the phase. No logic here: C# passes data in, JavaScript draws.
/// </summary>
public partial class FlyoverPage : ContentPage
{
    private readonly string _sceneJson;
    private readonly TileSources _tiles;
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private int _seconds;
    private int _fpsSum;
    private double _loadedAfter;

    public FlyoverPage(FlyoverScene scene, TileSources tiles)
    {
        InitializeComponent();
        _sceneJson = scene.ToJson();
        _tiles = tiles;
        Meter.Text = "Cargando el relieve…";
    }

    // The bridge raises this on its own thread; the WebView and the page only take calls on the main one.
    private void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        var message = e.Message ?? "";
        Dispatcher.Dispatch(async () => await HandleAsync(message));
    }

    private async Task HandleAsync(string message)
    {
        if (message == "ready")
        {
            await Web.InvokeJavaScriptAsync("showScene", HybridJsonContext.Default.String,
                [_sceneJson, _tiles.TileUrlTemplate, _tiles.Attribution, HudTheme.Guide.ToHex()],
                [HybridJsonContext.Default.String, HybridJsonContext.Default.String, HybridJsonContext.Default.String, HybridJsonContext.Default.String]);
        }
        else if (message.StartsWith("loaded", StringComparison.Ordinal))
        {
            _loadedAfter = _since.Elapsed.TotalSeconds;
            Meter.Text = $"Relieve cargado en {_loadedAfter:0.0} s · {message.Split('|').ElementAtOrDefault(1)}";
        }
        else if (message.StartsWith("snapshot|", StringComparison.Ordinal))
        {
            // What WebGL drew, as the JavaScript saw it: for the judge of the phase (docs/verificacion).
            var path = Path.Combine(FileSystem.CacheDirectory, "flyover-snapshot.png");
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(message["snapshot|".Length..]));
        }
        else if (message.StartsWith("meter|", StringComparison.Ordinal))
        {
            var parts = message.Split('|').Skip(1).Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            if (int.TryParse(parts.GetValueOrDefault("fps"), out var fps))
            {
                _seconds++;
                _fpsSum += fps;
                Meter.Text = $"{fps} fps (media {_fpsSum / _seconds}) · {parts.GetValueOrDefault("tiles")} teselas · cargado en {_loadedAfter:0.0} s";
            }
        }
        else if (message.StartsWith("error|", StringComparison.Ordinal))
        {
            Meter.Text = message["error|".Length..];
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
