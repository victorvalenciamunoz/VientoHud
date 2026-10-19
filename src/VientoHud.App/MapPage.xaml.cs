using Mapsui;
using Mapsui.Layers;
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
/// The one screen: where the rider is, which way to head (phase 7d) and the map where the ride will be traced (7e).
/// Layout only: the position comes from the device and every judgement and sentence from Core.
/// </summary>
public partial class MapPage : ContentPage
{
    /// <summary>Until the rider can choose (phase 7g): a two-hour ride.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(2);

    private readonly TileSources _tiles;
    private readonly ILocationProvider _location;
    private readonly DirectionAdvisor _direction;
    private readonly MemoryLayer _fanLayer = new("Fan") { Style = FanStyle() };
    private readonly MemoryLayer _homeLayer = new("Home") { Style = HomeStyle() };
    private bool _started;
    private CancellationTokenSource? _cancel;

    public MapPage(TileSources tiles, ILocationProvider location, DirectionAdvisor direction)
    {
        InitializeComponent();
        _tiles = tiles;
        _location = location;
        _direction = direction;
        MapName.Text = tiles.Name;
        Attribution.Text = tiles.Attribution;
        MapView.Map.Layers.Add(tiles.CreateLayer());
        MapView.Map.Layers.Add(_fanLayer);
        MapView.Map.Layers.Add(_homeLayer);
        // Until the device answers: León, where the author rides. The navigator keeps the call until the map has its size.
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(new GeoCoordinate(42.5987, -5.5671)), ResolutionAt(12));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_started)
        {
            _started = true;
            _ = AdviseAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cancel?.Cancel();
    }

    private async void OnRetryClicked(object? sender, EventArgs e) => await AdviseAsync();

    /// <summary>The position, then the wind and where to head; what failed stays on screen with a way to try again.</summary>
    private async Task AdviseAsync()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        var cancellationToken = _cancel.Token;
        Retry.IsVisible = false;
        Headline.IsVisible = Detail.IsVisible = false;
        try
        {
            Status.Text = "Buscando tu posición…";
            var fix = await _location.GetCurrentAsync(cancellationToken);
            ShowHome(fix.Position);

            Status.Text = "Pidiendo el viento…";
            var targetKm = RideDistance.FromDuration(DefaultDuration);
            var advice = await _direction.AdviseAsync(fix.Position, targetKm, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
            Headline.Text = RideText.DirectionHeadline(advice.Wind);
            Detail.Text = RideText.Wind(advice.Wind);
            Headline.IsVisible = Detail.IsVisible = true;
            Status.Text = (fix.AccuracyMeters is { } accuracy ? $"Posición ±{accuracy:0} m · " : "") +
                          $"salida de {DefaultDuration.TotalHours:0} h, viento de las próximas {advice.Hours} h";
            ShowFan(advice.Fan);
        }
        catch (OperationCanceledException)
        {
        }
        catch (LocationUnavailableException ex)
        {
            Status.Text = ex.Message;
            Retry.IsVisible = true;
        }
        catch (WindForecastException ex)
        {
            Status.Text = $"Sin previsión de viento: {ex.Message}";
            Retry.IsVisible = true;
        }
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
            : [new GeometryFeature(new Polygon(new LinearRing(ring.Select(p => ToMercator(p)).Select(m => new Coordinate(m.X, m.Y)).ToArray())))];
        _fanLayer.DataHasChanged();
        MapView.Map.RefreshGraphics();
    }

    /// <summary>The sector, translucent, in the guide color.</summary>
    private static VectorStyle FanStyle() => new()
    {
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Guide, 56)),
        Outline = new Pen(ToMapsui(HudTheme.Guide, 200), 2),
        Line = null,
    };

    /// <summary>Home: a marker-colored dot with a light rim.</summary>
    private static SymbolStyle HomeStyle() => new()
    {
        SymbolType = SymbolType.Ellipse,
        SymbolScale = 0.5,
        Fill = new Mapsui.Styles.Brush(ToMapsui(HudTheme.Marker, 255)),
        Outline = new Pen(ToMapsui(HudTheme.Text, 255), 2),
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

    private async void OnAttributionTapped(object? sender, TappedEventArgs e) =>
        await Launcher.OpenAsync(_tiles.AttributionUrl);
}
