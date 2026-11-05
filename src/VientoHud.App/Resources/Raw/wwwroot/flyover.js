// The 3D view (phase 8, DECISIONS 055): a thin drawing layer over MapLibre GL JS. Everything it shows comes from
// Core through C# (the scene JSON of FlyoverScene); it only puts it on the terrain and reports how it runs.
import * as maplibregl from './lib/maplibre/maplibre-gl.mjs';

// Every tile is asked of the page's own origin: C# serves it from the device's store or fetches it once (no CORS
// in the way, and the map provider's key never reaches the JavaScript).
const TERRAIN_TILES = `${location.origin}/tiles/terrarium/{z}/{x}/{y}.png`;
const BASE_TILES = `${location.origin}/tiles/base/{z}/{x}/{y}.png`;
const TERRAIN_ATTRIBUTION = 'Relieve: SRTM (USGS) y EU-DEM (Copernicus) vía Mapzen';

let map = null;
let frames = 0;
let tilesRequested = 0;
let bytesSeen = 0;

let stopped = false;
let meterTimer = null;

function report(message) {
  if (!stopped && window.HybridWebView) {
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
  meterTimer = setInterval(() => {
    report(`meter|fps=${frames}|tiles=${tilesRequested}|bytes=${bytesSeen}`);
    frames = 0;
  }, 1000);
}

// Called from C# with the scene JSON, the base map's credit and the route colour.
// baseTileSize: what C# says the base tiles measure (512: Thunderforest's double scale, a quarter of the requests).
window.showScene = function (sceneJson, baseAttribution, routeColor, baseTileSize) {
  const scene = JSON.parse(sceneJson);
  if (map) {
    map.remove();
  }
  map = new maplibregl.Map({
    container: 'map',
    style: {
      version: 8,
      sources: {
        base: { type: 'raster', tiles: [BASE_TILES], tileSize: Number(baseTileSize) || 256, maxzoom: 18, attribution: baseAttribution },
        // Up to zoom 12, the data's own resolution: the corridor C# keeps ahead covers 8 to 12 (TerrainTiles).
        terrain: { type: 'raster-dem', tiles: [TERRAIN_TILES], tileSize: 256, encoding: 'terrarium', maxzoom: 12, attribution: TERRAIN_ATTRIBUTION },
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
  });
  map.on('load', () => {
    const gl = map.getCanvas().getContext('webgl2') || map.getCanvas().getContext('webgl');
    report('loaded|' + (gl ? gl.getParameter(gl.VERSION) + ' / ' + gl.getParameter(gl.RENDERER) : 'no gl'));
    loaded = true;
    // The camera at the start of the flight, paused: nothing moves until PLAY.
    if (path) {
      place(position);
    }
    if (weather) {
      drawWeather();
    }
  });
  map.on('error', e => report('error|' + (e && e.error && e.error.message ? e.error.message : String(e))));
  // A finger on the map takes the camera: the flight pauses where it is.
  map.on('dragstart', e => { if (e.originalEvent) { pauseBy('user'); } });
  // Moving the camera from here fires these too: only a finger (an original event) pauses.
  map.on('rotatestart', e => { if (e.originalEvent) { pauseBy('user'); } });
  map.on('pitchstart', e => { if (e.originalEvent) { pauseBy('user'); } });
};

// The flight (phase 8, step 5): Core computed the frames (FlyoverPath); here they are only played, the camera
// moving smoothly from one to the next. Position is in frames, from 0 to the last one.
let path = null;
let loaded = false;
let view = 'close';
let position = 0;
let playing = false;
let lastTime = 0;
let lastReport = 0;

function lerpBearing(a, b, t) {
  const turn = ((b - a + 540) % 360) - 180;
  return (a + turn * t + 360) % 360;
}

function place(at) {
  const frames = path.frames;
  const i = Math.max(0, Math.min(Math.floor(at), frames.length - 2));
  const t = Math.max(0, Math.min(1, at - i));
  const a = frames[i];
  const b = frames[i + 1] || a;
  const v = path.views[view];
  map.jumpTo({
    center: [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t],
    bearing: lerpBearing(a[2], b[2], t),
    zoom: v.zoom,
    pitch: v.pitch,
  });
  windowArrows((at / (frames.length - 1)) * path.lengthKm);
}

// Only the arrows of the stretch being flown, from Core's window (FlyoverWeather): on an out and back the other
// way's arrows stand right beside these. Refreshed every 100 m of flight.
let windowAt = null;

function windowArrows(km) {
  if (!weather || !loaded) {
    return;
  }
  const at = Math.round(km * 10) / 10;
  if (at === windowAt) {
    return;
  }
  windowAt = at;
  const w = weather.window;
  for (const set of Object.keys(ARROW_SETS)) {
    const ahead = set === 'close' ? w.closeAheadKm : w.farAheadKm;
    const filter = ['all', ['>=', ['get', 'atKm'], at - w.behindKm], ['<=', ['get', 'atKm'], at + ahead]];
    for (const id of [`${set}-halo`, set]) {
      if (map.getLayer(id)) {
        map.setFilter(id, filter);
      }
    }
  }
  // The marks and drops of the stretch being flown, as far ahead as the view in use sees.
  const ahead = view === 'close' ? w.closeAheadKm : w.farAheadKm;
  for (const id of MARK_LAYERS) {
    if (map.getLayer(id)) {
      map.setFilter(id, ['all', ['>=', ['get', 'atKm'], at - w.behindKm], ['<=', ['get', 'atKm'], at + ahead]]);
    }
  }
}

function step(now) {
  if (!playing) {
    return;
  }
  const last = path.frames.length - 1;
  // The first frame may come stamped a hair before PLAY: never go backwards.
  position = Math.min(last, position + (Math.max(0, now - lastTime) / path.durationMs) * last);
  lastTime = now;
  place(position);
  if (now - lastReport > 100) {
    report(`pos|${position / last}`);
    lastReport = now;
  }
  if (position >= last) {
    playing = false;
    report('ended');
    return;
  }
  requestAnimationFrame(step);
}

function pauseBy(who) {
  if (playing) {
    playing = false;
    report(`paused|${who}`);
  }
}

window.setPath = function (pathJson) {
  path = JSON.parse(pathJson);
  position = 0;
  if (loaded) {
    place(position);
  }
};

window.play = function () {
  if (!path || !loaded) {
    return;
  }
  if (position >= path.frames.length - 1) {
    position = 0;
  }
  playing = true;
  lastTime = performance.now();
  requestAnimationFrame(step);
};

window.pause = function () { pauseBy('button'); };

// Jump to a point of the flight, as a fraction from 0 to 1 (the slider).
window.seek = function (fraction) {
  if (!path || !loaded) {
    return;
  }
  position = Math.max(0, Math.min(1, fraction)) * (path.frames.length - 1);
  place(position);
};

window.setView = function (name) {
  view = name in path.views ? name : 'close';
  showArrowsOf(view);
  windowAt = null;
  if (loaded && !playing) {
    place(position);
  }
};

// The weather over the route (phase 8, step 6): Core composed the wind arrows at the time of passing and the GPX's
// alerts (FlyoverWeather); C# gives the app's colours by feel and kind. Here they are only drawn on the terrain.
let weather = null;
let palette = null;

function byKey(property, fallback) {
  const expression = ['match', ['get', property]];
  for (const [key, colour] of Object.entries(palette)) {
    expression.push(key, colour);
  }
  expression.push(fallback);
  return expression;
}

// Two sets of arrows from Core: one for each camera view (the far ones fell out of the close view's picture).
const ARROW_SETS = { far: 'arrows', close: 'closeArrows' };

// As on the map (phase 10, DECISIONS 065): the wind's arrows in white say how it blows; the small arrows of the way
// round, colored by feel, say how it hits the rider; a blue drop by them where rain is likely as they pass.
const MARK_LAYERS = ['marks', 'drops'];

// An image drawn on a canvas, for a symbol layer.
function canvasImage(width, height, draw) {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  draw(context);
  return context.getImageData(0, 0, width, height);
}

function addMarkImages() {
  if (!map.hasImage('mark')) {
    // A small arrow pointing up, white: an SDF image the layer colors by feel, with a dark halo.
    map.addImage('mark', canvasImage(28, 40, c => {
      c.fillStyle = '#FFFFFF';
      c.beginPath();
      c.moveTo(14, 2); c.lineTo(26, 18); c.lineTo(18.5, 18); c.lineTo(18.5, 38);
      c.lineTo(9.5, 38); c.lineTo(9.5, 18); c.lineTo(2, 18); c.closePath();
      c.fill();
    }), { sdf: true, pixelRatio: 1 });
  }
  if (!map.hasImage('drop')) {
    map.addImage('drop', canvasImage(36, 48, c => {
      c.fillStyle = '#2F6BFF';
      c.strokeStyle = '#0B1218';
      c.lineWidth = 4;
      c.beginPath();
      c.moveTo(18, 3);
      c.bezierCurveTo(18, 3, 4, 22, 4, 31);
      c.arc(18, 31, 14, Math.PI, 0, true);
      c.bezierCurveTo(32, 22, 18, 3, 18, 3);
      c.closePath();
      c.fill();
      c.stroke();
    }), { pixelRatio: 2 });
  }
}

function drawWeather() {
  for (const id of ['far-halo', 'far', 'close-halo', 'close', ...MARK_LAYERS]) {
    if (map.getLayer(id)) {
      map.removeLayer(id);
    }
  }
  for (const id of ['far', 'close', ...MARK_LAYERS]) {
    if (map.getSource(id)) {
      map.removeSource(id);
    }
  }
  for (const [set, key] of Object.entries(ARROW_SETS)) {
    map.addSource(set, { type: 'geojson', data: weather[key] });
    const visibility = set === view ? 'visible' : 'none';
    map.addLayer({ id: `${set}-halo`, type: 'line', source: set,
      layout: { 'line-cap': 'round', 'line-join': 'round', visibility },
      paint: { 'line-color': '#0B1218', 'line-width': 7 } });
    map.addLayer({ id: set, type: 'line', source: set,
      layout: { 'line-cap': 'round', 'line-join': 'round', visibility },
      paint: { 'line-color': '#E8EEF4', 'line-width': 3.5 } });
  }
  addMarkImages();
  map.addSource('marks', { type: 'geojson', data: weather.marks });
  map.addLayer({ id: 'marks', type: 'symbol', source: 'marks',
    layout: { 'icon-image': 'mark', 'icon-rotate': ['get', 'bearing'], 'icon-rotation-alignment': 'map',
      'icon-pitch-alignment': 'map', 'icon-allow-overlap': true, 'icon-ignore-placement': true },
    paint: { 'icon-color': byKey('feel', '#8FA0B0'), 'icon-halo-color': '#0B1218', 'icon-halo-width': 2 } });
  map.addSource('drops', { type: 'geojson', data: weather.drops });
  map.addLayer({ id: 'drops', type: 'symbol', source: 'drops',
    layout: { 'icon-image': 'drop', 'icon-allow-overlap': true, 'icon-ignore-placement': true, 'icon-anchor': 'bottom' } });
  windowAt = null;
  if (path) {
    windowArrows((position / (path.frames.length - 1)) * path.lengthKm);
  }
}

function showArrowsOf(name) {
  if (!loaded || !weather) {
    return;
  }
  for (const set of Object.keys(ARROW_SETS)) {
    for (const id of [`${set}-halo`, set]) {
      if (map.getLayer(id)) {
        map.setLayoutProperty(id, 'visibility', set === name ? 'visible' : 'none');
      }
    }
  }
}

window.setWeather = function (weatherJson, paletteJson) {
  weather = JSON.parse(weatherJson);
  palette = JSON.parse(paletteJson);
  if (loaded) {
    drawWeather();
  }
};

// Called from C# when the page closes: nothing keeps running or talking to a page that is gone.
window.shutdown = function () {
  stopped = true;
  playing = false;
  if (meterTimer) {
    clearInterval(meterTimer);
  }
  if (map) {
    map.remove();
    map = null;
  }
};

window.onerror =(message, source, line) => { report(`error|${message} (${source}:${line})`); };

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
