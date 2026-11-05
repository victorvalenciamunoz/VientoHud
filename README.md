<p align="center">
  <img src="src/VientoHud.App/Resources/AppIcon/appiconfg.svg" width="112" alt="VientoHud icon: three cyan gusts of wind" />
</p>

<h1 align="center">VientoHud</h1>

<p align="center">
  <b>Before a bike ride: should I go, when, and which way?</b><br/>
  It checks the wind and rain for that moment, tells you which way to head, lets you draw the route by tapping the map and sends it to your bike computer as a GPX file.
</p>

<p align="center">
  <b>English</b> · <a href="README.es.md">Español</a>
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" />
  <img alt=".NET MAUI Android" src="https://img.shields.io/badge/MAUI-Android-3DDC84?logo=android&logoColor=white" />
  <a href="https://github.com/victorvalenciamunoz/VientoHud/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/victorvalenciamunoz/VientoHud/actions/workflows/ci.yml/badge.svg" /></a>
  <img alt="Tests" src="https://img.shields.io/badge/tests-658-7CFFB2" />
  <img alt="No backend" src="https://img.shields.io/badge/backend-none-0B1218" />
  <a href="LICENSE"><img alt="MIT license" src="https://img.shields.io/badge/license-MIT-4DD2FF" /></a>
</p>

> **Language note.** The app, the advice inside the GPX file and the project documentation in [`docs/`](docs/) are in
> Spanish. This README is the English entry point; [`README.es.md`](README.es.md) is the Spanish one.

---

## What it is

Road bikes only, and no typing: your location comes from the phone and everything else is chosen by tapping.

1. **Which way.** Using the wind forecast for the hours of your ride, the app tells you which way to start (headwind on the way out, tailwind on the way back) and draws it as a fan on the map.
2. **Which route.** Long-press one point on the map (out and back) or two or more (a loop through them). One request to BRouter over paved roads and, in seconds: the best direction for the wind, the wind on each stretch at the time you will ride it (coloured arrows), the rain forecast along the route (radar nowcast first, weather model after), the km through towns and your arrival time.
3. **When.** "Go now" or "better wait until…" if waiting keeps you drier or avoids dangerous crosswind gusts. You can also pick today's start time.
4. **The GPX is the product.** The route, with the summary and warnings at the points that matter, goes to your bike computer (Hammerhead, Garmin or any other). The phone stays in your pocket.

| The route, with wind and rain | The GPX, to your device | The profile |
| --- | --- | --- |
| ![The route drawn and judged, with wind arrows and rain drops](docs/capturas/1-ruta.png) | ![The "GPX to device" panel](docs/capturas/2-panel-gpx.png) | ![The route profile in colours, with the wind by stretch](docs/capturas/3-perfil.png) |

Screenshots taken in the emulator with a recorded day (19 October 2026, at the foot of Mont Ventoux): the app relives
that moment with the forecast and radar available back then.

Sibling of [CieloHud](https://github.com/victorvalenciamunoz/CieloHud) and GincanaHud: same look, same principles.

## Data and services

No server of its own: everything comes from public services and data, behind interfaces, with their licenses in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

| Piece | Source |
|---|---|
| Hourly wind and rain | [Open-Meteo](https://open-meteo.com) (AROME in Spain, other models as fallback) |
| Short-term rain | [EUMETNET OPERA](https://www.eumetnet.eu/activities/observations-programme/current-activities/opera/) radar (Open Radar Data), moved forward in time |
| Routing | [BRouter](https://github.com/abrensch/brouter) with a custom road-bike profile on OpenStreetMap data |
| Base map | OpenCycleMap by [Thunderforest](https://www.thunderforest.com) (with a key) or, without one, OpenStreetMap |

And the weather is the judge: every phase has been checked against what actually happened (weather stations, METAR,
later radar images). The decisions and how each one was checked are in [`docs/DECISIONS.md`](docs/DECISIONS.md) (in
Spanish); the details of those checks, with data from real places, stay out of the repo.

## Structure

| Project | What it is |
|---|---|
| `src/VientoHud.Core` | All the logic, no UI: wind, radar and rain, routing, scoring, texts and GPX. |
| `src/VientoHud.Console` | Console app for validation and for the judges (`wind`, `route`, `trace`, `radar`, `verify`). It only formats what Core returns. |
| `src/VientoHud.App` | The .NET MAUI app for Android. It only shows what Core returns. |
| `tests/VientoHud.Core.Tests` | xUnit, with fake services and recorded real responses. Tests that depend on real places stay out of the repo and only run on the author's machine. |
| `docs/` | [Vision](docs/VISION.md), [phased plan](docs/PLAN.md), [status](docs/STATUS.md) and [decisions](docs/DECISIONS.md), in Spanish. |

## Build and test

You need the .NET 10 SDK (`global.json` pins the version) and, for the app, the Android workload.

```bash
dotnet build                      # Core, console, tests and app (the app takes a bit longer)
dotnet test                       # the Core tests
dotnet build src/VientoHud.App    # the Android app only
```

The console, for example, for a route drawn through one point (the coordinates are just an example):

```bash
dotnet run --project src/VientoHud.Console -- trace --lat 45.0 --lon 1.0 --via "45.05,1.3"
```

Keys and services, never in the repo:

- `VIENTOHUD_THUNDERFOREST_KEY`: Thunderforest key, read when building the app. Without it, the map is OpenStreetMap.
- `VIENTOHUD_BROUTER_URL`: your own BRouter for the console (brouter.de by default). During development a local one (e.g. on port 17777) avoids loading the public server.
- `VIENTOHUD_APP_BROUTER_URL`: the same for the app, read when building (from the emulator, `http://10.0.2.2:17777/`).

## Install on your phone

Download the APK from the [latest release](https://github.com/victorvalenciamunoz/VientoHud/releases) on the phone
itself (Android 8.0 or later) and open it. Android will ask for permission to install apps from unknown sources, and
Play Protect may warn that it doesn't know the app: that is normal for an app that is not on Google Play.

When it opens it asks for your **location** (it is the starting point; it only leaves the phone to request the weather
and the routes). It needs a connection: the forecast, radar, routes and map come from public services. The 0.x
versions are *pre-releases*: phase 7 is still open and it is worth comparing what the app says with what you then find
on the road.

## Sending the route to your bike computer

With the route drawn and judged (wind and rain included), the **GPX** button next to DETALLE (details) opens the "GPX
al aparato" (GPX to device) panel. The file carries the full advice in its description and the gust and rain warnings
as waypoints.

### Hammerhead Karoo

1. **COMPARTIR** (share).
2. In the Android share sheet, choose **Karoo Companion** (the Hammerhead app).
3. "Sending to Karoo" and "Upload complete": the route is now in your Hammerhead route list, and the Karoo downloads
   it over wifi or through the app.

The same works for any other app that accepts a shared GPX (Strava, komoot, Drive, email…).

### Garmin (Fenix, Edge…)

1. **ABRIR CON…** (open with). Garmin Connect doesn't show up in the share sheet: it only accepts "open with". If you
   have several apps that open GPX files, Android asks which one; if Garmin Connect is the only one, it opens directly.
2. Garmin Connect asks for the course type: **Road cycling**.
3. It shows the route; **Save** adds it to your courses, ready to send to your watch or bike computer.

## Status

Ten phases: nine closed, and phase 7 (the Android app) open only for its judge: real rides with the app. What is in
progress is in [`docs/STATUS.md`](docs/STATUS.md) (in Spanish).

## License

MIT, see [`LICENSE`](LICENSE). Third-party fonts, data and services have their own:
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
