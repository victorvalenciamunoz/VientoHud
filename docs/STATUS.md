# Estado

**Fase actual: 2 — Rutas circulares + GPX** (pasos 1 a 3 de 7 hechos; del 4, el 4a)

Fase 1 — Viento en consola: terminada el 2026-10-14 (los 6 pasos hechos y el juez ejecutado).
Lo hecho en la fase 1 está más abajo, a partir de "Qué funciona".

## Fase 2

### Qué funciona (fase 2)

- **Escritor de GPX** (`VientoHud.Core.Gpx`): `GpxTrack` (nombre, puntos, hora UTC, descripción y copyright opcionales),
  `GpxTrackPoint` (coordenada y altitud opcional) y `GpxWriter`. Escribe GPX 1.1 con un track y un segmento (lo que los
  GPS de bici importan como curso), UTF-8 sin BOM, punto decimal siempre, coordenadas a 7 decimales y altitud al
  decímetro. `GpxCopyright.OpenStreetMap` pone la atribución que pide la licencia ODbL.
- **Motor de rutas** (`VientoHud.Core.Routing`): interfaz `IRouteEngine` (ruta por una lista de puntos de paso; para una
  vuelta, el último es la salida), `Route` (puntos con altitud, distancia, desnivel, atribución) y `RouteSegment`
  (tramos consecutivos con las etiquetas OSM, p. ej. `highway`, `surface`). Los fallos son `RouteException` con
  `RouteFailure.Unreachable` (algún punto no se puede unir: moverlo puede ayudar) o `RouteFailure.Service`
  (red, servidor o respuesta rara).
- **Cliente de BRouter** (`BRouterRouteEngine`): sube el perfil estricto `road-strict.brf` (recurso incrustado en Core)
  una vez y pide rutas en GeoJSON con el id que devuelve el servidor. Si el servidor responde 500 sin texto (perfil
  perdido) lo sube de nuevo y repite una vez. Como mucho una petición por segundo (configurable). También admite un
  perfil del servidor (`ServerProfile`, p. ej. `fastbike`) sin subir nada.
- **Análisis de una ruta, sin red** (`RouteAnalyzer`, `RoadRules`; `DECISIONS.md` 014): km por tipo de vía y firme con
  su categoría (carretera, camino asfaltado, prohibido, desconocido), distancia de cada punto de paso pedido a la línea
  del track y fracción del recorrido repetido. Devuelve cada tramo repetido (dónde está y dónde su otra pasada) y si es
  un **ramal**: entrar y salir por la misma carretera sin salida en mitad de la ruta, lo que hace una estrella. Las
  cuerdas de un globo, los cuellos de un lazo y las idas y vueltas desde casa no son ramales (`DECISIONS.md` 015).

### Qué falta (fase 2)

- [x] 1. Elegir el motor de rutas: **BRouter** con perfil estricto propio y puntos de paso calculados en Core
  (`DECISIONS.md` 013).
- [x] 2. Escritor de GPX 1.1 en Core (track con puntos), validado en los tests contra el esquema oficial.
- [x] 3. Cliente de BRouter detrás de una interfaz: perfil estricto en el repositorio (prohíbe también carreteras en
  obras; `trunk` prohibido en todo el mundo, `DECISIONS.md` 013), subida del perfil, ruta entre puntos de paso,
  lectura de las etiquetas de cada tramo, errores ("target island", 400, 500) y fixtures grabadas.
- [ ] 4. Generador de vueltas. Dividido en:
  - [x] 4a. Análisis de una ruta, sin red: desvío de cada punto pedido, % repetido y km por tramos prohibidos
    (`DECISIONS.md` 014). Probado con rutas reales grabadas. Ampliado con los tramos repetidos y los ramales
    (`DECISIONS.md` 015), calibrado con seis salidas reales del autor.
  - [ ] Prueba en vivo antes de 4b (cuando brouter.de lo permita, pocas peticiones y espaciadas): unas 8 vueltas en
    Madrid y León, 40-100 km, rumbos variados, moviendo los puntos de ramal, ajustando R y girando el rumbo si no hay
    vuelta; perfil estricto frente a `trunk` permitido. Éxito: en al menos 6 de 8, ruta con ≤ 10 % en ramales,
    ±10 % de la distancia, 0 km prohibidos y a menos de 45° del rumbo pedido. Con los datos, el autor decide lo del
    `trunk`.
  - [ ] 4b. Colocar los puntos de paso a partir de la salida, el rumbo (hacia la parte más lejana de la vuelta) y la
    distancia objetivo: abanico de n = 5 (la salida y 4 puntos a R km en `rumbo - (90 - 180·i/n)`), R inicial ≈
    objetivo / 5,8. Sentido de giro fijo (horario en el mapa); invertirlo es cosa de la fase 3.
  - [ ] 4c. Generador: ajuste de R hasta ±10 % del objetivo con pocas llamadas, reintentos ante "target island", girar
    el rumbo ±15-30° si repite mucho, descarte de candidatas malas y varias candidatas con rumbos distintos. El rumbo es
    un parámetro (la fase 3 lo elegirá según el viento). Probado con un motor falso y luego en vivo en Madrid y León.
    Criterio de forma: ≤ 10 % de la ruta en ramales (provisional, `DECISIONS.md` 015). El análisis dice qué tramos son
    ramales y dónde: sirve para mover el punto de paso que los provoca.
    **Decisiones del autor pendientes:** desvío máximo de un punto, llamadas o segundos de espera aceptables y número
    de candidatas.
- [ ] 5. Comando de consola `route --km 60` o `--hours 2` (con velocidad media configurable),
  `[--lat --lon] [--count 3]`: escribe los GPX y un resumen.
- [ ] 6. Juez: km por tipo de vía y firme a lo largo del track, distancia dentro de ±10 %, GPX válido; pasada en Madrid
  y León con varias distancias; abrir en gpx.studio y cargar en el GPS de la bici.
- [ ] 7. Probablemente: mejores puntos de paso (pueblos, cruces) si el juez muestra mucha repetición o mucho tramo
  urbano (la salida de Madrid).

### Cómo se ha validado (fase 2)

- Paso 1 (2026-10-14): llamadas reales a BRouter (brouter.de), Valhalla y OSRM (FOSSGIS); openrouteservice y
  GraphHopper responden 401 sin clave. Vueltas desde Madrid centro, Tres Cantos y León capital contando km por tipo
  de vía y firme con las etiquetas que devuelve BRouter (y `trace_attributes` en Valhalla). Resultados en
  `DECISIONS.md` 013. Los scripts de prueba no se han guardado en el repositorio: el cliente real llega en el paso 3.
- Paso 2 (2026-10-14): 13 tests nuevos (130 en total). Cada GPX se valida contra el esquema oficial `gpx.xsd` de
  TopoGrafix, guardado en `Fixtures/Gpx/`; un test comprueba que la validación detecta una coma decimal. Otro fuerza la
  configuración española (`es-ES`) para comprobar que los decimales salen con punto. Se escribieron con `GpxWriter` dos
  vueltas reales de BRouter del paso 1 (León 103 km, Madrid 54 km): ~2.000 puntos y ~175 KB cada una.
  Se abren bien en gpx.studio (comprobado por el autor). **Pendiente:** cargar una en el Hammerhead Karoo del autor
  (dashboard de Hammerhead → Routes → New Route → Import, y sincronizar).
- Paso 3 (2026-10-14): 20 tests nuevos (150 en total) con respuestas reales grabadas en `Fixtures/BRouter/` (vuelta de
  17,4 km en León, "target island", subida de perfil bien y con error). Comprueban la URL y el orden lon,lat de los
  puntos (también con cultura `es-ES`), la subida única del perfil, la lectura de puntos, distancia, desnivel y
  etiquetas, que los tramos cubren la ruta de punta a punta (la suma coincide al metro con la distancia de BRouter),
  la nueva subida cuando el servidor pierde el perfil, cada tipo de error y la espera entre peticiones.
  Prueba en vivo contra brouter.de, con el cliente subiendo el perfil él mismo: Madrid centro hacia el este 72,9 km,
  Tres Cantos hacia el oeste 113,6 km, León hacia el este 80,2 km, **0 km prohibidos** en las tres (sin `trunk`,
  pistas, obras ni firmes sin asfaltar). León hacia el oeste con radio 11 km da `Unreachable` ("target island").
  Las distancias siguen muy lejos de lo pedido: eso es el paso 4.
- Paso 4a (2026-10-15): 43 tests nuevos (193 en total). Casos calculados a mano (ida y vuelta, vuelta cuadrada,
  "piruleta" que sale y vuelve por la misma carretera, punto a 2 km de una recta de 10 km sin puntos intermedios,
  meridiano de 180°) y tres rutas reales nuevas grabadas en `Fixtures/BRouter/` (ver su README):

  | Ruta grabada | Km | Repetido | Prohibido | Desvío máx. de un punto |
  |---|---|---|---|---|
  | León norte, R = 3 km, estricto | 17,4 | 7 % | 0 | 0,14 km |
  | León este, R = 6 km, estricto | 79,7 | 84 % | 0 | 1,07 km |
  | León sur, R = 3 km, `fastbike` | 21,3 | 5 % | 2,53 km (pistas, una senda y 85 m de `trunk`) | 0,25 km |
  | Tres Cantos suroeste, R = 5 km, estricto | 48,1 | 74 % | 0 | 2,69 km (punto en el Monte de El Pardo) |

  Las rutas del perfil estricto dan 0 km prohibidos también con la lista ampliada de firmes. La repetición cambia menos
  de 1 punto al revisar cada 50 m o al recorrer la ruta al revés. Los GPX de León este y Tres Cantos se escribieron en
  el scratchpad para mirarlos en gpx.studio (pendiente de que el autor los vea).
- Ampliación del 4a, cuerda (2026-10-15): 4 tests nuevos y otros ampliados (197 en total). Casos a mano: ida y vuelta
  (todo cuerda), piruleta (1 km de cuerda + vuelta), vuelta cuadrada (nada), cuadrado con un ramal de 2 km en una
  esquina (todo fuera de la cuerda), track que no acaba donde empieza. Con datos reales: el globo del autor
  (`Fixtures/Rides/`, solo posiciones y altitud) repite el 50 %, con 38,9 km de cuerda y 0 % fuera de ella; las
  estrellas grabadas de León este y Tres Cantos, más del 70 % fuera de la cuerda. **Fallo corregido:** en un track
  abierto (que no acaba justo en la salida, como una salida grabada) la repetición daba siempre 0. No afectaba a las
  cifras anteriores, porque las rutas de BRouter acaban exactamente en la salida.
  Después, con una ida y vuelta pura del autor (La Bañeza, 96 km, también en `Fixtures/Rides/`): salía 48 % fuera de la
  cuerda porque el primer km de salida va por otras calles y la cuerda solo podía empezar a 1 km de casa. Ahora puede
  empezar hasta a 3 km de cada extremo: 94,9 km de cuerda y 0 % fuera de ella. Caso a mano equivalente (calles
  paralelas los 2 primeros km). 200 tests en total.
- Ramales en vez de cuerda (2026-10-15): un lazo del autor (Boadilla del Monte, 84 km: vuelta cerca de casa, cuello de
  4,6 km y vuelta de 50 km) salía con 11 % fuera de la cuerda, rechazado. El criterio pasa a ser "ramal o no" (cada
  tramo repetido se juzga por lo que hay entre sus dos pasadas; `DECISIONS.md` 015). Al pasarlo por las estrellas
  generadas salieron dos fallos más, corregidos con tests: un ramal dentro de otro ramal pasaba por vuelta (ahora solo
  cuentan los km hechos una vez) y una cuerda seguida justo de un ramal se mezclaba con él (ahora el tramo se parte
  donde salta su otra pasada). Resultado: salidas del autor (globo, ida y vuelta, lazo, dos vueltas) 0-0,6 % en
  ramales; vueltas generadas buenas 0-1,6 %; estrellas generadas 35-84 %. Casos a mano nuevos: lazo, ramal con una
  vuelta de 2 km al final, cuerda seguida de ramal. 205 tests en total.
- Globo en el centro (2026-10-15): otra salida del autor (Humanes, 21,6 km) con una ida y vuelta al punto más lejano
  colgando de una vuelta pequeña salía con 28 % en ramales. Ahora el ramal que da la vuelta en el punto más lejano de
  casa es el destino. Con un margen del 90 % dejaba pasar estrellas (León este con `trunk`: 35 % → 0 %), así que solo
  vale el punto más lejano exacto. Resultado: seis salidas del autor 0 % en ramales; estrellas generadas 24-60 %.
  Casos a mano nuevos: globo en el centro y estrella de dos brazos (solo se perdona el que llega más lejos).
  208 tests en total.

### Riesgos y pendientes (fase 2)

- **BRouter lleva cada punto de paso a la vía permitida más cercana, aunque esté lejos:** un punto en el mar a 50 km
  de Galicia dio HTTP 200 y una ruta de 446 km hasta la costa. `RouteAnalyzer` ya mide a qué distancia pasa la ruta
  de cada punto pedido (4a); el generador (4c) tiene que descartar o mover los que se alejen demasiado. La opción de BRouter para devolver los puntos (`exportWaypoints`) no sirve:
  devuelve los pedidos, no los corregidos, y con JSON mal formado.
- **HTTP 500 sin texto es ambiguo:** BRouter lo devuelve por perfil inexistente, por un solo punto y por coordenadas
  inválidas. El cliente valida los puntos antes de llamar y lo trata como perfil perdido (vuelve a subirlo una vez).
- **brouter.de sin política de uso publicada** (`DECISIONS.md` 013): si cae, no hay rutas. **Limita sin avisar:**
  el 2026-10-15, tras unas 75 peticiones en unos 10 minutos (a una por segundo, varias con URL largas), respondió
  HTTP 403 "Please, retry later!". El generador tiene que ir justo de peticiones.
- **Las vueltas en abanico salen como estrellas, no como anillos** (investigación del 2026-10-15, scripts en
  `tools/investigacion/`): con puntos geométricos, BRouter entra y sale de cada punto por el mismo ramal. Con R = 6-8 km:
  León oeste 2 % repetido (bien), León norte 48 %, León este 84 %, Tres Cantos este 77 %, Tres Cantos suroeste 74 %;
  León este y sur con R = 8 y Tres Cantos norte, "target island". Dos salidas reales del autor (Garmin) repiten 0 %.
  - Penalizar lo ya recorrido (zonas `nogos` con peso de BRouter, tramo a tramo): ayuda poco (84 → 60 %, 77 → 61 %)
    y cuesta 5 peticiones por vuelta. Descartado.
  - Mover el punto que provoca un ramal al cruce donde empieza: León norte pasa de 48 % a 0 %, pero la vuelta encoge
    (58 → 27 km). No sirve donde toda la red es un árbol.
  - La prohibición de `trunk` pesa: en España muchas nacionales de un carril por sentido están etiquetadas `trunk`.
    León este (R = 6) con `trunk` permitido: 33 km, 35 % repetido, 3,6 km por `trunk` (frente a 80 km y 84 %).
    Un solo caso antes del bloqueo de brouter.de.
  - Algunos rumbos no tienen vuelta posible (Tres Cantos hacia El Pardo): hay que poder girar el rumbo.

## Fase 1

## Qué funciona

- Solución .NET 10 (`VientoHud.slnx`) con tres proyectos:
  - `src/VientoHud.Core`: tipos `Coordinate`, `WindSample`, `WindForecast`, `LocationFix`; interfaces `IWindForecastProvider`
    e `ILocationProvider`; cliente `OpenMeteoWindForecastProvider`; `Compass` (grados a 16 rumbos); regla `StartDirection`;
    `IWindObservationSource` con cliente `IemWindObservationSource` (METAR); `IPastWindForecastSource` con cliente
    `OpenMeteoPastWindForecastSource`; cálculo del error `WindVerification` y orquestación `ForecastJudge`.
  - `src/VientoHud.Console` (ejecutable `vientohud`): comandos `wind` y `verify`.
  - `tests/VientoHud.Core.Tests` (xUnit, 117 tests).
- `global.json` fija el SDK 10.0.x (en la máquina hay una preview de .NET 11 que, si no, se usaría por defecto).
- `Directory.Build.props` con ajustes comunes: `net10.0`, nullable, implicit usings y warnings como errores.
- Repositorio git inicializado con `.gitignore` estándar de .NET.
- **Cliente Open-Meteo:** pide un rango de horas en UTC con varios modelos en una sola llamada
  (por defecto AROME France y luego `best_match`), un modelo por llamada y solo mientras falten horas.
  Cada hora lleva el modelo del que viene en `WindSample.Model`: cuando AROME se acaba (unas 48 h) o el sitio
  está fuera de su dominio (p. ej. Tarifa), sigue con `best_match`.
  Errores de red, timeout, HTTP de error y JSON inválido acaban en `WindForecastException`.
- **Proveedor falso** `FakeWindForecastProvider` en el proyecto de tests, para probar código que consume previsiones.
- **Comando `wind`:** `dotnet run --project src/VientoHud.Console -- wind [--lat <grados> --lon <grados>] [--hours <1-168>]`.
  Sin `--lat/--lon` usa la ubicación de Windows. Tabla por horas en hora local: viento, racha, de dónde viene
  (16 rumbos en español y grados) y modelo. Termina con la atribución de Open-Meteo.
  Al final, la recomendación de sentido de salida (ver `DECISIONS.md` 007).
  Códigos de salida: 0 bien, 1 error de previsión o ubicación, 2 argumentos incorrectos, 130 cancelado.
- **Comando `verify`:** `vientohud verify [--lat --lon] [--days 30] [--until aaaa-mm-dd] [--stations 5] [--radius 100]
  [--models a,b,c]`. Busca las estaciones METAR más cercanas, descarga observaciones y previsiones pasadas
  (corto plazo y 24 h) y muestra el error por modelo, en total y con viento, y por estación. Unos segundos.

## Qué falta (fase 1)

- [x] Crear la solución .NET 10: `VientoHud.Core`, `VientoHud.Console`, `VientoHud.Core.Tests`.
- [x] Elegir el proveedor de previsión: **Open-Meteo**, modelo AROME France (ver `DECISIONS.md` 005).
- [x] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [x] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [x] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [x] Juez: previsión vs observación, al menos 5 días. Tabla de error aquí. Dividido en:
  - [x] 6a. Observaciones: METAR de IEM, estaciones más cercanas a cualquier punto del mundo (`DECISIONS.md` 009).
  - [x] 6b. Previsiones pasadas de Open-Meteo: a 0-3 h (Historical Forecast API) y a 24 h (Previous Runs API),
    para AROME, `best_match` y ECMWF (`DECISIONS.md` 010).
  - [x] 6c. Cálculo del error en Core: emparejar previsión y observación por estación y hora; error medio de
    velocidad (absoluto y con signo), error angular de dirección, % de horas con la dirección dentro de ±45°. Por separado con viento
    observado flojo (< 12 km/h) y con viento (`DECISIONS.md` 011).
  - [x] 6d. Comando `verify`; ejecutado 90 días en 8 zonas y 365 días en Madrid y León. AROME sigue por delante
    de `best_match` (`DECISIONS.md` 012).
  - [ ] Más adelante: AEMET como complemento para España cuando llegue la clave.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 117/117 superados.
- Paso 2: llamadas reales a Open-Meteo para Madrid (40.4168, -3.7038). AROME France, AROME France HD,
  ICON-EU, ECMWF IFS y `best_match` devuelven 48/48 horas con velocidad, dirección y rachas.
  ICON-D2 y HARMONIE (KNMI, DMI) responden "No data is available for this location".
  La Historical Forecast API devuelve previsiones pasadas de AROME (útil para el juez).
  AEMET sin clave responde 200 con cuerpo vacío.
- Paso 3: fixtures reales grabadas el 2026-10-14 en `tests/VientoHud.Core.Tests/Fixtures/OpenMeteo/`:
  respuesta de un solo modelo (los campos no llevan sufijo), error de latitud (HTTP 400) y "The service is overloaded" (salió de verdad
  al grabar; no se guardó el código HTTP, el test asume 503). Los tests comprueban la URL, los valores leídos,
  el cambio de modelo hora a hora y cada tipo de error. Aún no se ha llamado al cliente contra la API real
  desde código: se verá con el comando `wind` del paso 4.
- Paso 4 (2026-10-14, 15:30): `wind` con la ubicación del dispositivo (Windows la dio con ±149 m) y con
  coordenadas de Madrid devuelve datos reales. Con `--hours 72` salen 48 horas de AROME y 24 de `best_match`.
  Probados argumentos erróneos (lat sin lon, latitud 95, `--hours 0`, opción desconocida) y coma decimal.
  El formateo de la tabla y el análisis de argumentos no tienen tests automáticos: viven en la consola,
  que no tiene proyecto de tests.
- Paso 5 (2026-10-14, ~16:00): la regla se probó con datos reales. Madrid 6 h: viento flojo (media 4,5 km/h).
  Tarifa 6 h: "empieza hacia el E", levante medio de 33 km/h, todo de `best_match` porque AROME no cubre Tarifa.
  Madrid 72 h: viento variable. Al probar Tarifa salió que pedir varios modelos en una llamada era ambiguo:
  se cambió el cliente a un modelo por llamada y se grabaron fixtures nuevas por modelo.
  Un test destapó que con vientos opuestos el redondeo dejaba una dirección inventada; corregido con tolerancia.
- Paso 6a (2026-10-14): prueba en vivo contra IEM. Estaciones a menos de 100 km: Madrid → LEVS 8 km, LEGT 13 km,
  LEMD 14 km, LETO 23 km, LECV 32 km; León → solo LELN 7 km; Tarifa → Algeciras, Gibraltar, Ceuta, Tánger,
  Tetuán; Nebraska → 5 aeródromos; mitad del Atlántico → ninguna. Observaciones de LEMD y LELN del 1 al 3 de octubre:
  196 registros, con VRB, calma y rachas bien interpretados. Fixtures reales en `Fixtures/Iem/`.
- Paso 6b (2026-10-14): prueba en vivo de los dos archivos de Open-Meteo con AROME en Barajas y León, 1-2 de octubre:
  48 horas por estación y plazo. Tarifa sale "sin cobertura" tras repetir la petición por estación.
  Primer vistazo a ojo (1 oct 00:00 UTC): Barajas observado 340° 7 km/h, AROME corto plazo 310° 11 km/h,
  un día antes 324° 6 km/h; León observado 350° 7 km/h, corto plazo 340° 8 km/h, un día antes 296° 17 km/h.
  Sin vueltas de 180° ni desfases evidentes. Se sacó a `OpenMeteoApi` lo común con el cliente de previsión;
  `wind` sigue funcionando en vivo (León y Tarifa).
- Paso 6c (2026-10-14): tests con casos calculados a mano y con los datos grabados de Barajas y León (1-2 oct):
  96 horas emparejadas, todas a la hora exacta; si se invierte la dirección prevista el error de dirección
  empeora más de 60°, así que no hay confusión "desde"/"hacia". Prueba en vivo con AROME, `best_match` y
  `ecmwf_ifs` a los dos plazos: errores de 2-4 km/h y 30-40°, peores a 24 h que a corto plazo. Solo 3 horas
  con viento ≥ 12 km/h en esos dos días: confirma que el juez necesita muchos más días. La prueba destapó que
  ECMWF IFS no archiva rachas a 24 h y esas horas se perdían; ahora la racha es opcional.
- Paso 6d (2026-10-14): `ForecastJudge` probado con fuentes falsas (orden de veredictos, estaciones sin cobertura,
  sin estaciones, periodo vacío). `verify` probado en vivo: errores de argumentos, 30 días en Madrid (5,7 s),
  90 días en 8 zonas, 365 días en Madrid y León. Los resultados están en la sección "Juez del viento".
  El formateo de `verify` no tiene tests automáticos (la consola no tiene proyecto de tests).

## Riesgos y pendientes detectados

- **Juez con AEMET (complemento):** su observación horaria solo cubre las últimas 12 horas; habrá que ir
  guardándola durante días. Útil sobre todo donde no hay aeródromos (montaña de León).
- **Clave de AEMET:** pedida el 2026-10-14; el enlace de confirmación respondió 204 (éxito, sin contenido).
  Falta el correo con la clave. Solo hace falta para el complemento de AEMET.
  Es dudoso que llegue. **Riesgo para la fase 4:** el plan parte del radar de AEMET, que también pide clave;
  si no llega, habrá que buscar otra fuente de radar en esa fase (licencia, cobertura, clave).
- **Elegir el modelo por zona:** el juez muestra que el mejor modelo cambia según la zona (León, Navarra y
  A Coruña frente al centro y el este). La app podría ejecutar el juez en las estaciones cercanas al usuario
  y elegir el modelo allí. No es de la fase 1; anotado como mejora posible.
- **Pocas estaciones METAR fuera de las ciudades:** en León solo LELN en 100 km. El juez dirá qué modelo acierta
  más en zonas llanas y abiertas; en valles y montaña la comparación será menos representativa.

## Juez del viento (fase 1)

Ejecutado el 2026-10-14 con `verify`, 90 días (16 jul – 13 oct 2026), estaciones METAR más cercanas
(salida completa en `docs/verificacion/`). Métricas definidas en `DECISIONS.md` 011.

**Con viento observado ≥ 12 km/h, previsión a corto plazo.** Error medio de velocidad en km/h (sesgo entre
paréntesis; negativo = el modelo se queda corto) y % de horas con la dirección dentro de ±45°:

| Zona (estaciones) | Horas | AROME | best_match | ECMWF IFS |
|---|---|---|---|---|
| Madrid (LEVS, LEGT, LEMD, LETO, LECV) | 2.599 | **3,9** (−0,9) 95 % | 6,7 (−5,8) 95 % | 5,7 (−5,2) 94 % |
| León (LELN) | 430 | 4,0 (−0,5) 89 % | **3,4** (−0,6) 93 % | 5,0 (−3,9) 86 % |
| Navarra (LEPP, LESO, LELO) | 1.811 | 3,7 (−1,2) 94 % | **3,3** (−1,1) 98 % | 5,3 (−4,1) 88 % |
| Zaragoza (LEZG, LEHC) | 1.894 | **3,6** (0,0) 96 % | 5,2 (−4,7) 96 % | 5,6 (−5,1) 93 % |
| Burgos (LEBG) | 1.216 | **3,6** (−2,1) 97 % | 3,7 (−2,1) 95 % | 3,8 (−1,6) 96 % |
| Sevilla (LEZL, LEMO, LEEC) | 1.537 | no cubre | **4,2** (−3,4) 96 % | 4,5 (−4,0) 95 % |
| Valencia (LEVC, LEBT) | 1.234 | **3,3** (−2,0) 96 % | 4,8 (−4,1) 96 % | 5,3 (−4,8) 93 % |
| A Coruña (LECO, LEST) | 1.108 | 4,2 (−3,4) 96 % | 3,6 (−2,8) 91 % | **3,0** (−1,5) 91 % |

**Con viento, previsión del día anterior (24 h)**, error medio de velocidad: Madrid 4,5 / 6,6 / 5,8;
León 5,4 / 5,3 / 5,4; Navarra 4,5 / 4,8 / 5,5; Zaragoza 4,7 / 5,5 / 5,8; Burgos 4,3 / 4,1 / 3,9;
Sevilla — / 4,5 / 4,8; Valencia 3,8 / 4,9 / 5,5; A Coruña 4,2 / 3,7 / 3,2 (AROME / best_match / ECMWF).
Dirección dentro de ±45°: 81-95 %.

**Todas las horas** (la mayoría con viento flojo): error medio de velocidad 2,5-4,7 km/h y de dirección 22-43°
según zona, modelo y plazo. Con viento flojo la dirección vale poco en cualquier modelo.

**Un año** (14 oct 2025 – 13 oct 2026), con viento, corto plazo: Madrid AROME 4,3 km/h (−1,0) frente a
`best_match` 7,7 (−7,3), 10.600 horas; León AROME 3,9 frente a `best_match` 3,1, 1.467 horas.
Confirma que el resultado no es cosa del verano.

**AROME HD** (1,5 km), 90 días, con viento, corto plazo: Madrid 3,8, León 4,3, Navarra 3,7, A Coruña 3,6 km/h;
igual que AROME salvo en A Coruña.

**Conclusiones** (`DECISIONS.md` 012):
- Con viento de verdad, la dirección prevista es fiable (≈ 95 % dentro de ±45° a corto plazo): la regla
  "empieza hacia X" se apoya en algo sólido cuando sopla.
- Los modelos globales se quedan cortos de velocidad 4-7 km/h en el centro y el este. AROME casi no tiene sesgo.
- Se mantiene AROME → `best_match`.

**Cautelas:** son aeródromos (llano y despejado); el archivo de AROME a 24 h solo cubre ~90 días, por eso tiene
menos horas a ese plazo; León tiene una sola estación.
