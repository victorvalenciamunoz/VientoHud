using System.Diagnostics;
using VientoHud.App.Hud;
using VientoHud.App.Maps;
using VientoHud.Core.Flyover;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;

namespace VientoHud.App;

/// <summary>
/// The 3D view of a route (phase 8). The page hands MapLibre the scene Core composed (<see cref="FlyoverScene"/>)
/// and the base map's tiles, serves every tile the view asks for from the <see cref="TileStore"/> (fetching what is
/// missing, so that what has been seen works offline), fetches the terrain corridor of the route ahead, and shows
/// what the JavaScript reports (frames per second, tiles) for the judge of the phase. No logic here: C# passes data
/// in, JavaScript draws.
/// </summary>
public partial class FlyoverPage : ContentPage
{
    private readonly string _sceneJson;
    private readonly FlyoverPath _path;
    private readonly FlyoverWeather _weather;
    private readonly string _pathJson;
    private bool _playing;
    private bool _settingSlider;
    private int _flightSeconds;
    private int _flightFpsSum;
    private readonly Route _route;
    private readonly TileSources _tiles;
    private readonly TileStore _store;
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private readonly CancellationTokenSource _closing = new();
    private int _seconds;
    private int _fpsSum;
    private double _loadedAfter;
    private string _corridor = "";

    public FlyoverPage(FlyoverScene scene, FlyoverPath path, FlyoverWeather weather, Route route, TileSources tiles, TileStore store)
    {
        InitializeComponent();
        _sceneJson = scene.ToJson();
        _path = path;
        _pathJson = path.ToJson();
        _weather = weather;
        ShowWhere(0);
        _route = route;
        _tiles = tiles;
        _store = store;
        Meter.Text = "Cargando el relieve…";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = PrefetchCorridorAsync();
    }

    protected override async void OnDisappearing()
    {
        _closing.Cancel();
        base.OnDisappearing();
        // The page is gone: stop the JavaScript (its meter kept talking every second to a handler that no longer had
        // a page, 2026-10-26) and let the WebView and its GPU memory go.
        try
        {
            await Web.EvaluateJavaScriptAsync("shutdown()");
        }
        catch (Exception)
        {
            // The WebView may already be gone: nothing left to stop.
        }
        Web.Handler?.DisconnectHandler();
    }

    /// <summary>The terrain of the whole route ahead of the camera, so that the flyover works offline afterwards.</summary>
    private async Task PrefetchCorridorAsync()
    {
        try
        {
            var corridor = TerrainTiles.Corridor(_route.Points.Select(p => p.Position).ToList());
            var (fetched, failed) = await Task.Run(() => _store.PrefetchAsync(TerrainTiles.Source, corridor, TerrainTiles.Url, null, _closing.Token), _closing.Token);
            var sizeMb = _store.SizeBytes(TerrainTiles.Source) / 1024.0 / 1024.0;
            _corridor = failed == 0
                ? $"relieve guardado ({corridor.Count} teselas, {sizeMb:0.0} MB)"
                : $"relieve: faltan {failed} de {corridor.Count} teselas";
            ShowMeter(null);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Every tile the view asks for comes through here: from the store, or fetched once and kept. Android lets us
    /// answer a request but not change it, so the whole answer is ours.
    /// </summary>
    private void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
    {
        // The view asks its own origin for "tiles/terrarium/{z}/{x}/{y}.png" and "tiles/base/…" (flyover.js): same
        // origin, so the browser's CORS rules do not get in the way of answers we make up.
        var path = e.Uri.AbsolutePath;
        if (path.StartsWith(TerrainPath, StringComparison.Ordinal) && TileCorridor.TryParsePath(path[TerrainPath.Length..], out var terrain))
        {
            e.Handled = true;
            e.SetResponse(200, "OK", "image/png", TileAsync(TerrainTiles.Source, terrain, TerrainTiles.Url(terrain)));
        }
        else if (path.StartsWith(BasePath, StringComparison.Ordinal) && TileCorridor.TryParsePath(path[BasePath.Length..], out var tile))
        {
            e.Handled = true;
            e.SetResponse(200, "OK", "image/png", TileAsync(_tiles.FlyoverSource, tile, new Uri(_tiles.FlyoverTileUrlTemplate.Replace("{z}/{x}/{y}", tile.Path))));
        }
    }

    private const string TerrainPath = "/tiles/terrarium/";
    private const string BasePath = "/tiles/base/";

    private async Task<Stream?> TileAsync(string source, TileAddress tile, Uri url)
    {
        var bytes = await _store.GetOrFetchAsync(source, tile, url, _closing.Token);
        // Nothing to give: an empty body, which the view takes as a missing tile.
        return new MemoryStream(bytes ?? []);
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
                [_sceneJson, _tiles.Attribution, HudTheme.Guide.ToHex(), _tiles.FlyoverTileSize.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                [HybridJsonContext.Default.String, HybridJsonContext.Default.String, HybridJsonContext.Default.String, HybridJsonContext.Default.String]);
            await Web.InvokeJavaScriptAsync("setPath", HybridJsonContext.Default.String, [_pathJson], [HybridJsonContext.Default.String]);
            // The colours of the map and the profile: wind by feel as the arrows, alerts by kind as the warnings.
            var palette = $"{{\"Headwind\":\"{HudTheme.Alert.ToHex()}\",\"Tailwind\":\"{HudTheme.Good.ToHex()}\",\"FromLeft\":\"{HudTheme.Marker.ToHex()}\",\"FromRight\":\"{HudTheme.Marker.ToHex()}\",\"Gusts\":\"{HudTheme.Marker.ToHex()}\",\"Rain\":\"{HudTheme.Alert.ToHex()}\",\"AreaShowers\":\"{HudTheme.TextMuted.ToHex()}\"}}";
            await Web.InvokeJavaScriptAsync("setWeather", HybridJsonContext.Default.String, [_weather.ToJson(), palette],
                [HybridJsonContext.Default.String, HybridJsonContext.Default.String]);
        }
        else if (message.StartsWith("loaded", StringComparison.Ordinal))
        {
            _loadedAfter = _since.Elapsed.TotalSeconds;
            ShowMeter(null);
        }
        else if (message.StartsWith("meter|", StringComparison.Ordinal))
        {
            var parts = message.Split('|').Skip(1).Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
            if (int.TryParse(parts.GetValueOrDefault("fps"), out var fps))
            {
                _seconds++;
                _fpsSum += fps;
                if (_playing)
                {
                    _flightSeconds++;
                    _flightFpsSum += fps;
                }
                ShowMeter($"{fps} fps (media {_fpsSum / _seconds}{(_flightSeconds > 0 ? $", en vuelo {_flightFpsSum / _flightSeconds}" : "")}) · {parts.GetValueOrDefault("tiles")} teselas");
            }
        }
        else if (message.StartsWith("pos|", StringComparison.Ordinal)
            && double.TryParse(message["pos|".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fraction))
        {
            ShowPosition(fraction);
        }
        else if (message.StartsWith("paused", StringComparison.Ordinal) || message == "ended")
        {
            SetPlaying(false);
        }
        else if (message.StartsWith("error|", StringComparison.Ordinal))
        {
            Meter.Text = message["error|".Length..];
        }
    }

    private string _lastFps = "";

    private void ShowMeter(string? fps)
    {
        _lastFps = fps ?? _lastFps;
        var loaded = _loadedAfter > 0 ? $"cargado en {_loadedAfter:0.0} s" : "cargando…";
        Meter.Text = string.Join(" · ", new[] { _lastFps, loaded, _corridor }.Where(s => s.Length > 0));
    }

    private async void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        await Web.EvaluateJavaScriptAsync(_playing ? "pause()" : "play()");
        SetPlaying(!_playing);
    }

    private async void OnViewClicked(object? sender, EventArgs e)
    {
        var far = ViewChip.Text == "LEJOS";
        await Web.EvaluateJavaScriptAsync(far ? "setView('far')" : "setView('close')");
        ViewChip.Text = far ? "CERCA" : "LEJOS";
    }

    /// <summary>Dragging the slider pauses the flight and takes the camera there.</summary>
    private async void OnScrubChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_settingSlider)
        {
            return;
        }
        if (_playing)
        {
            await Web.EvaluateJavaScriptAsync("pause()");
            SetPlaying(false);
        }
        await Web.EvaluateJavaScriptAsync($"seek({e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        ShowWhere(e.NewValue);
    }

    private void ShowPosition(double fraction)
    {
        _settingSlider = true;
        Scrub.Value = fraction;
        _settingSlider = false;
        ShowWhere(fraction);
    }

    /// <summary>Where the camera is (km, elevation, grade) and what the rider meets there, all worded by Core.</summary>
    private void ShowWhere(double fraction)
    {
        var frame = _path.FrameAt(fraction);
        Where.Text = RideText.FlyoverAt(frame);
        var weather = RideText.FlyoverWeatherAt(_weather.At(frame.AtKm), TimeZoneInfo.Local);
        WhereWeather.Text = weather;
        WhereWeather.IsVisible = weather.Length > 0;
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        PlayPause.Text = playing ? "PAUSA" : "PLAY";
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
