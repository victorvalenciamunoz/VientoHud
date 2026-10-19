using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using VientoHud.App.Services;
using VientoHud.Core.Net;

namespace VientoHud.App.Maps;

/// <summary>
/// The map tiles (DECISIONS 050): OpenCycleMap by Thunderforest when the app was built with the key in
/// <c>VIENTOHUD_THUNDERFOREST_KEY</c> (asphalt against dirt, relief, cycle routes), the standard OpenStreetMap tiles
/// otherwise. Only the tiles on screen are asked for, with the app's user agent; those seen are kept on the device,
/// which both allow.
/// </summary>
public sealed class TileSources
{
    private readonly string? _thunderforestKey = AppSettings.ThunderforestKey;

    public bool HasThunderforest => !string.IsNullOrWhiteSpace(_thunderforestKey);

    public string Name => HasThunderforest ? "OPENCYCLEMAP" : "OPENSTREETMAP · SIN CLAVE";

    /// <summary>The credit each provider's terms ask for.</summary>
    public string Attribution => HasThunderforest
        ? "Mapas © Thunderforest · Datos © colaboradores de OpenStreetMap"
        : "© colaboradores de OpenStreetMap";

    public string AttributionUrl => HasThunderforest ? "https://www.thunderforest.com" : "https://www.openstreetmap.org/copyright";

    /// <summary>
    /// The tile layer. Its own attribution box (white, Mapsui's) is switched off: the page shows the credit in the
    /// family's look, with the link the terms ask for.
    /// </summary>
    public TileLayer CreateLayer()
    {
        var layer = HasThunderforest
            ? new TileLayer(new HttpTileSource(
                new GlobalSphericalMercator(),
                $"https://tile.thunderforest.com/cycle/{{z}}/{{x}}/{{y}}.png?apikey={_thunderforestKey}",
                name: "OpenCycleMap",
                persistentCache: new FileCache(Path.Combine(FileSystem.CacheDirectory, "tiles", "opencyclemap"), "png"),
                attribution: new Attribution(Attribution, AttributionUrl),
                configureHttpRequestMessage: r => r.Headers.TryAddWithoutValidation("User-Agent", ResilientHttp.UserAgent)))
                { Name = "OpenCycleMap" }
            : OpenStreetMap.CreateTileLayer(ResilientHttp.UserAgent);
        layer.Attribution.Enabled = false;
        return layer;
    }
}
