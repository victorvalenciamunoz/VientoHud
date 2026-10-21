<p align="center">
  <img src="src/VientoHud.App/Resources/AppIcon/appiconfg.svg" width="112" alt="Icono de VientoHud: tres ráfagas de viento en cian" />
</p>

<h1 align="center">VientoHud</h1>

<p align="center">
  <b>Antes de salir en bici: ¿salgo, cuándo y por dónde?</b><br/>
  Mira el viento y la lluvia de ese momento, te dice hacia dónde conviene ir, trazas la ruta tocando el mapa y te la lleva al GPS de la bici en GPX.
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" />
  <img alt=".NET MAUI Android" src="https://img.shields.io/badge/MAUI-Android-3DDC84?logo=android&logoColor=white" />
  <a href="https://github.com/victorvalenciamunoz/VientoHud/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/victorvalenciamunoz/VientoHud/actions/workflows/ci.yml/badge.svg" /></a>
  <img alt="Tests" src="https://img.shields.io/badge/tests-629-7CFFB2" />
  <img alt="Sin backend" src="https://img.shields.io/badge/backend-ninguno-0B1218" />
  <a href="LICENSE"><img alt="Licencia MIT" src="https://img.shields.io/badge/licencia-MIT-4DD2FF" /></a>
</p>

---

## Qué es

Solo bici de carretera, y nada de teclear: la ubicación sale del móvil y lo demás se elige tocando.

1. **Hacia dónde.** Con el viento previsto para las horas de la salida, la app dice hacia dónde conviene empezar (ida de cara, vuelta a favor) y lo pinta como un abanico en el mapa.
2. **Por dónde.** Mantienes pulsado un punto del mapa (ida y vuelta) o dos o más (vuelta por ellos). Una petición a BRouter por carreteras asfaltadas, y en segundos: el sentido mejor para el viento, el viento tramo a tramo a la hora a la que pasarás (flechas de colores), la lluvia prevista en la ruta (radar a corto plazo y modelo después), los km de ciudad y la hora de llegada.
3. **Cuándo.** "Sal ahora" o "mejor espera hasta las…" si así te mojas menos o evitas rachas de lado peligrosas. También puedes elegir la hora de salida de hoy.
4. **El GPX es el producto.** La ruta, con el resumen y avisos en los puntos que importan, va al GPS de la bici (Hammerhead, Garmin o cualquiera). El móvil se queda guardado.

Hermano de [CieloHud](https://github.com/victorvalenciamunoz/CieloHud) y GincanaHud: misma estética, mismos principios.

## Datos y servicios

Sin servidor propio: todo son servicios y datos públicos, detrás de interfaces y con sus licencias en
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

| Pieza | Fuente |
|---|---|
| Viento y lluvia por horas | [Open-Meteo](https://open-meteo.com) (AROME en España, otros modelos de reserva) |
| Lluvia a corto plazo | Radar de [EUMETNET OPERA](https://www.eumetnet.eu/activities/observations-programme/current-activities/opera/) (Open Radar Data), movido hacia delante |
| Rutas | [BRouter](https://github.com/abrensch/brouter) con un perfil propio de bici de carretera sobre datos de OpenStreetMap |
| Mapa de fondo | OpenCycleMap de [Thunderforest](https://www.thunderforest.com) (con clave) o, sin clave, OpenStreetMap |

Y el tiempo es el juez: cada fase se ha contrastado con lo que pasó de verdad (estaciones, METAR, imágenes de radar
posteriores). Está todo en [`docs/STATUS.md`](docs/STATUS.md) y en [`docs/verificacion/`](docs/verificacion/).

## Estructura

| Proyecto | Qué es |
|---|---|
| `src/VientoHud.Core` | Todo el cálculo, sin interfaz: viento, radar y lluvia, rutas, puntuación, textos y GPX. |
| `src/VientoHud.Console` | Consola para validar y para los jueces (`wind`, `route`, `trace`, `radar`, `verify`). Solo formatea lo que devuelve Core. |
| `src/VientoHud.App` | La app .NET MAUI para Android. Solo muestra lo que devuelve Core. |
| `tests/VientoHud.Core.Tests` | xUnit, con servicios falsos y respuestas reales grabadas. |
| `tools/` | BRouter local para desarrollo y scripts de investigación y jueces. |
| `docs/` | [Visión](docs/VISION.md), [plan por fases](docs/PLAN.md), [estado](docs/STATUS.md) y [decisiones](docs/DECISIONS.md), en español. |

## Compilar y probar

Hace falta el SDK de .NET 10 (`global.json` fija la versión) y, para la app, la carga de trabajo de Android.

```bash
dotnet build                      # Core, consola, tests y app (la app tarda un poco más)
dotnet test                       # los tests de Core
dotnet build src/VientoHud.App    # solo la app Android
```

La consola, por ejemplo, para una ruta trazada por dos puntos desde León:

```bash
dotnet run --project src/VientoHud.Console -- trace --lat 42.5987 --lon -5.5671 --via "42.60,-5.30"
```

Claves y servicios, nunca en el repo:

- `VIENTOHUD_THUNDERFOREST_KEY`: clave de Thunderforest, leída al compilar la app. Sin ella, el mapa es OpenStreetMap.
- `VIENTOHUD_BROUTER_URL`: un BRouter propio para la consola (por defecto, brouter.de). `tools/brouter-local/start.ps1` arranca uno local en el puerto 17777 para no cargar el servidor público durante el desarrollo.
- `VIENTOHUD_APP_BROUTER_URL`: lo mismo para la app, leído al compilar (desde el emulador, `http://10.0.2.2:17777/`).

## Instalar en tu móvil

Descarga el APK de la [última versión](https://github.com/victorvalenciamunoz/VientoHud/releases) desde el propio móvil
(Android 8.0 o posterior) y ábrelo. Android pedirá permiso para instalar apps de origen desconocido, y Play Protect
puede avisar de que no la conoce: es normal en una app que no está en Google Play.

Al abrirla pedirá la **ubicación** (es el punto de salida; no sale del móvil salvo para pedir el tiempo y las rutas).
Necesita conexión: la previsión, el radar, las rutas y el mapa vienen de servicios públicos. Las versiones 0.x son
*pre-release*: la fase 7 sigue abierta y conviene contrastar lo que dice con lo que luego pasa en la carretera.

## Llevar la ruta al GPS de la bici

Con la ruta trazada y juzgada (viento y lluvia dentro), el botón **GPX** junto a DETALLE abre el panel "GPX al aparato".
El fichero lleva el consejo entero en la descripción y los avisos de rachas y lluvia como puntos de paso.

| 1. La ruta | 2. El panel GPX | 3. Al Karoo: COMPARTIR | 4. A Garmin: ABRIR CON… |
| --- | --- | --- | --- |
| ![La ruta trazada y juzgada](docs/capturas/1-ruta.png) | ![El panel GPX al aparato](docs/capturas/2-gpx.png) | ![La hoja de compartir con Karoo Companion](docs/capturas/3-compartir-karoo.png) | ![La ruta abierta en Garmin Connect](docs/capturas/4-garmin-connect.png) |

- **Hammerhead Karoo** (y casi cualquier app): **COMPARTIR** y elige *Karoo Companion*. La ruta aparece en el panel de
  Hammerhead y se sincroniza con el Karoo.
- **Garmin** (Fenix, Edge…): **ABRIR CON…** y elige *Garmin Connect*. Pide el tipo de trayecto (ciclismo en ruta),
  muestra la ruta y con *Guardar* queda en tus trayectos para enviarla al reloj o al ciclocomputador. Garmin Connect
  no sale en la hoja de compartir: solo acepta "abrir con".
- Cualquier otro aparato que admita GPX: COMPARTIR a la app que use, o guárdalo (Drive, correo…) y cárgalo desde allí.

Las capturas son del móvil del autor con la ubicación simulada en La Bañeza (León).

## Estado

Fase 7 de 7, la app Android, en marcha. Lo hecho y lo que falta, paso a paso y con cómo se validó, en
[`docs/STATUS.md`](docs/STATUS.md).

## Licencia

MIT, ver [`LICENSE`](LICENSE). Las fuentes, datos y servicios de terceros tienen las suyas:
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
