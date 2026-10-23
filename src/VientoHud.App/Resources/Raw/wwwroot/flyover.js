// The 3D view (phase 8, DECISIONS 055): a thin drawing layer over MapLibre GL JS. Everything it shows comes from
// Core through C# (the scene JSON of FlyoverScene); it only puts it on the terrain and reports how it runs.
import * as maplibregl from './lib/maplibre/maplibre-gl.mjs';

const TERRAIN_TILES = 'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png';
const TERRAIN_ATTRIBUTION = 'Relieve: SRTM (USGS) y EU-DEM (Copernicus) vía Mapzen';

let map = null;
let frames = 0;
let tilesRequested = 0;
let bytesSeen = 0;

function report(message) {
  if (window.HybridWebView) {
    window.HybridWebView.SendRawMessage(message);
  }
}

// Frames per second and tiles loaded, once a second, for the judge of the phase.
function startMeters() {
  const tick = () => { frames++; requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
  try {
    const observer = new PerformanceObserver(list => {
      for (const entry of list.getEntries()) {
        if (entry.name.includes('/{z}/'.replace('{z}', '')) || /\.(png|jpg|pbf)(\?|$)/.test(entry.name)) {
          tilesRequested++;
          bytesSeen += entry.transferSize || entry.encodedBodySize || 0;
        }
      }
    });
    observer.observe({ type: 'resource', buffered: true });
  } catch (e) {
    // No PerformanceObserver: tiles are not counted.
  }
  setInterval(() => {
    report(`meter|fps=${frames}|tiles=${tilesRequested}|bytes=${bytesSeen}`);
    frames = 0;
  }, 1000);
}

// Called from C# with the scene JSON, the raster tiles of the base map and their credit.
window.showScene = function (sceneJson, baseTiles, baseAttribution, routeColor) {
  const scene = JSON.parse(sceneJson);
  if (map) {
    map.remove();
  }
  map = new maplibregl.Map({
    container: 'map',
    style: {
      version: 8,
      sources: {
        base: { type: 'raster', tiles: [baseTiles], tileSize: 256, maxzoom: 18, attribution: baseAttribution },
        terrain: { type: 'raster-dem', tiles: [TERRAIN_TILES], tileSize: 256, encoding: 'terrarium', maxzoom: 15, attribution: TERRAIN_ATTRIBUTION },
        track: { type: 'geojson', data: scene.pieces },
      },
      layers: [
        { id: 'base', type: 'raster', source: 'base' },
        { id: 'hills', type: 'hillshade', source: 'terrain', paint: { 'hillshade-exaggeration': 0.35 } },
        { id: 'track-halo', type: 'line', source: 'track',
          layout: { 'line-cap': 'round', 'line-join': 'round' },
          paint: { 'line-color': '#0B1218', 'line-width': 8, 'line-opacity': 0.6 } },
        { id: 'track', type: 'line', source: 'track',
          layout: { 'line-cap': 'round', 'line-join': 'round' },
          paint: { 'line-color': ['coalesce', ['get', 'color'], routeColor], 'line-width': 4 } },
      ],
      terrain: { source: 'terrain', exaggeration: 1.0 },
      sky: {},
    },
    bounds: scene.bounds,
    fitBoundsOptions: { padding: 40 },
    pitch: 60,
    maxPitch: 85,
    attributionControl: { compact: true },
    canvasContextAttributes: { preserveDrawingBuffer: true },
  });
  map.on('load', () => {
    const gl = map.getCanvas().getContext('webgl2') || map.getCanvas().getContext('webgl');
    report('loaded|' + (gl ? gl.getParameter(gl.VERSION) + ' / ' + gl.getParameter(gl.RENDERER) : 'no gl'));
    // The start of the ride, seen from the south-west and above.
    map.easeTo({ center: scene.start, zoom: 13, pitch: 65, bearing: 20, duration: 1500 });
    // A picture of what WebGL drew, for the judge (the emulator's screen capture leaves the canvas black).
    setTimeout(() => {
      const source = map.getCanvas();
      const copy = document.createElement('canvas');
      copy.width = Math.round(source.width / 3);
      copy.height = Math.round(source.height / 3);
      copy.getContext('2d').drawImage(source, 0, 0, copy.width, copy.height);
      report('snapshot|' + copy.toDataURL('image/png').split(',')[1]);
    }, 6000);
  });
  map.on('error', e => report('error|' + (e && e.error && e.error.message ? e.error.message : String(e))));
};

window.onerror = (message, source, line) => { report(`error|${message} (${source}:${line})`); };

// The bridge to C# (window.HybridWebView) may not be there yet when this module runs: wait for it.
function whenBridgeReady(tries) {
  console.log('VientoHud 3D: bridge ' + typeof window.HybridWebView + ', try ' + tries);
  if (window.HybridWebView) {
    startMeters();
    report('ready');
  } else if (tries < 100) {
    setTimeout(() => whenBridgeReady(tries + 1), 100);
  }
}
whenBridgeReady(0);
