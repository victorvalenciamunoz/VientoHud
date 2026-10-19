using Mapsui;
using Mapsui.Projections;
using VientoHud.App.Maps;

namespace VientoHud.App;

/// <summary>The one screen: the map, where the ride will be traced (phase 7c: the skeleton with the map only).</summary>
public partial class MapPage : ContentPage
{
    private readonly TileSources _tiles;

    public MapPage(TileSources tiles)
    {
        InitializeComponent();
        _tiles = tiles;
        MapName.Text = tiles.Name;
        Attribution.Text = tiles.Attribution;
        MapView.Map.Layers.Add(tiles.CreateLayer());
        // Until the device location arrives (7d): León, where the author rides. The navigator keeps the call until
        // the map has its size.
        MapView.Map.Navigator.CenterOnAndZoomTo(ToMercator(42.5987, -5.5671), ResolutionAt(12));
    }

    /// <summary>Metres per pixel of Web Mercator at a zoom level (at the equator): the tile size over the world.</summary>
    private static double ResolutionAt(int zoom) => 2 * Math.PI * 6378137 / 256 / Math.Pow(2, zoom);

    private static MPoint ToMercator(double latitude, double longitude)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        return new MPoint(x, y);
    }

    private async void OnAttributionTapped(object? sender, TappedEventArgs e) =>
        await Launcher.OpenAsync(_tiles.AttributionUrl);
}
