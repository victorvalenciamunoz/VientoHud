# Avisos de terceros

VientoHud incluye o usa el siguiente software, datos, servicios y fuentes de terceros, cada uno con su propia licencia.

## Software (paquetes NuGet)

| Componente | Licencia | Origen |
|---|---|---|
| .NET MAUI | MIT | https://github.com/dotnet/maui |
| Mapsui 5.1.0 (con BruTile) | MIT (BruTile: Apache 2.0) | https://github.com/Mapsui/Mapsui |
| MapLibre GL JS 6.15.0 (incluido en la app, vista 3D) | BSD 3 cláusulas ([`licenses/BSD-MapLibre.txt`](licenses/BSD-MapLibre.txt)) | https://github.com/maplibre/maplibre-gl-js |
| SkiaSharp | MIT | https://github.com/mono/SkiaSharp |
| Microsoft.Extensions.Http.Resilience (sobre Polly) | MIT (Polly: BSD 3 cláusulas) | https://github.com/dotnet/extensions |
| PureHDF | MIT | https://github.com/Apollo3zehn/PureHDF |
| xUnit (solo tests) | Apache 2.0 | https://github.com/xunit/xunit |

## Fuentes tipográficas (incluidas en la app)

| Fuente | Licencia | Texto de la licencia |
|---|---|---|
| Chakra Petch | SIL Open Font License 1.1 | [`licenses/OFL-ChakraPetch.txt`](licenses/OFL-ChakraPetch.txt) |
| Open Sans | SIL Open Font License 1.1 | [`licenses/OFL-OpenSans.txt`](licenses/OFL-OpenSans.txt) |

## Datos y servicios (en tiempo de ejecución)

- **Rutas:** BRouter (MIT, https://github.com/abrensch/brouter) sobre datos de OpenStreetMap.
  *Routing: BRouter · Map data © OpenStreetMap contributors (ODbL).*
- **Mapa de fondo:** OpenCycleMap de Thunderforest (https://www.thunderforest.com; *Maps © Thunderforest, Data ©
  OpenStreetMap contributors*) o, sin clave, las teselas estándar de OpenStreetMap
  (https://www.openstreetmap.org/copyright; *© OpenStreetMap contributors*).
- **Relieve (vista 3D):** teselas Terrarium de Mapzen en AWS Open Data (https://registry.opendata.aws/terrain-tiles/).
  *SRTM data courtesy of the U.S. Geological Survey · Europe terrain data produced using Copernicus data and
  information funded by the European Union - EU-DEM layers.*
- **Previsión de viento y lluvia:** Open-Meteo (https://open-meteo.com). *Weather data by Open-Meteo.com (CC BY 4.0).*
- **Radar:** EUMETNET OPERA, Open Radar Data. *Radar: EUMETNET OPERA (CC BY 4.0).*
- **Observaciones para los jueces** (solo en el ordenador): Iowa Environmental Mesonet (METAR, ASOS) y AEMET OpenData.
