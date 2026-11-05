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
public sealed class TileSources(UsageMeter meter)
{
    private readonly string? _thunderforestKey = AppSettings.ThunderforestKey;

    public bool HasThunderforest => !string.IsNullOrWhiteSpace(_thunderforestKey);

    public string Name => HasThunderforest ? "OPENCYCLEMAP" : "OPENSTREETMAP · SIN CLAVE";

    /// <summary>The credit each provider's terms ask for.</summary>
    public string Attribution => HasThunderforest
        ? "Mapas © Thunderforest · Datos © colaboradores de OpenStreetMap"
        : "© colaboradores de OpenStreetMap";

    public string AttributionUrl => HasThunderforest ? "https://www.thunderforest.com" : "https://www.openstreetmap.org/copyright";

    /// <summary>The same tiles for the 3D view (phase 8), as a URL template with {z}/{x}/{y} for MapLibre.</summary>
    public string TileUrlTemplate => HasThunderforest
        ? $"https://tile.thunderforest.com/cycle/{{z}}/{{x}}/{{y}}.png?apikey={_thunderforestKey}"
        : "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>
    /// The base map of the 3D view (phase 9, step 2): the usual 256 px tiles, shown as if they were 512 px so that
    /// MapLibre asks for those of one zoom less, a quarter as many, a little blurrier close up. A 3D session took ~720
    /// tiles of the shared free plan as they were (2026-10-26); Thunderforest's double-scale tiles ("@2x") cut that by
    /// 41 % but took 24 MB of data instead of 6 (the author: too much).
    /// </summary>
    public string FlyoverTileUrlTemplate => TileUrlTemplate;

    /// <summary>The size MapLibre is told the 3D view's base tiles have.</summary>
    public int FlyoverTileSize => 512;

    /// <summary>Name of the 3D view's base tiles in the store: one per provider and size, so that they never mix.</summary>
    public string FlyoverSource => HasThunderforest ? "opencyclemap-512" : "openstreetmap-512";

    /// <summary>
    /// The tile layer. Its own attribution box (white, Mapsui's) is switched off: the page shows the credit in the
    /// family's look, with the link the terms ask for.
    /// </summary>
    /// <remarks>
    /// Mapsui fetches the tiles with its own client, so each download is counted here (phase 9): the hook runs before
    /// every request and never for a tile served from the cache. Without the bytes, which it does not see.
    /// </remarks>
    public TileLayer CreateLayer()
    {
        var name = HasThunderforest ? "OpenCycleMap" : "OpenStreetMap";
        var layer = new TileLayer(new HttpTileSource(
            new GlobalSphericalMercator(),
            TileUrlTemplate,
            name: name,
            persistentCache: new FileCache(Path.Combine(FileSystem.CacheDirectory, "tiles", name.ToLowerInvariant()), "png"),
            attribution: new Attribution(Attribution, AttributionUrl),
            configureHttpRequestMessage: r =>
            {
                r.Headers.TryAddWithoutValidation("User-Agent", ResilientHttp.UserAgent);
                meter.Record(r.RequestUri!, 0);
            }))
            { Name = name };
        layer.Attribution.Enabled = false;
        return layer;
    }
}
