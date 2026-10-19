# Scripts de investigación

Scripts sueltos de C# (`dotnet run fichero.cs`) usados para investigar. No forman parte de la solución ni tienen tests.
Usan `VientoHud.Core` con `#:project`. Ejecutarlos desde la raíz del repositorio, para que `global.json` fije el SDK 10:

```
dotnet run tools/investigacion/loops.cs -- <carpeta-salida> 3 leon-N:42.5987:-5.5671:0:8
```

Los que llaman a brouter.de esperan algo más de 1 s entre peticiones. Aun así, brouter.de respondió
HTTP 403 "Please, retry later!" tras unas 75 peticiones en unos 10 minutos (2026-10-15). Hay que hacer pocas pruebas y espaciarlas.

| Script | Qué hace |
|---|---|
| `record.cs` | Pide vueltas en abanico (salida + 4 puntos a R km) a brouter.de y guarda la respuesta tal cual (fixtures). |
| `analyze.cs` | Pasa `RouteAnalyzer` sobre respuestas grabadas de BRouter, con varias opciones de repetición. |
| `repeats.cs` | Tramos repetidos de un GPX según `RouteAnalyzer`: dónde, dónde está su otra pasada y si son ramales (estrellas). |
| `stem.cs` | Primera versión del criterio de forma (cuerda junto a casa frente a lo repetido fuera de ella), con una tira de texto a lo largo de la ruta. Sustituido por los ramales de `RouteAnalyzer` (`DECISIONS.md` 015). |
| `shape.cs` | Forma de un track (GPX o GeoJSON de BRouter): km, % repetido, punto más lejano, superficie encerrada. |
| `loops.cs` | Fase 2: compara formas de evitar la repetición (abanico actual, penalizar lo recorrido, mover puntos de ramales). Con `ALLOW_TRUNK=1`, el perfil estricto con `trunk` permitido. Escribe un GPX por resultado. |
| `livetest.cs` | Prueba en vivo del paso 4: vueltas con hasta 12 peticiones (abanico, mover puntos de ramal, ajustar R, girar el rumbo). Guarda cada respuesta en caché y se para ante un 403. Argumento `strict` o `trunk`. |
| `matchrides.cs` | Empareja salidas grabadas (GPX) con BRouter, con un punto de paso por km y `trunk` permitido, y cuenta los km por tipo de vía; dice dónde están los tramos `trunk`. |
| `generate.cs` | Pasada en vivo del generador de Core (`LoopGenerator`) con el cliente real de BRouter: un rumbo por caso, o `*` para tres candidatas cada 120°. Escribe el GPX de la mejor de cada rumbo. |
| `stubs.cs` | Medias vueltas cortas de un GPX (entrar y salir por el mismo sitio): dónde y cuánto entra. Lo usé para medir las de las vueltas de León antes de llevarlo a `RouteAnalyzer`. |
| `judge.cs` | Juez de la fase 2: genera vueltas en varias salidas y distancias contra un BRouter (pensado para el local) y saca una tabla con validez, error de distancia, km por tipo de vía y sin dato de firme, repetición, medias vueltas y validez del GPX contra el esquema. |
| `togpx.cs` | Convierte una respuesta grabada de BRouter en GPX para mirarla en gpx.studio. |
| `legs.cs` | Fase 3: tramos de ~1 km de un GPX (`RouteLegs`) con su rumbo, rectitud y hora de paso. |
| `routewind.cs` | Fase 3: viento previsto (Open-Meteo, un punto cada 5 km, una llamada) en cada tramo de un GPX: de cara y de lado. Puede guardar la respuesta (fixtures). |
| `calibrate.cs` | Fase 3: cuánto frenan al autor el viento y la pendiente, con sus salidas de Garmin (hora y altitud) y la previsión archivada de AROME en cada tramo. Llevó a DECISIONS 021. |
| `timing.cs` | Fase 3: compara la hora de paso prevista por `RouteLegs` (fija y por pendiente) con la real de salidas de Garmin. |
| `rank.cs` | Fase 3: ejemplos de la puntuación: vueltas en varios rumbos (BRouter local) puntuadas en los dos sentidos con el viento archivado de un día pasado, con la explicación por tramos de las dos primeras; un GPX por opción con su puesto. |
| `judge3.cs` | Juez de la fase 3 (`DECISIONS.md` 024): días pasados con viento en varios sitios; elige y ordena las vueltas con la previsión archivada ("salgo ahora" y "el día antes") y las repuntúa con el METAR; tabla y CSV. Pensado para el BRouter local (~1,5 h). |
| `radarlive.cs` | Fase 4: prueba en vivo de `OperaRadarSource`: la última imagen de OPERA (o la de una hora dada), recortada alrededor de un punto; lluvia en el punto, zona con datos y mojada, tiempos. |
| `motioncheck.cs` | Fase 4: ¿sirve el movimiento para lo que viene? En momentos de días de lluvia, calcula el movimiento con las imágenes hasta T−30, mueve la de T−30 y la compara con la real de T frente a dejarla quieta. CSV y resumen. |
| `motionwind.cs` | Fase 4: compara la salida de `motioncheck.cs` con el viento a 700 hPa del archivo de Open-Meteo (dirección y velocidad). |
| `nowcastcheck.cs` | Fase 4: previsión de lluvia en un aeropuerto a +30/+60/+90 min en momentos pasados (como `radar`), frente a lo que vio el radar en esa celda y el METAR, con el rival "nada se mueve". CSV y recuento por plazo. |
| `judge4.cs` | Juez de la fase 4 (`DECISIONS.md` 031): días con lluvia en el METAR de Madrid y León; en cada momento, previsión a +30/+60/+90 min en cada aeropuerto (movida con tres círculos, quieta, AROME HD 15 min) contra el radar y contra el METAR. `casos.csv` y `resumen.md`; guarda los recortes de radar comprimidos. ~1-2 h. |
| `judge5a.cs` | Fase 5, paso 5a-2: juez de AROME HD 15 min en una zona con pasadas antiguas (Single Runs API, cada 3 h) en los días más lluviosos de Barajas y León: radios, márgenes de tiempo, un punto y el radar movido, contra el radar y el METAR. Guarda las respuestas de AROME y los recortes. ~1,5 h y ~6.000 llamadas. |
| `rainroute.cs` | Fase 5, paso 5a-3: lluvia prevista en cada tramo de una salida (GPX) que empieza en un momento pasado, con lo que se sabía entonces (radar y su movimiento, pasada archivada de AROME, reserva del archivo), frente a lo que vio el radar en cada tramo a la hora de paso. |
| `judge5.cs` | Juez de la fase 5: en días de lluvia (La Bañeza y Humanes), la app decide a las 9:00 y 12:00 con lo que se sabía entonces (vuelta, sentido, esperar) y se mira lo que pasó (km mojados según el radar, viento de los METAR) frente a una vuelta sin mirar el tiempo. Guarda en disco las respuestas de Open-Meteo e IEM y los recortes de radar (se puede repetir o retomar con la misma carpeta). BRouter local. |
| `outandback.cs` | Fase 6: idas y vueltas del generador (`RouteShape.OutAndBack`) en vivo contra el BRouter local en los sitios de prueba, tres rumbos; validez, distancia, peticiones, tipos de vía y un GPX de cada una. |
| `urban.cs` | Fase 6: km en ciudad (`RouteUrban`) de las vueltas e idas y vueltas de hoy en tres rumbos desde los sitios de prueba, y km de vuelta cerca de casa; un GPX por ruta con un nombre citable (`humanes-N-iv`). |
| `ridesurban.cs` | Fase 6: km en ciudad (`RouteUrban`) de las salidas reales del autor, pasándolas por el BRouter local con un punto cada km. |
| `brouterspeed.cs` | Fase 7: cuánto tarda una sola petición (tres idas de 23 km y una vuelta de 60 km) en cada servidor de BRouter que se le pase, en Fuenlabrada y León, con 1,5 s entre peticiones. |
