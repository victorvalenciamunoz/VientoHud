using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using VientoHud.App.Hud;
using VientoHud.App.Maps;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;
using VientoHud.Core.Wind;
using GeoCoordinate = VientoHud.Core.Coordinate;

namespace VientoHud.App;

/// <summary>
/// The one screen: where the rider is, which way to head (phase 7d) and the ride traced by touching the map (7e):
/// one point is an out and back, two or more a loop. Layout only: the position comes from the device and every
/// route, judgement and sentence from Core. Core's work runs on a background thread (<see cref="Task.Run(Func{Task})"/>):
/// reading radar frames and ranking a ride take seconds on a phone, and on the UI thread that froze the map until
/// Android killed the app (ANR, 2026-10-19).
/// </summary>
public partial class MapPage : ContentPage
{
    /// <summary>Until the rider can choose (phase 7g): a two-hour ride, for the fan.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(2);

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Local;

    private readonly TileSources _tiles;
    private readonly ILocationProvider _location;
    private readonly DirectionAdvisor _direction;
    private readonly TracedRideAdvisor _rides;
    private readonly MemoryLayer _fanLayer = new("Fan") { Style = FanStyle() };
    private readonly MemoryLayer _routeLayer = new("Route") { Style = RouteStyle() };
    private readonly MemoryLayer _viaLayer = new("Via") { Style = ViaStyle() };
    private readonly MemoryLayer _reachedLayer = new("Reached") { Style = ReachedStyle() };
    private readonly MemoryLayer _snapLayer = new("Snap") { Style = SnapStyle() };
    // The wind: the mean one at home, and along the ride one layer per feel (the color is the layer's).
    // Every arrow has a white halo underneath, so it stands out on the pale map whatever its color.
    private readonly MemoryLayer _homeWindHaloLayer = new("HomeWindHalo") { Style = ArrowStyle(HudTheme.Text, 9) };
    private readonly MemoryLayer _homeWindLayer = new("HomeWind") { Style = ArrowStyle(HudTheme.Background, 5) };
    private readonly MemoryLayer _windHaloLayer = new("WindHalo") { Style = ArrowStyle(HudTheme.Text, 7) };
    private readonly Dictionary<WindFeel, MemoryLayer> _windLayers = new()
    {
        [WindFeel.Headwind] = new MemoryLayer("WindHead") { Style = ArrowStyle(HudTheme.Alert, 3.5) },
        [WindFeel.Tailwind] = new MemoryLayer("WindTail") { Style = ArrowStyle(HudTheme.Good, 3.5) },
        [WindFeel.FromLeft] = new MemoryLayer("WindLeft") { Style = ArrowStyle(HudTheme.Marker, 3.5) },
        [WindFeel.FromRight] = new MemoryLayer("WindRight") { Style = ArrowStyle(HudTheme.Marker, 3.5) },
        [WindFeel.Light] = new MemoryLayer("WindLight") { Style = ArrowStyle(HudTheme.Background, 3) },
    };
    private readonly MemoryLayer _homeLayer = new("Home") { Style = HomeStyle() };
    private readonly List<GeoCoordinate> _via = [];
    private GeoCoordinate? _home;
    private bool _started;
    private CancellationTokenSource? _cancel;

    public MapPage(TileSources tiles, ILocationProvider location, DirectionAdvisor direction, TracedRideAdvisor rides)
    {
        InitializeComponent();
        _tiles = tiles;
        _location = location;
        _direction = direction;
        _rides = rides;
        MapName.Text = tiles.Name;
        Attribution.Text = tiles.Attribution;
        MapView.Map.Layers.Add(tiles.CreateLayer());
        MapView.Map.Layers.Add(_fanLayer);
        MapView.Map.Layers.Add(_routeLayer);
        MapView.Map.Layers.Add(_snapLayer);
        MapView.Map.Layers.Add(_homeWindHaloLayer);
        MapView.Map.Layers.Add(_homeWindLayer);
        MapView.Map.Layers.Add(_windHaloLayer);
        foreach (var layer in _windLayers.Values)
        {
            MapView.Map.Layers.Add(layer);
        }
        MapView.Map.Layers.Add(_viaLayer);
        MapView.Map.Layers.Add(_reachedLayer);
        MapView.Map.Layers.Add(_homeLayer);
        MapView.Map.Tapped += OnMapTapped;
        // Until the device answers: León, where the author rides. The navigator keeps the call until the map has its size.
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(new GeoCoordinate(42.5987, -5.5671)), ResolutionAt(12));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_started)
        {
            _started = true;
            _ = AdviseDirectionAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancel?.Cancel();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_via.Count > 0)
        {
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>A tap on the map adds a point and asks for the ride through all of them.</summary>
    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        if (e.GestureType != GestureType.SingleTap || _home is null)
        {
            return;
        }
        var (longitude, latitude) = SphericalMercator.ToLonLat(e.WorldPosition.X, e.WorldPosition.Y);
        _via.Add(new GeoCoordinate(latitude, longitude));
        ShowVia();
        e.Handled = true;
        _ = AdviseRideAsync();
    }

    private async void OnRemoveLastClicked(object? sender, EventArgs e)
    {
        if (_via.Count == 0)
        {
            return;
        }
        _via.RemoveAt(_via.Count - 1);
        ShowVia();
        // The old route goes at once; the one through the points left comes when the engine answers.
        ShowRoute(null);
        if (_via.Count > 0)
        {
            await AdviseRideAsync();
        }
        else
        {
            await AdviseDirectionAsync();
        }
    }

    /// <summary>The position, then the wind and where to head; what failed stays on screen with a way to try again.</summary>
    private async Task AdviseDirectionAsync()
    {
        var cancellationToken = Restart();
        try
        {
            if (_home is null)
            {
                Status.Text = "Buscando tu posición…";
                var fix = await _location.GetCurrentAsync(cancellationToken);
                _home = fix.Position;
                ShowHome(fix.Position);
            }
            Status.Text = "Pidiendo el viento…";
            var targetKm = RideDistance.FromDuration(DefaultDuration);
            var advice = await Task.Run(() => _direction.AdviseAsync(_home.Value, targetKm, DateTimeOffset.UtcNow, cancellationToken: cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Show(RideText.DirectionHeadline(advice.Wind), RideText.Wind(advice.Wind),
                $"Salida de {DefaultDuration.TotalHours:0} h, viento de las próximas {advice.Hours} h.");
            ShowFan(advice.Fan);
            ShowHomeWind(WindArrows.AtHome(advice.Wind, _home.Value));
            ShowRideWind([]);
        }
        catch (OperationCanceledException)
        {
        }
        catch (LocationUnavailableException ex)
        {
            Fail(ex.Message);
        }
        catch (WindForecastException ex)
        {
            Fail($"Sin previsión de viento: {ex.Message}");
        }
    }

    /// <summary>The route through the points (drawn as soon as it comes), then the wind, the rain and when to leave.</summary>
    private async Task AdviseRideAsync()
    {
        var cancellationToken = Restart();
        // The route's own line stays on screen even when the weather after it fails.
        var summary = "";
        try
        {
            var request = new TracedRequest(_home!.Value, _via.ToList(), DateTimeOffset.UtcNow);
            Status.Text = request.Shape == RouteShape.OutAndBack ? "Buscando la ida y vuelta…" : "Buscando la vuelta…";
            var traced = await Task.Run(() => _rides.RouteAsync(request, cancellationToken), cancellationToken);
            // A point added or taken away meanwhile: this answer is stale, the next job draws its own.
            cancellationToken.ThrowIfCancellationRequested();
            ShowRoute(traced.Route);
            ShowReached(traced);
            summary = RideText.TracedSummary(traced) + ". ";
            Status.Text = $"{summary}Pidiendo el viento…";

            // The wind first (one request): the arrows, the way round and most of the panel; then the rain.
            var windOnly = await Task.Run(() => _rides.JudgeWindAsync(traced, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowRide(windOnly, "Pidiendo la lluvia…");

            var ride = await Task.Run(() => _rides.JudgeRainAsync(windOnly, DateTimeOffset.UtcNow, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ShowRide(ride, null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (RouteException ex) when (ex.Failure == RouteFailure.Unreachable)
        {
            Fail("BRouter no pudo unir los puntos: mueve el último a una carretera (QUITAR PUNTO y toca otra vez).");
        }
        catch (RouteException ex)
        {
            Fail($"BRouter no responde: {ex.Message}");
        }
        catch (WindForecastException ex)
        {
            Fail($"{summary}Sin previsión de viento: {ex.Message}");
        }
    }

    /// <summary>The ride as judged so far: the route in its better way round, the wind along it and the panel.</summary>
    /// <param name="doing">What is still on its way, for the status; null when nothing is.</param>
    private void ShowRide(TracedRide ride, string? doing)
    {
        var option = ride.Ride;
        ShowRoute(option.Route);
        ShowRideWind(WindArrows.AlongRide(option.Winds));
        var lines = new List<string> { $"{SpanishText.Capitalized(RideText.Turn(option, ride.Shape))}: {RideText.Advantage(option.Score)}" };
        lines.AddRange(RideText.Lines(option, ride.RainFailure, Zone, ride.RainPending));
        Show(RideText.Recommendation(ride.Departure, fromNow: true, Zone), string.Join("\n", lines),
            $"{RideText.TracedSummary(ride)}. {RideText.Arrival(option, Zone)}{(doing is null ? "" : $" {doing}")}");
    }

    /// <summary>Cancels what was running and clears the panel for the next job.</summary>
    private CancellationToken Restart()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        Retry.IsVisible = false;
        Headline.IsVisible = Detail.IsVisible = false;
        return _cancel.Token;
    }

    private void Show(string headline, string detail, string status)
    {
        Headline.Text = headline;
        Detail.Text = detail;
        Status.Text = status;
        Headline.IsVisible = Detail.IsVisible = true;
    }

    private void Fail(string why)
    {
        Status.Text = why;
        Retry.IsVisible = true;
    }

    private void ShowHome(GeoCoordinate position)
    {
        _homeLayer.Features = [new PointFeature(ToMercator(position))];
        _homeLayer.DataHasChanged();
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(position), ResolutionAt(12));
    }

    private void ShowFan(IReadOnlyList<GeoCoordinate>? ring)
    {
        _fanLayer.Features = ring is null
            ? []
            : [new GeometryFeature(new Polygon(new LinearRing(ring.Select(p => ToNts(p)).ToArray())))];
        _fanLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    private void ShowVia()
    {
        _viaLayer.Features = _via.Select(p => new PointFeature(ToMercator(p))).ToList();
        _viaLayer.DataHasChanged();
        RemoveLast.IsVisible = _via.Count > 0;
        Hint.Text = _via.Count switch
        {
            0 => "Toca el mapa: un punto, ida y vuelta; dos o más, vuelta.",
            1 => "Ida y vuelta. Toca otro punto para hacer una vuelta.",
            var n => $"Vuelta por {n} puntos.",
        };
        MapView.Map.RefreshGraphics();
    }

    private void ShowRoute(Route? route)
    {
        _routeLayer.Features = route is null
            ? []
            : [new GeometryFeature(new LineString(route.Points.Select(p => ToNts(p.Position)).ToArray()))];
        _routeLayer.DataHasChanged();
        if (route is null)
        {
            _reachedLayer.Features = [];
            _snapLayer.Features = [];
            _reachedLayer.DataHasChanged();
            _snapLayer.DataHasChanged();
            ShowRideWind([]);
        }
        MapView.Map.RefreshGraphics();
    }

    private void ShowHomeWind(WindArrow? arrow)
    {
        _homeWindLayer.Features = arrow is null ? [] : ArrowFeatures(arrow);
        _homeWindHaloLayer.Features = arrow is null ? [] : ArrowFeatures(arrow);
        _homeWindLayer.DataHasChanged();
        _homeWindHaloLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    private void ShowRideWind(IReadOnlyList<WindArrow> arrows)
    {
        _windHaloLayer.Features = arrows.SelectMany(ArrowFeatures).ToList();
        _windHaloLayer.DataHasChanged();
        foreach (var (feel, layer) in _windLayers)
        {
            layer.Features = arrows.Where(a => a.Feel == feel).SelectMany(ArrowFeatures).ToList();
            layer.DataHasChanged();
        }
        MapView.Map.RefreshGraphics();
    }

    /// <summary>The shaft and the head of an arrow, as two lines.</summary>
    private static IEnumerable<IFeature> ArrowFeatures(WindArrow arrow) =>
    [
        new GeometryFeature(new LineString([ToNts(arrow.Tail), ToNts(arrow.Tip)])),
        new GeometryFeature(new LineString([ToNts(arrow.HeadLeft), ToNts(arrow.Tip), ToNts(arrow.HeadRight)])),
    ];

    private static VectorStyle ArrowStyle(Microsoft.Maui.Graphics.Color color, double width) => new()
    {
        Line = new Pen(ToMapsui(color, 230), width),
        Fill = null,
        Outline = null,
    };

    /// <summary>
    /// Where the route really passes for each point touched: a solid dot there and, when it is not where the finger
    /// was (no asphalt nearer), a thin line from the touched point to it.
    /// </summary>
    private void ShowReached(TracedRoute traced)
    {
        var reached = traced.Reached;
        _reachedLayer.Features = reached.Select(p => new PointFeature(ToMercator(p))).ToList();
        _snapLayer.Features = traced.Request.Via
            .Select((via, i) => (Via: via, Reached: reached[i]))
            .Where(pair => pair.Via.DistanceKmTo(pair.Reached) >= 0.3)
            .Select(pair => (IFeature)new GeometryFeature(new LineString([ToNts(pair.Via), ToNts(pair.Reached)])))
            .ToList();
        _reachedLayer.DataHasChanged();
        _snapLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    /// <summary>The sector, translucent, in the guide color.</summary>
    private static VectorStyle FanStyle() => new()
    {
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Guide, 48)),
        Outline = new Pen(ToMapsui(HudTheme.Guide, 180), 2),
        Line = null,
    };

    /// <summary>The route: a firm line in the guide color.</summary>
    private static VectorStyle RouteStyle() => new()
    {
        Line = new Pen(ToMapsui(HudTheme.Guide, 230), 5),
        Fill = null,
        Outline = null,
    };

    /// <summary>Home: a marker-colored dot with a light rim.</summary>
    private static SymbolStyle HomeStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.5,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Marker, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Text, 255), 2),
    };

    /// <summary>The points touched: smaller dots in the marker color.</summary>
    private static SymbolStyle ViaStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.4,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Background, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Marker, 255), 3),
    };

    /// <summary>Where the route really passes: a small solid dot in the marker color.</summary>
    private static SymbolStyle ReachedStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.3,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Marker, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Background, 255), 1),
    };

    /// <summary>From the touched point to where the route passes: thin, in the marker color.</summary>
    private static VectorStyle SnapStyle() => new()
    {
        Line = new Pen(ToMapsui(HudTheme.Marker, 200), 2) { PenStyle = PenStyle.Dash },
        Fill = null,
        Outline = null,
    };

    private static Mapsui.Styles.Color ToMapsui(Microsoft.Maui.Graphics.Color color, int alpha) =>
        new((int)(color.Red * 255), (int)(color.Green * 255), (int)(color.Blue * 255), alpha);

    /// <summary>Metres per pixel of Web Mercator at a zoom level (at the equator): the tile size over the world.</summary>
    private static double ResolutionAt(int zoom) => 2 * Math.PI * 6378137 / 256 / Math.Pow(2, zoom);

    private static MPoint ToMercator(GeoCoordinate position)
    {
        var (x, y) = SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        return new MPoint(x, y);
    }

    private static Coordinate ToNts(GeoCoordinate position)
    {
        var m = ToMercator(position);
        return new Coordinate(m.X, m.Y);
    }

    private async void OnAttributionTapped(object? sender, TappedEventArgs e) =>
        await Launcher.OpenAsync(_tiles.AttributionUrl);
}
