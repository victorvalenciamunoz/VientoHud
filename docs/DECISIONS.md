# Decisiones

Formato: contexto → decisión → alternativas descartadas.

## 001 — Sin backend propio
- Contexto: previsión, radar, mapas y rutas están disponibles como datos o servicios públicos.
- Decisión: no montamos servidor ni base de datos; consumimos servicios externos detrás de interfaces
  y cacheamos en fichero local cuando haga falta.
- Alternativas: API propia (descartada de momento: complejidad sin beneficio claro).

## 002 — El GPX es la salida principal
- Contexto: en bici no se lleva el móvil con la pantalla encendida ni se usan auriculares.
- Decisión: toda la información útil durante la ruta va en el GPX para el GPS de la bici;
  la app solo se mira antes de salir.

## 003 — Solo bici de carretera
- Contexto: las rutas por caminos y pistas son más difíciles de generar y validar
  (estado del firme, datos de OpenStreetMap menos fiables, ciclabilidad variable).
- Decisión: solo carretera asfaltada. Evitar autopistas y autovías; preferir secundarias de poco tráfico.
- Alternativas: perfil configurable carretera/gravel/MTB (descartado: multiplica la complejidad
  sin aportar al caso de uso real).

## 004 — SDK fijado con global.json
- Contexto: en la máquina de desarrollo conviven varios SDK, entre ellos una preview de .NET 11
  que `dotnet` elegiría por defecto.
- Decisión: `global.json` con SDK 10.0.401, `rollForward: latestFeature` y sin previews.
  Ajustes comunes de compilación en `Directory.Build.props` (warnings como errores).
- Alternativas: no fijar el SDK (descartado: compilaciones distintas según la máquina).

## 005 — Proveedor de previsión de viento: Open-Meteo
- Contexto: la fase 1 necesita viento previsto por horas (velocidad, dirección, rachas) para una coordenada.
  Candidatos: Open-Meteo y AEMET OpenData. Comprobado el 2026-10-14 contra la documentación oficial
  y con llamadas reales.
- Comparación:

  | | Open-Meteo | AEMET OpenData |
  |---|---|---|
  | Clave | No (plan gratuito) | Sí, gratuita, en cabecera `api_key`. Sin ella responde 200 con cuerpo vacío |
  | Entrada | Lat/lon cualquiera | Código INE de municipio (hay que mapear coordenada → municipio) |
  | Dirección | Grados (`wind_direction_10m`) | 8 rumbos (N, NE, E… y calma) |
  | Rachas | `wind_gusts_10m` cada hora (máx. de la hora anterior) | Solo si viento ≥ 20 km/h y racha > viento + 10 |
  | Resolución | AROME France 2,5 km y HD 1,5 km cubren Madrid; también ICON-EU y ECMWF IFS | Predicción municipal (sin resolución publicada) |
  | Alcance | AROME 2 días; `best_match` hasta 16 días | Horaria hasta 48 h |
  | Formato | JSON en una llamada, UTC con `timezone=GMT` | Dos llamadas (URL de datos + datos), ISO-8859-1 |
  | Límites | 600/min, 5.000/h, 10.000/día, 300.000/mes | Límite por minuto (429) sin cifra publicada |
  | Licencia | CC BY 4.0, atribución "Weather data by Open-Meteo.com" | Reutilización citando a AEMET como autor |
  | Uso | Gratis solo no comercial (sin anuncios ni suscripciones) | Libre |
  | Pasado | `past_days` (hasta 92) y Historical Forecast API con previsiones archivadas | No guarda previsiones pasadas |

- Decisión: Open-Meteo como proveedor de previsión, detrás de una interfaz.
  Modelo por defecto `meteofrance_arome_france` (2,5 km, cubre Madrid, rachas por hora),
  con `best_match` como alternativa para las horas que AROME no cubre (más allá de ~48 h) y para los sitios
  fuera de su dominio (p. ej. Tarifa). Se pide un modelo por llamada y solo mientras falten horas: si se piden
  varios en una llamada y alguno no cubre el sitio, Open-Meteo lo descarta en silencio y devuelve los campos sin
  sufijo, y ya no se sabe de qué modelo es cada dato (descubierto en el paso 5). El modelo es configurable:
  el juez (paso 6) decidirá con datos cuál acierta más en la zona.
  Atribución a Open-Meteo en la salida de la consola y, en el futuro, en la app.
- Alternativas descartadas:
  - AEMET como previsión principal: 8 rumbos son poca precisión para calcular viento de cara o de lado
    en cada tramo, las rachas no vienen siempre y exige clave y traducir coordenada a municipio.
  - Probados y sin datos en Madrid: `icon_d2`, `knmi_harmonie_arome_europe`, `dmi_harmonie_arome_europe`.
- Consecuencias:
  - AEMET sigue haciendo falta como juez: es la fuente de observaciones de estaciones (requiere clave).
  - El plan gratuito de Open-Meteo es no comercial. Si la app llegara a tener anuncios o suscripciones,
    habría que pasar a un plan de pago.

## 006 — Ubicación en la consola: servicio de ubicación de Windows
- Contexto: la ubicación debe salir del dispositivo (cero entrada manual). La consola corre en Windows.
- Decisión: interfaz `ILocationProvider` en Core. La consola la implementa con `Windows.Devices.Geolocation`
  (usa GPS, Wi-Fi o IP según lo que tenga Windows) y por eso apunta a `net10.0-windows10.0.19041.0`.
  Core sigue en `net10.0` sin dependencias de plataforma. `--lat/--lon` quedan para probar otras ubicaciones.
  La app MAUI aportará su propia implementación con el GPS del móvil.
- Alternativas: geolocalización por IP con un servicio externo (descartada: menos precisa y otra dependencia
  externa); exigir siempre `--lat/--lon` (descartada: va contra el principio de cero entrada manual).
- Consecuencia: la consola solo compila y corre en Windows. El SDK descarga el paquete de referencia
  `Microsoft.Windows.SDK.NET.Ref` (de Microsoft) como parte del framework de destino; no es un `PackageReference` nuestro.

## 007 — Regla "empieza hacia X"
- Contexto: la fase 1 pide una regla básica para el sentido de salida, contra el viento medio de las próximas horas.
- Decisión: media vectorial del viento de las horas de la salida (cada hora pesa según su velocidad;
  350° y 10° dan 0°, no 180°). Se sale hacia la dirección de la que viene el viento medio: ida de cara,
  vuelta a favor. Dos casos sin recomendación:
  - Viento flojo: velocidad media < 5 km/h (tope de fuerza 1 Beaufort, poco frente a los 25-30 km/h de la bici).
  - Viento variable: constancia (velocidad vectorial / velocidad media) < 0,7, más o menos direcciones repartidas
    en ±80° alrededor de la media.
  Los umbrales son configurables (`StartDirectionOptions`) y se afinarán con datos en la fase 3.
- Alternativas: media aritmética de grados (descartada: falla al cruzar el norte); dirección de la hora más
  ventosa (descartada: ignora el resto de la salida); ponderar más la segunda mitad de la ruta (se deja
  para la fase 3, cuando se sepa a qué hora se pasa por cada tramo).

## 008 — Alcance global; Madrid y León como referencia
- Contexto: el autor sale sobre todo por Madrid y por la provincia de León, pero quiere que la app sirva en cualquier sitio.
- Decisión: nada en Core puede depender de una región. Madrid y León son los casos de referencia para probar
  y validar, no el alcance. Consecuencias:
  - Las fuentes principales deben ser globales: Open-Meteo para la previsión y METAR (archivo mundial de IEM)
    para el juez, con las estaciones más cercanas a la ubicación, no una lista fija.
  - AEMET es un complemento para España (más estaciones en zonas sin aeródromos, como la montaña de León),
    no una dependencia.
  - Los modelos regionales (AROME) solo se mantienen por delante de `best_match` si el juez demuestra que aciertan
    más; si no, `best_match` (que ya elige el mejor modelo disponible en cada sitio) es la opción más sencilla
    para una app global.
- Alternativas: centrarse en España con AEMET como fuente principal (descartada: limita la app sin necesidad).

## 009 — Observaciones para el juez: METAR del archivo de IEM
- Contexto: el juez necesita viento observado por horas en días pasados, en cualquier parte del mundo (DECISIONS 008).
  AEMET solo da las últimas 12 horas, solo en España y con clave (pedida, aún sin llegar).
- Decisión: archivo METAR de Iowa Environmental Mesonet (Iowa State University), detrás de `IWindObservationSource`.
  Verificado el 2026-10-14:
  - Licencia: dominio público ("may be used freely by anyone for any lawful purpose"); agradecen la atribución.
  - Sin clave. Límite: una petición por segundo y por IP (429 si se supera); 422 si se piden más de
    1.000 estaciones-año de golpe.
  - Cobertura mundial de aeródromos, con historia de décadas. METAR cada 30-60 min, viento medio de 10 min
    a 10 m, dirección respecto al norte geográfico redondeada a 10°, velocidad en nudos.
  - Búsqueda de estaciones cercanas: IEM no la tiene; se descarga la lista de redes (una por país o estado de EE. UU.),
    se eligen las que tienen el rectángulo cerca del punto y se calcula la distancia a cada estación.
- Limitaciones asumidas:
  - Son aeródromos: zonas llanas y despejadas, no valles ni puertos. En León solo hay una estación en 100 km (LELN).
  - Rachas solo cuando son significativas; el modelo da la racha máxima de la hora. No son comparables directamente.
  - Calma y viento variable (VRB) no tienen dirección.
  - Algunas redes tienen el rectángulo inflado por estaciones mal asignadas (Austria incluye Puerto España,
    en Trinidad), lo que provoca descargas de más, sin afectar al resultado.
- Alternativas: AEMET OpenData (se añadirá como complemento para España cuando llegue la clave, guardando
  observaciones con el tiempo); NOAA ISD (más pesado de descargar y procesar).

## 010 — Previsiones pasadas para el juez: archivos de Open-Meteo, dos plazos
- Contexto: para juzgar un modelo hay que saber qué previó para horas que ya pasaron, y a qué plazo.
- Decisión: dos plazos, detrás de `IPastWindForecastSource`:
  - **Corto plazo** (`ShortRange`), Historical Forecast API: las primeras horas de cada pasada del modelo cosidas.
    Es la previsión a pocas horas vista (dentro del intervalo entre pasadas): el caso "salgo ahora".
  - **Un día antes** (`DayAhead`), Previous Runs API con `_previous_day1`: lo que se previó 24 h antes.
    El caso "mañana salgo".
  Verificado el 2026-10-14: ambas aceptan AROME, `best_match`, `ecmwf_ifs` (9 km) y `ecmwf_ifs025`, con rachas,
  archivo desde enero de 2024 (antes devuelven `null`), y varias ubicaciones en una sola llamada.
  Si el modelo no cubre alguna de las ubicaciones, Open-Meteo rechaza la llamada entera; entonces se repite
  ubicación por ubicación y las no cubiertas quedan marcadas (sin celda ni horas), sin error.
  ECMWF IFS no archiva rachas a un día vista: la racha es opcional en `WindSample` (la consola muestra "—").
- Alternativas: Single Runs API (cada pasada completa; más fiel a un plazo exacto, pero muchas más llamadas);
  guardar nosotros las previsiones cada día (descartado: el archivo ya existe y llega a 2024).

## 011 — Cómo mide el juez el error del viento
- Contexto: hay que comparar previsión (horaria, en punto) con METAR (cada 30-60 min más avisos especiales).
- Decisión (`WindVerification`):
  - **Emparejar:** cada hora prevista con la observación de esa estación más cercana en el tiempo, como mucho
    a ±15 min (en empate, la anterior). Las horas sin observación cercana no cuentan.
  - **Velocidad:** sesgo (media de previsto − observado; negativo = el modelo se queda corto) y error absoluto medio.
  - **Dirección:** solo cuando la observación tiene dirección (fuera calma y VRB). Ángulo más corto entre ambas
    (0-180°), su media y el porcentaje de horas dentro de ±45°.
  - **Por viento observado:** todas las horas, flojo (< 12 km/h) y con viento (≥ 12 km/h, fuerza 3 Beaufort).
    Lo que importa para la bici es el segundo grupo. Separar por lo observado sesga el error de velocidad de cada
    grupo, así que se comparan modelos dentro de un mismo grupo, no grupos entre sí.
  - **Rachas:** fuera, no son comparables (DECISIONS 009).
- Alternativas: interpolar la observación a la hora en punto (descartado: el METAR en punto existe casi siempre);
  RMSE en lugar de error absoluto medio (penaliza mucho los fallos grandes aislados; se puede añadir si hace falta);
  separar por viento previsto (mide otra cosa: si fiarse de la previsión cuando dice que hará viento).

## 012 — El juez confirma AROME primero y `best_match` después
- Contexto: DECISIONS 005 eligió AROME por resolución, sin datos; DECISIONS 008 pedía mantenerlo solo si el juez
  demostraba que acierta más. Juez ejecutado el 2026-10-14 (`verify`, 90 días en 8 zonas de España y 365 días en
  Madrid y León; resultados en `docs/verificacion/` y tabla en `STATUS.md`).
- Resultado, en horas con viento observado ≥ 12 km/h (las que importan en bici):
  - La **dirección** la aciertan todos los modelos parecido: 89-98 % de horas dentro de ±45° a corto plazo.
  - La **velocidad** es lo que los separa. Los modelos globales (`best_match` en el centro y el este, ECMWF)
    se quedan cortos de forma sistemática, 4-7 km/h. AROME tiene sesgo pequeño (−2 a 0 km/h, salvo A Coruña −3,4).
  - AROME gana con claridad en Madrid (3,9 frente a 6,7 km/h de error medio; en un año 4,3 frente a 7,7),
    Zaragoza (3,6 frente a 5,2) y Valencia (3,3 frente a 4,8). Empata en Burgos. Pierde por poco en León (4,0
    frente a 3,4; en un año 3,9 frente a 3,1) y Navarra (3,7 frente a 3,3), y en A Coruña gana ECMWF (3,0 frente a 4,2).
  - AROME HD (1,5 km) no mejora a AROME de forma apreciable salvo en A Coruña.
  - AROME no cubre Sevilla; ahí `best_match` (4,2 km/h) va bien.
- Decisión: se mantiene el orden AROME → `best_match`. Donde AROME pierde, pierde por menos de 1 km/h;
  donde gana, gana por 1,5-3,4 km/h, y fuera de su dominio `best_match` cubre el resto del mundo.
- Alternativas: `best_match` solo (descartado: en Madrid falla casi el doble con viento); elegir el modelo por
  zona con el propio juez (posible mejora futura: la app podría ejecutar el juez en las estaciones cercanas
  al usuario y quedarse con el modelo que mejor acierta allí).
- Limitaciones: estaciones en aeródromos; el archivo de AROME a 24 h solo cubre ~90 días; no se sabe qué modelo
  usa `best_match` en cada zona (en el norte acierta más de lo que explicaría ECMWF y no es AROME HD).

## 013 — Motor de rutas: BRouter con perfil estricto y puntos de paso propios
- Contexto: la fase 2 necesita rutas circulares de bici de carretera en cualquier sitio del mundo (DECISIONS 008):
  solo asfalto, sin autovías (DECISIONS 003), sin backend propio (DECISIONS 001). Investigado el 2026-10-14 en las
  fuentes oficiales y con llamadas reales: vueltas de ~60-100 km desde Madrid centro, Tres Cantos y León capital,
  contando los km por tipo de vía (`highway`) y firme (`surface`) que devuelve el propio motor.
- Comparación:

  | | BRouter | openrouteservice | GraphHopper | Valhalla (FOSSGIS) | OSRM | Motor propio (Itinero) |
  |---|---|---|---|---|---|---|
  | Clave | No | Sí, gratis | Sí, gratis | No | No | — |
  | Límites | Ninguno publicado (servidor de buena voluntad) | 2.000/día, 40/min | 500 créditos/día; vuelta circular = 2 | 1 petición/s; comercial solo si no es esencial; no fijar la URL en apps | 1 petición/s | — |
  | Vuelta circular | Sí (`engineMode=4`, con rumbo) | Sí, máx. 100 km | No en el plan gratuito (exige modo flexible) | No | No (`trip` solo ordena puntos) | No |
  | Bici de carretera | Perfiles de serie y **perfil propio subido al servidor** | `cycling-road`: permite `trunk`, no excluye la tierra; sin `custom_model` en la API pública | `racingbike` y `custom_model` solo de pago; gratuito no comercial | `bicycle_type=Road`, sin reglas a medida | Solo bici urbana | Por hacer |
  | Tipo de vía y firme por tramo | Sí, etiquetas OSM (`WayTags`) | Sí (`extra_info`) | Sí (`details`) | Sí, con `trace_attributes` | No | — |
  | Software | MIT, 1.7.10 (jul 2026) | GPL-3; resultados CC BY-SA | Apache-2.0 | MIT, 3.9.1 (oct 2026) | BSD | Itinero 2 en alfa, no publicado en NuGet |
  | Android sin conexión | Sí: la app BRouter ofrece servicio a otras apps (AIDL) | No | No | No | No | Mucho trabajo |

- Pruebas reales:
  - BRouter con los perfiles de serie (`fastbike`, `fastbike-lowtraffic`, `fastbike-verylowtraffic`,
    `racebike-verylowtraffic`): vueltas en 0,5-5 s, pero con 5-17 km de pistas de tierra y tramos cortos de `trunk`.
    Sus perfiles solo penalizan lo no asfaltado; no lo prohíben.
  - BRouter con un perfil estricto derivado de `fastbike` (prohíbe `trunk`, firmes declarados sin asfaltar y pistas,
    caminos y sendas salvo que estén declarados asfaltados): **0 km dudosos** en todas las vueltas. Con destinos a los
    que solo se llega por tierra no la pisa: se para en la carretera asfaltada más cercana (a 1,8-2,6 km del punto).
    `fastbike` metía 2,9-4,5 km de pista para esos mismos destinos.
  - El modo circular del servidor falla con perfiles propios (error interno "dummy.brf"), pero las rutas normales
    funcionan; por eso los puntos de paso los calculamos nosotros.
  - Valhalla `Road`: poca tierra (1-2 km) pero 8,5 km de `trunk` en León, sin forma de prohibirlo.
  - openrouteservice y GraphHopper: 401 sin clave (no se pidió clave).
  - Con cualquier motor, poner los puntos de paso en un círculo no basta: con el mismo radio salieron vueltas de
    54 a 114 km, y en León hacia el este la ruta repetía la mitad del recorrido (puntos en mitad del campo).
    A veces un punto cae junto a un trozo de vía aislado y el motor responde "target island detected".
- Decisión: enfoque mixto.
  - **Core genera los puntos de paso** (rumbo y radio según la distancia), ajusta la distancia con unas pocas
    llamadas, mide la repetición y el desvío de cada punto, reintenta los "islotes" y descarta las candidatas con
    tramos prohibidos. Así controlamos el rumbo de salida, que la fase 3 necesita para el viento.
  - **BRouter une los puntos** con nuestro perfil estricto, guardado en el repositorio y subido al servidor
    (`POST /brouter/profile`) en cada sesión, detrás de una interfaz en Core.
  - Para la consola se usa el servidor público brouter.de con mesura: como mucho una petición por segundo y
    User-Agent propio.
  - El juez de la fase 2 comprueba las etiquetas OSM de cada tramo; el GPX lo escribimos nosotros, no el motor.
  - Perfil (`src/VientoHud.Core/Routing/BRouter/road-strict.brf`, paso 3): `trunk` prohibido en todo el mundo, no solo
    en España. En algunos países es una carretera normal y allí habrá más rodeos o rutas imposibles; se acepta antes que
    arriesgarse a meter una autovía. También prohíbe carreteras en obras, escaleras y ferris. BRouter descarta cualquier
    tramo con coste mayor de 9998, así que el tope de 9999 del perfil sigue siendo una prohibición.
    **Cambiado por DECISIONS 016:** `trunk` permitido con coste 3.
- Alternativas:
  - openrouteservice: la mejor opción con clave, pero vueltas de 100 km como máximo, no prohíbe `trunk` ni la tierra,
    y sus resultados quedan bajo CC BY-SA.
  - GraphHopper de pago: perfiles a medida y vuelta circular con rumbo; candidato si algún día se paga un servicio.
  - Geoapify (revisado solo en documentación, por terceros): clave gratuita sin tarjeta, 3.000 créditos/día, uso
    comercial; sus "evitar" son blandos, habría que validar y descartar después. Reserva si BRouter falla.
  - Valhalla y OSRM de FOSSGIS: condiciones de uso poco aptas para una app y sin forma de prohibir `trunk`
    (Valhalla) o sin perfil de carretera (OSRM).
  - Motor propio (Itinero 2, Routino): mucho trabajo, alojarlo nosotros y sin ventaja sobre el perfil de BRouter.
  - Descartados por documentación: Mapbox (`exclude=unpaved` solo para coche), HERE (bici en beta sobre la red
    peatonal), TomTom (evitar blando), CycleStreets (centrado en Reino Unido, sin excluir tierra), RouteYou
    (comercial, condiciones desconocidas), Mapsource (programa de escritorio de Garmin, sin API).
- Riesgos:
  - brouter.de no publica política de uso ni garantías. Si cae, la consola no puede generar rutas.
    No se sabe cuánto conserva los perfiles subidos (por eso se suben en cada sesión).
  - Ningún perfil detecta una pista que OpenStreetMap no tenga etiquetada como tal.
- Para la fase 6 (se decidirá entonces): BRouter en el propio móvil (la app BRouter como servicio, sin conexión y
  sin depender de un servidor ajeno) u OsmAnd, revisando su licencia (GPL).

## 014 — Análisis de una ruta: tramos prohibidos, caminos asfaltados, repetición y desvío de los puntos
- Contexto: el generador de vueltas (fase 2, paso 4) tiene que descartar candidatas sin pedir nada más a la red, y el
  juez del paso 6 tiene que contar los km por tipo de vía y firme. Con las etiquetas OSM que BRouter da en cada tramo
  basta; no hace falta Overpass (que además estaba saturado).
- Decisión (`RoadRules`, `RouteAnalyzer` en `VientoHud.Core.Routing`):
  - **Cuatro categorías por tramo:** carretera; **camino asfaltado** (`track`, `path`, `footway` o `road` con firme
    asfaltado, hormigón, adoquín o `smoothness` buena); **prohibido**; **desconocido** (el motor no dio tipo de vía).
  - **Prohibido:** `motorway` y sus enlaces, obras (`construction`, `proposed`, `abandoned`), escaleras,
    `bridleway`, ferris; cualquier vía con firme sin asfaltar; pistas y sendas sin firme asfaltado declarado. Sigue las
    reglas del perfil estricto, pero con una lista más larga de firmes sin asfaltar (p. ej. `earth`, `mud`, `rock`):
    es una comprobación independiente que puede pillar huecos del perfil. `trunk` estaba en esta lista hasta
    DECISIONS 016; ahora tiene categoría propia (permitido, contado aparte).
  - **Caminos asfaltados permitidos, contados aparte** (decisión del autor, 2026-10-15). Muchos caminos de concentración
    parcelaria de León están asfaltados; unos están bien y otros rotos. Se permiten, como hace el perfil, y el juez
    enseña sus km. Si resultan malos, se prohibirán en el perfil y aquí.
  - **Desvío de cada punto de paso:** distancia más corta del punto pedido a la línea del track (a los segmentos entre
    puntos consecutivos, no solo a los puntos, que en una recta pueden estar a cientos de metros).
  - **Repetición:** se revisa un punto cada 100 m; está repetido si otra parte del track pasa a menos de 30 m y está a
    más de 1 km por el recorrido. Cuentan las dos pasadas (ida y vuelta por la misma carretera = 100 %). La distancia se
    mide a la línea del track, no a otros puntos revisados: con muestras cada 100 m, dos pasadas por la misma carretera
    tienen las muestras desfasadas hasta 50 m y con 30 m de umbral se escaparía cerca del 40 % de lo repetido. En una
    vuelta cerrada, la separación por el recorrido se mide en círculo: llegar a casa no es repetir la salida, pero salir y
    volver por la misma carretera sí cuenta (salvo los 500 m más cercanos a casa).
  - Geometría en un plano local (proyección equirrectangular centrada en la salida): error de ~1,5 % a 100 km en latitud
    a 42°, irrelevante para umbrales de decenas de metros. Funciona al cruzar el meridiano de 180°.
- Resultado con rutas reales grabadas: León hacia el este (79,7 km) 84 % repetido; Tres Cantos hacia el suroeste
  (48,1 km) 74 %; León hacia el norte (17,4 km, ciudad) 7 %. Cambiar el espaciado (50 m) o el umbral (20-50 m) lo mueve
  2 puntos como mucho.
- Limitaciones: dos carriles de una autovía desdoblada o una carretera de curvas cerradas muy pegadas (menos de 30 m)
  cuentan como repetidas. El tiempo crece con el cuadrado de la longitud (~0,1 s para 80 km en Debug).
- Alternativas: muestras contra muestras (lo usado en el paso 1; subestima); comparar tramos por identificador de vía
  OSM (BRouter no lo devuelve); Overpass para las etiquetas (saturado y una dependencia más).

## 015 — Forma válida de una ruta: se puede repetir, pero no hacer ramales (estrellas)
- Contexto: las vueltas en abanico salían como estrellas (entrar y salir de cada punto de paso por el mismo ramal), con
  un 35-84 % del recorrido repetido. Pero repetir no siempre está mal. Salidas reales del autor, todas válidas:
  - Globo (Humanes, 73 km): sale y vuelve por los mismos ~19 km (la cuerda) y da una vuelta al final; repite el 50 %.
  - Ida y vuelta pura (La Bañeza, 96 km): repite el 97 %; sale y entra del pueblo por calles distintas el primer km.
  - Lazo (Boadilla del Monte, 84 km): una vuelta cerca de casa unida por un cuello de 4,6 km, ida y vuelta, a una vuelta
    de 50 km; repite el 11 %, en mitad de la ruta.
  - Vueltas (Soto de la Vega, 73 y 84 km): repiten 0-4 %.
  - Globo en el centro (Humanes, 21,6 km): cuerda desde casa, una vuelta pequeña y, desde ella, ida y vuelta de 3 km
    hasta el punto más lejano; repite el 64 %.
  Un límite al % repetido, o exigir que lo repetido esté junto a casa, rechaza rutas buenas (se probó y falló con la ida
  y vuelta y con el lazo).
- Decisión (criterio del autor: "lo que no debemos hacer es crear estrellas"; afinado con sus rutas el 2026-10-15): lo
  único malo es el **ramal**, entrar por una carretera sin salida y salir por la misma en mitad de la ruta.
  - Cada tramo repetido tiene dos pasadas. Entre ellas está lo que se recorre de una a otra: una vuelta, en una cuerda o
    un cuello; solo la media vuelta, en un ramal.
  - **Ramal:** entre las dos pasadas hay menos de 5 km recorridos una sola vez, y menos del 15 % de la ruta. Solo cuentan
    los km hechos una vez: si no, un ramal al final de otro ramal pasaba por vuelta (lo destapó una estrella de Tres
    Cantos). Una vuelta de 2 km alrededor de un pueblo al final de un ramal sigue siendo un ramal.
  - **Salvo que salga de casa:** si el tramo empieza a menos de 3 km de los dos extremos de la ruta, es la ida y vuelta
    misma, no un desvío. Los 3 km dan margen para salir y entrar del pueblo por calles distintas o para una grabación
    que acaba antes de llegar.
  - **Salvo que lleve al destino:** si da la media vuelta en el punto de la ruta más lejano de casa (en línea recta), es
    el destino de la salida (ida y vuelta con un globo en medio), no un desvío. Solo un ramal puede dar la vuelta ahí:
    se probó con un margen del 90 % de la distancia máxima y dejaba pasar estrellas enteras, porque en un abanico todos
    los puntos de paso están a la misma distancia de casa.
  - Un tramo repetido se parte donde su otra pasada salta de sitio: así una cuerda seguida justo de un ramal no se mezcla
    con él (otra estrella de Tres Cantos).
  - Con datos: las seis rutas del autor 0 % en ramales; vueltas generadas buenas 0-1,6 %; estrellas generadas 24-60 %
    (sin contar el brazo que lleva al punto más lejano).
  - Criterio para el generador (4c), **umbral provisional**: como mucho un 10 % de la ruta en ramales. Se revisará con
    los datos de la prueba en vivo.
  - **Medias vueltas cortas** (añadido el 2026-10-15, umbral del autor): la medida de repetición ignora lo repetido a
    menos de 1 km por el recorrido, así que no ve los ramales cortos. Las vueltas generadas en León tenían de 1 a 3 por
    ruta, de 110 m a 2,4 km de entrada y salida (Ferral del Bernesga 610 m, N-630 2,4 km): un punto de paso en mitad de
    una carretera, al que BRouter va y vuelve. El análisis busca cada sitio donde el track da la vuelta sobre sí mismo
    (los puntos 30 m antes y después a menos de 15 m) y mide cuánto entra. **Una media vuelta de más de 200 m que no
    sea el destino invalida una ruta generada**; por debajo son glorietas, cambios de sentido o ruido del GPS (40-80 m
    en las salidas del autor). Además, el perfil activa `correctMisplacedViaPoints` de BRouter sin tope de longitud:
    BRouter recorta él mismo la ida y vuelta a un punto de paso intermedio (comprobado en su código fuente; revisa como
    mucho 500 nodos).
  - **También cualquier ramal de más de 200 m** que no sea el destino invalida una ruta generada, aunque sea poco
    porcentaje (2026-10-15, misma regla aplicada al caso que se escapaba): en vivo, la vuelta hacia el norte de León
    subía 2,4 km por la N-630 y bajaba por el mismo sitio con una vuelta pequeña al final; no es una media vuelta en
    seco (ni BRouter ni el detector la ven así) y solo era el 7 % de la ruta. El límite del 10 % en ramales queda, pero
    lo que manda para una ruta generada es esto.
- Limitaciones: un tramo repetido tres veces se empareja solo con la pasada más cercana; los umbrales (5 km, 15 %, 3 km)
  salen de pocas rutas reales, todas de Madrid y León. Una media vuelta que termina con una vuelta pequeña (una glorieta
  al final del ramal) no se detecta como media vuelta.
- Alternativas: límite al % repetido total (rechaza globos e idas y vueltas); repetición permitida solo junto a casa
  (rechaza lazos; fue la primera versión de este mismo día); contar puntos de giro (no distingue un ramal corto de una
  media vuelta en una rotonda).

## 016 — `trunk` permitido con penalización (cambia DECISIONS 013)
- Contexto: DECISIONS 013 prohibía `trunk` en todo el mundo. En España muchas carreteras nacionales de un carril por
  sentido están etiquetadas `trunk` en OpenStreetMap, también las antiguas nacionales que quedaron tranquilas al construir
  una autovía paralela. Datos del 2026-10-15:
  - Prueba en vivo (8 vueltas en Madrid y León): perfil estricto 3 de 8 válidas y 18 "target island" en 92 peticiones;
    con `trunk` permitido (coste 1,5) 6 de 8 y 6 "target island" en 96, pero hasta 17,5 km por `trunk`.
  - Salidas reales del autor, emparejadas con BRouter (`tools/investigacion/matchrides.cs`; 98-100 % de la ruta
    emparejada sobre el track): una vuelta por Soto de la Vega lleva 21 km por `trunk` (parece la antigua N-VI entre
    La Bañeza y Astorga), otra 2,8 km y una salida de Humanes 12 km. Casi todo de un carril por sentido; los trozos de
    sentido único son glorietas y cruces. Con la prohibición, el generador nunca propondría esas rutas.
- Decisión (del autor, 2026-10-15): **permitir `trunk` con una penalización fuerte** en el perfil, para que BRouter solo
  lo use cuando la alternativa sea mucho más larga o no exista. Autopistas y autovías (`motorway`) siguen prohibidas.
  El análisis contará los km por `trunk` aparte (no como prohibidos) y el resumen de la ruta los dirá. Sin tope de km
  por ruta de momento; entre candidatas, mejor la de menos `trunk`.
  - **Coste 3** (decidido por el autor, 2026-10-15): un km por `trunk` cuesta como 3 por una `tertiary`. Prueba en vivo
    con costes 2, 3 y 5: 6 de 8 vueltas válidas con los tres; km por `trunk` sumados en las seis válidas 24,1, 13,9 y
    18,2. Entre 3 y 5 la diferencia es ruido del generador; 3 deja usar nacionales tranquilas como la antigua N-VI
    cuando el rodeo por comarcales es largo, y 5 las evitaría casi siempre.
  - Aplicado en `road-strict.brf` y en `RoadRules` (categoría `Trunk`, `RouteAnalysis.TrunkKm`). En otros países el
    acceso lo siguen decidiendo las etiquetas (`bicycle=no`, `motorroad=yes` prohíben la vía).
- Alternativas: mantener la prohibición (rechaza carreteras que el autor usa y deja 3 de 8 vueltas); prohibir solo los
  `trunk` desdoblados (los tramos de sentido único en glorietas y cruces cortarían carreteras de un carril; se podrá
  cobrar más a los desdoblados sin prohibirlos).


## 017 — Límites del generador de vueltas
- Contexto: el generador (fase 2, paso 4c) pide rutas a BRouter, que admite una petición por segundo y limita sin avisar
  (STATUS). En la prueba en vivo del 2026-10-15 hicieron falta de 1 a 12 peticiones por vuelta.
- Decisión (del autor, 2026-10-15):
  - **Como mucho 12 peticiones por candidata** (unos 12-15 s a una por segundo).
  - **3 candidatas por defecto** en la consola, con rumbos separados; en la fase 3 el viento decidirá.
  - **Sin límite aparte para el desvío de un punto de paso:** un punto que cae muy lejos (el del mar dio 446 km) ya
    hace fallar la distancia o el rumbo. El análisis sigue midiendo el desvío.
- Alternativas: más peticiones por candidata (más vueltas válidas, pero más espera y más riesgo de bloqueo); una sola
  candidata (la fase 3 necesita elegir entre varias).

## 018 — BRouter en local para desarrollar
- Contexto: brouter.de limita sin avisar (dos bloqueos con HTTP 403 el 2026-10-15, uno tras unas 330 peticiones en el
  día). Las pruebas del generador y el juez de la fase 2 necesitan cientos de rutas.
- Decisión (del autor, 2026-10-15): para desarrollar y probar, un servidor de BRouter en el propio ordenador
  (`tools/brouter-local/`). Verificado en las fuentes: BRouter 1.7.10 (la misma versión que brouter.de), licencia MIT,
  paquete de 6,7 MB en GitHub comprobado con su SHA-256; datos de brouter.de/brouter/segments4 (descarga pública,
  regenerados cada semana; OpenStreetMap, ODbL), dos ficheros de 5° × 5° para Madrid y León (168 MB); Java 17 ya
  instalado. La consola lo usa si existe la variable de entorno `VIENTOHUD_BROUTER_URL` (no es una opción del comando:
  la app no pide datos al usuario), sin esperar entre peticiones. La atribución deja de nombrar brouter.de.
  - No contradice DECISIONS 001 (sin backend propio): es una herramienta de desarrollo, no algo que la app necesite.
    Para la app, la fase 6 ya preveía BRouter dentro del móvil.
  - Comprobado: la misma vuelta da exactamente las mismas rutas que brouter.de, en 9 s en lugar de ~1 min.
- Alternativas: seguir con brouter.de espaciando las pruebas (lento y con riesgo de bloqueo); BRouter en Docker (más
  piezas sin ventaja en Windows); motor propio (descartado en DECISIONS 013).

## 019 — Tramos de la ruta: iguales, de ~1 km, con el rumbo de la línea recta
- Contexto: la fase 3 (paso 3a) necesita saber hacia dónde va la ruta y a qué hora pasa por cada sitio para cruzarlo
  con el viento.
- Decisión (técnica, 2026-10-15; `RouteLegs`):
  - La ruta se parte en **N tramos iguales**, con N = longitud / 1 km redondeada (al menos uno): no queda un tramo
    suelto al final. La longitud se mide a lo largo del track (puede diferir un poco de la que da BRouter).
  - **Rumbo del tramo:** el de la línea recta de su principio a su final (grados HACIA). Para el viento de cara o a
    favor es exacto: el efecto de un viento constante a lo largo de las curvas del tramo suma lo mismo que sobre esa
    línea recta. Para el viento de lado no lo es (en un zigzag se queda corto).
  - **Rectitud:** línea recta / longitud del tramo (1 = recto; cerca de 0 = vuelve sobre sí mismo, como un giro de
    180°, y su rumbo no significa nada). Multiplicando la componente de cara por la rectitud sale la media exacta.
  - **Hora de paso:** salida + km / velocidad media (25 km/h por defecto, la misma que `--hours`). El ajuste por el
    viento queda para el paso 3c.
- Alternativas: tramos de 1 km exacto con uno más corto al final (un tramo de 100 m pesaría como uno entero si se
  promedia por tramos); un tramo por cada segmento del track (cientos de tramos de pocos metros, con rumbos ruidosos);
  rumbo medio de los segmentos (no tiene la propiedad de la suma exacta).

## 020 — Viento en cada tramo: un punto de previsión cada 5 km, en una llamada
- Contexto: la fase 3 (paso 3b) necesita el viento previsto en cada tramo a la hora en que se pasa por él.
  Medido el 2026-10-15 con las previsiones archivadas de AROME (1-14 oct., horas de día con viento ≥ 12 km/h,
  rejilla de 5 × 5 puntos): la diferencia media del viento con el del centro es de 3,7-5,1 km/h a 5 km y de
  5,2-8,7 km/h a 10 km (León, La Bañeza, Tres Cantos; en Tres Cantos, junto a la sierra, 10,7 km/h a 14 km).
  Es tanto o más que el error de la propia previsión (unos 4 km/h, DECISIONS 012): un solo punto no basta.
- Decisión (del autor, 2026-10-15): **un punto cada 5 km** de ruta (el centro de tramos iguales de ~5 km; 19 en
  96 km), todos en **una sola llamada** a Open-Meteo. Cada punto de una llamada cuenta como una llamada del cupo
  gratuito (10.000 al día): una ruta de 100 km gasta unas 20.
- Detalles técnicos:
  - Se mantiene AROME → `best_match` (DECISIONS 005 y 012). Si AROME no cubre algún punto, Open-Meteo rechaza la
    llamada entera (comprobado con Madrid + Tarifa) y todos los puntos pasan a `best_match`: la ruta queda con un
    solo modelo, sin costura.
  - El viento de un tramo es el de su punto medio a la hora en que se pasa por él (`WindField`): mezcla los **dos
    puntos más cercanos** (peso 1/distancia²; si está a menos de 50 m de uno, solo ese) y las **dos horas** que lo
    rodean (lineal). La velocidad se mezcla como velocidad y la dirección como flechas, para que dos vientos
    distintos no se anulen en una calma falsa. La racha es la de la hora en que cae el momento (la racha de
    Open-Meteo a las 9:00 es la más fuerte entre las 8:00 y las 9:00).
  - Componentes (`LegWind`): **de cara** = velocidad × cos(dirección DESDE − rumbo HACIA) × rectitud (positivo de
    cara, negativo a favor; es la media exacta a lo largo de las curvas, DECISIONS 019); **de lado** = velocidad ×
    sen(…) sobre la línea recta del tramo (positivo por la derecha), y la racha de lado igual, sin signo. En un tramo
    con curvas, el de lado se queda corto.
  - Por posición y no por km de ruta: sirve igual para los dos sentidos de una vuelta, para varias candidatas que
    compartan puntos y, en el juez (3e), para el viento observado en estaciones.
- Alternativas: un punto cada 10 km (la mitad de gasto, pero junto a la sierra el viento cambia ~9 km/h a 10 km); uno
  por tramo de 1 km (5 veces más gasto y la rejilla de AROME es de 2,5 km: datos repetidos); un solo punto (pierde lo
  que cambia el viento a lo largo de la ruta); mezclar todos los puntos y no solo los dos más cercanos (los lejanos
  diluyen el viento local).

## 021 — Hora de paso: la pendiente reparte el tiempo; el viento no ajusta la velocidad
- Contexto: la fase 3 (paso 3c) preveía ajustar la velocidad de cada tramo por el viento para estimar mejor la hora
  de paso. Medido el 2026-10-15 con 8 salidas reales del autor (Garmin, con hora y altitud; 2023-2026, Madrid y León;
  575 tramos de 1 km) y la previsión archivada de AROME en cada tramo (`tools/investigacion/calibrate.cs`):
  - **Viento:** en llano, unos 0,3 km/h de velocidad por cada km/h de viento de cara o a favor, casi simétrico
    (−2,4 km/h con más de 8 km/h de cara, +2,5 km/h con más de 8 a favor). Menos de lo que daría la física a
    potencia constante (~0,45-0,5). Todas las salidas fueron con viento flojo o moderado.
  - **Pendiente:** −3,9 km/h por cada 1 % (entre −4 % y 4 %), a unos 30 km/h: un 13 % de la velocidad en llano.
- Decisión (del autor, 2026-10-15):
  - **El viento no ajusta la velocidad**, solo cuenta en la puntuación. Con vientos así mueve la hora de paso unos
    minutos en una salida de horas, cuando la previsión va por horas; y pesan otros factores (ir solo o en grupo, el
    terreno, lo que se apriete). Cambia el plan de la fase 3 ("con un ajuste simple por viento").
  - **La pendiente sí** (`RouteLegs.Split`): la ruta entera dura su longitud / la velocidad media elegida (como hasta
    ahora: `--hours` sigue significando lo mismo), y ese tiempo se reparte por tramos con la velocidad en llano por
    (1 − 0,13 × pendiente en %), con la pendiente limitada a ±4 % (lo medido). Sin altitudes, velocidad fija.
- Comprobación (`tools/investigacion/timing.cs`, mismas 8 salidas, a la velocidad media real de cada una): error en
  el tiempo transcurrido al final de cada tramo, medio | máximo: **velocidad fija 2,9 | 13,4 min; con pendiente
  1,6 | 4,8 min**. Con altitudes del GPS (barométricas); las de BRouter salen de un modelo del terreno.
- Alternativas: ajuste por viento medido (0,3 km/h por km/h) o físico (potencia constante): descartados por el autor;
  pendiente sin mantener la media (la velocidad elegida sería la de llano: una ruta con puertos duraría más que lo
  pedido con `--hours`).

## 022 — Puntuación de una salida para el viento
- Contexto: la fase 3 (paso 3c-2) tiene que ordenar las vueltas candidatas, en los dos sentidos. El plan pide viento
  de cara en la primera mitad, a favor en la segunda y penalizar las rachas fuertes de lado. Preguntar pesos
  abstractos ("¿cuánto vale el viento de cara al principio?") no tiene buena respuesta (lo dijo el autor), así que se
  busca un criterio sin pesos y solo se le pregunta lo que sabe por experiencia.
- Decisión (2026-10-15; `RideRanking`, `RideScore`):
  - **Criterio principal, sin pesos: ventaja = viento de cara medio en la primera mitad − en la segunda** (por
    distancia; la media de cada mitad pondera por la rectitud de los tramos, DECISIONS 019). Mayor es mejor.
  - **Seguridad: racha de lado de más de 40 km/h** (límite elegido por el autor; configurable). Va primero la salida
    con menos km por encima del límite (cero, si se puede) y, entre iguales, la de más ventaja. Se dice dónde están
    esos tramos. Si alguna hora no tiene racha prevista, se avisa (`GustsKnown`).
  - Cada vuelta se puntúa en los dos sentidos, con una sola llamada de previsión para todas las candidatas.
- **Consecuencia comprobada (test):** con el mismo viento en todas partes y todo el rato, los dos sentidos de una
  vuelta puntúan igual: el punto de la mitad es el mismo en los dos, y la media del viento de cara de cada mitad solo
  depende de dónde queda ese punto. Lo que decide es **qué vuelta** (que su mitad quede contra el viento: el rumbo,
  paso 3d); el sentido solo cambia algo cuando el viento cambia durante la salida (sube, rola) o de un sitio a otro.
- Alternativas: pesos para cara/favor/lado/rachas (no hay forma de fijarlos con criterio); penalizar el viento por
  posición con pesos lineales a lo largo de la ruta (con viento constante también da lo mismo en los dos sentidos y
  añade un peso más); que las rachas resten puntos en vez de ordenar primero (mezcla seguridad y comodidad).

## 023 — Rumbos según el viento y explicación por tramos
- Contexto: la fase 3 (paso 3d) tiene que buscar las vueltas donde el viento las favorece (no cada 120°) y explicar
  cada una en pocas líneas. Con DECISIONS 022 se vio que lo que decide es qué vuelta (que su mitad quede contra el
  viento), no el sentido.
- Decisión (del autor, 2026-10-15; `RideBearings`): con viento claro (regla "empieza hacia X" de la fase 1,
  DECISIONS 007, sobre las horas de la salida), **una vuelta justo contra el viento y las demás a ±45°**, luego ±90°…
  Con calma o viento variable, repartidas cada 360°/n desde el norte, como en la fase 2. En el ejemplo de La Bañeza
  (viento del 194°) ganó la de 240°, a 46°: hace falta margen a los lados porque justo contra el viento puede no haber
  vuelta válida (hacia el sur no la hubo).
- Decisión (del autor): **un GPX por vuelta, en su mejor sentido**, numerados por puesto (el otro sentido apenas cambia
  la puntuación).
- Explicación (técnica; `WindStretches`): cada tramo de 1 km se clasifica por cómo se nota el viento (de cara, a
  favor, de lado por la derecha o por la izquierda, según la componente mayor; flojo si ninguna llega a 5 km/h); los
  tramos seguidos iguales se unen y los de menos de 5 km se pegan a su vecino más largo, hasta que todos llegan a 5 km.
  Cada tramo da la media de lo que se nota y la racha de lado más fuerte. Una vuelta de 60 km queda en unas 4 líneas.
- Decisión (del autor): **si contra el viento no sale ninguna vuelta válida, se amplía el abanico**, de uno en uno,
  a +90°, −90° y 180° (con el viento a la espalda), hasta tener una. Descubierto en la prueba en vivo desde
  Fuenlabrada (viento del E): las tres del sector este fallaron (autovías y polígonos; ya era zona difícil en la fase 2)
  y salió una hacia el N tras fallar la del S. Cuesta hasta 36 peticiones más; no se amplía si el rumbo lo da el usuario
  (`--bearing`) ni con calma o viento variable (los rumbos ya van en todas direcciones).
- Hora de salida (técnica): `--at hh:mm`, hora local, la próxima vez que llega (hoy o mañana); por defecto, ahora. Se
  calcula en Core (`DepartureTime`) con la zona horaria del sistema, incluidos los cambios de hora. `--speed` vale ya
  también con `--km`: decide la hora de paso.
- Alternativas: ±30° (las tres pueden salir parecidas o fallar donde hay pocas carreteras) o ±60° (las de los lados ya
  llevan mucho viento de lado); sin ampliar (puede quedarse sin ruta) o siempre una de reserva en otra dirección (casi
  nunca buena para el viento); escribir los dos sentidos (6 ficheros) o solo la ganadora; tramos fijos de 10 km en la
  explicación (cortan por la mitad los cambios de viento).

## 024 — Juez de la fase 3: la elección con la previsión, repuntuada con el viento observado
- Contexto: el plan pide recalcular la puntuación de rutas de días pasados con el viento observado y ver si la elegida
  seguía siendo buena.
- Decisión (2026-10-16; sitios y previsiones elegidos por el autor):
  - **Casos:** León (LELN a 7 km), Fuenlabrada (Getafe, Cuatro Vientos), Tres Cantos (Barajas, Cuatro Vientos) y La
    Bañeza (LELN a ~35 km: el viento "real" es menos fiable, va en fila aparte); los ~15 días con más viento de los
    últimos 3 meses en cada sitio; salida a las 10:00 hora local, 60 km a 25 km/h, 3 vueltas.
  - **Dos previsiones** (como la fase 1): "salgo ahora" (archivo a corto plazo) y "el día antes" (24 h antes).
  - **Viento observado** (`ObservedWind`): METAR de las estaciones cercanas (las de la fase 1), una muestra por hora
    con el informe más cercano a ±15 min; un viento variable o en calma toma la dirección del informe más cercano que la
    tenga; si a una estación le falta alguna hora, no cuenta. Con una sola estación el viento es igual en toda la ruta:
    sirve para juzgar qué vuelta se elige, no los detalles de un pueblo a otro.
  - **Medidas** (`RideJudge`), sobre las mismas vueltas y las mismas horas de paso: si la elegida fue también la mejor
    con el viento real (acierto); cuánta ventaja se perdió frente a la mejor (pérdida); su ventaja real frente a una
    vuelta elegida sin mirar el viento (rumbos cada 120° desde el norte, la mejor del generador, como en la fase 2);
    cuántas veces tuvo de verdad viento de cara en la primera mitad; rachas de lado de más de 40 km/h observadas sin
    aviso.
  - **Para darlo por bueno:** sin umbral fijado de antemano; como mínimo, la elegida tiene que ganar claramente a no
    mirar el viento. Se decide con los resultados delante.
- Resultado (2026-10-16, 129 casos; tabla en `STATUS.md` y `docs/verificacion/2026-10-16-fase3-juez.*`): sin La
  Bañeza, la elegida acierta el 90 % ("salgo ahora") y el 81 % ("el día antes"), pierde 0,6 km/h de media frente a la
  mejor y tiene +8,0 / +6,1 km/h de ventaja real frente a −0,5 / −1,0 sin mirar el viento. **El autor da la fase 3 por
  buena.** Pendientes: pocas candidatas (generador de la fase 2) y elecciones frágiles cuando la ventaja prevista es
  pequeña.
- Alternativas: el archivo a corto plazo de Open-Meteo como "viento real" (es otra previsión, no una medida); solo
  estaciones dentro de la ruta (casi nunca hay).

## 025 — Fuente de radar: OPERA (EUMETNET); AEMET OpenData fuera del producto
- Contexto: la fase 4 (paso 4a) necesita imágenes de radar que se puedan descargar, pasar a una rejilla de intensidad
  de lluvia, comparar entre sí para sacar el movimiento y, para el juez, recuperar de días pasados. El plan partía del
  radar de AEMET. Comprobado el 2026-10-16 en la documentación oficial y con llamadas reales.
- Comparación:

  | | OPERA (EUMETNET, Open Radar Data) | Web de AEMET (descarga GeoTIFF) | AEMET OpenData | RainViewer |
  |---|---|---|---|---|
  | Clave | No | No | Sí; desde jul. 2026 caduca a los 3 meses | No |
  | Contenido | Valores: reflectividad (DBZH) y lluvia en mm/h (RATE) | Clases de color de 6 dBZ (11 niveles) | Imagen GIF de 480×530 px con fronteras, logo y leyenda dibujados (comprobado) | Teselas PNG; el color se traduce a dBZ |
  | Frecuencia / retraso | DBZH 5 min / ~4 min; RATE 15 min / ~12 min | 10 min / ~11 min | 10 min (regional), 30 min (nacional) | 10 min |
  | Resolución | DBZH 1 km; RATE 2 km | ~2,5 km (composición) | Sin publicar | Zoom 7 como máximo (~0,9 km) |
  | Cobertura | Europa, con los radares de AEMET; Madrid y León comprobados píxel a píxel | Península y Baleares | España | Mundial |
  | Histórico | Desde sept. 2012, descarga anónima | 2-3 días | Solo la última imagen | 2 horas |
  | Licencia | CC BY 4.0 | CC BY 4.0 ("Fuente: AEMET") | CC BY 4.0 | Solo uso personal o educativo |
  | Formato | ODIM HDF5 y GeoTIFF optimizado (COG), proyección LAEA | GeoTIFF EPSG:4326 en tar.gz | GIF | PNG |
  | Estabilidad | Servicio oficial y documentado (MeteoGate) | Direcciones internas de la web, sin documentar | Oficial | Recortado mucho en 2025-2026 |

  Acceso a OPERA: `https://s3.waw3-1.cloudferro.com/openradar-24h/` (últimas 24 h) y `.../openradar-archive/` (histórico),
  ruta `AAAA/MM/DD/OPERA/COMP/OPERA@AAAAMMDDTHHMM@0@{DBZH|RATE|ACRR}.{h5|tiff}`, ~1,3-2,4 MB por fichero (Europa
  entera). También hay una API (OGC EDR, `api.meteogate.eu`) limitada a 200 peticiones/h sin clave.
- Decisión (del autor, 2026-10-16):
  - **OPERA como fuente principal**, detrás de una interfaz.
  - **La web de AEMET, solo como reserva** si OPERA falla; no se programa ahora.
  - **AEMET OpenData no entra en el producto:** su clave caduca cada 3 meses, se pide a mano y no puede ir dentro de la
    app (ni en el repo ni en cada móvil); pedir una a cada usuario va contra "cero entrada de datos manual". Como mucho,
    herramienta del autor para el juez (estaciones sin aeropuerto, pendiente de la fase 1). La clave del autor llegó el
    2026-10-16 y caduca hacia mediados de enero de 2027; va en `VIENTOHUD_AEMET_KEY` o en user-secrets, nunca en el repo.
  - **El juez de la fase 4 compara con dos rivales:** "nada se mueve" (la lluvia de ahora se queda donde está) y la
    previsión de AROME HD cada 15 min de Open-Meteo (`meteofrance_arome_france_hd_15min`: 1,5 km, cubre Madrid y León,
    no el sur de España). La predicción propia solo vale si gana a los dos.
- Cautelas:
  - León lo cubre el radar de Palencia (Autilla del Pino), a ~100-120 km: a esa distancia el haz va alto y puede
    quedarse corto con la lluvia débil. El juez lo medirá.
  - La red de AEMET está en renovación (Valladolid se apagó el 2026-06-15) y hoy faltan en OPERA Valencia, Murcia,
    A Coruña, Asturias y Palma. OPERA distingue "sin eco" (`undetect`) de "sin datos" (`nodata`): la app tiene que
    distinguirlos también.
  - Leer HDF5 o GeoTIFF pedirá un paquete NuGet; se valora en el paso 4b (candidato: PureHDF, MIT, sin dependencias
    nativas, probado con un fichero real de OPERA).
  - Fuera de Europa no hay fuente; RainViewer sería la candidata si algún día hace falta (uso no comercial).
- Alternativas descartadas: AEMET OpenData (clave que caduca, dibujos GIF en vez de datos, sin histórico); web de AEMET como principal
  (colores en escalones de 6 dBZ, direcciones sin documentar, 2-3 días de histórico); RainViewer (uso personal, 2 h de
  histórico, sin previsión desde 2026); Météo-France (solo Francia, clave, ~20 h sin archivo); DWD (solo Alemania);
  satélite H SAF (más grueso, registro, 60 días); servicios con "lluvia en el próximo minuto" (OpenWeatherMap,
  Tomorrow.io…): no dan radar, solo su previsión, y piden clave.

## 026 — Leer OPERA: PureHDF, reflectividad cada 5 min y solo la zona de la salida
- Contexto: el paso 4b tiene que leer las imágenes de OPERA (`DECISIONS.md` 025). Comprobado el 2026-10-16: en el
  histórico de 2026 solo hay ficheros HDF5 (los GeoTIFF solo están en las últimas 24 h y en el histórico hasta 2025),
  y el juez usará días de lluvia de los últimos meses.
- Decisión (del autor, 2026-10-16):
  - **Paquete NuGet PureHDF 2.2.0** en `VientoHud.Core`, el primero de Core. Licencia MIT, sin dependencias (ni nativas
    ni de otros paquetes), para .NET 8 y posteriores (vale para MAUI), activo (versiones en marzo, julio y agosto de
    2026; ~590.000 descargas). Riesgo: lo mantiene una sola persona.
  - **Producto: reflectividad (DBZH)**, 1 km, cada 5 min, publicada ~4-8 min después; la lluvia en mm/h (RATE) llega
    cada 15 min con ~12 min de retraso, y el "ahora" puede ser de hace casi media hora. El lector admite los dos y el
    juez (4e) podrá compararlos.
- Decisiones técnicas:
  - **Solo la zona:** se lee un cuadrado alrededor de la salida (un trozo del HDF5, sin leer el resto). La imagen entera
    de 1 km tiene 16,7 millones de celdas y tarda ~40 s; un cuadrado de 300 km, 1-4 s.
  - **La rejilla se queda en la proyección de OPERA** (Lambert azimutal equivalente, celdas de 1 km), para que imágenes
    seguidas encajen celda a celda al calcular el movimiento (4c). Proyección propia (fórmulas de Snyder), comprobada
    con las cuatro esquinas que trae el fichero (calculadas por EUMETNET con PROJ): cuadran al metro.
  - **"Seco" ≠ "sin datos":** `undetect` (el radar miró y no vio nada) es 0 mm/h; `nodata` (fuera de alcance, radar
    parado o fuera del compuesto) es NaN.
  - **Reflectividad a lluvia con Marshall-Palmer** (Z = 200·R^1,6), la relación estándar; tope en 55 dBZ (~100 mm/h)
    porque por encima suele ser granizo.
  - **Hora de la imagen:** la de su nombre, que es el final de la ventana de 10 min en que se juntan los barridos.
  - **Dónde buscar:** las de menos de 23 h en el bucket de 24 h y luego en el histórico; las más viejas al revés. La
    última se busca listando el bucket desde hace dos horas. Caché opcional de los ficheros descargados en disco.
- Primera observación (3 oct. 2026, 6:00 UTC, chubasco en Barajas): la reflectividad da 1,0 mm/h en Barajas y lluvia
  en el 26 % del cuadrado de 300 km; RATE da 0,24 mm/h y el 12 %. RATE sale más seco; lo dirá el juez.
- Alternativas: leer GeoTIFF con un lector propio sin paquete (no hay GeoTIFF en el histórico de 2026; quizá en la
  fase 6 para descargar solo un trozo en el móvil); envoltorios de la librería nativa de HDF5 (HDF.PInvoke: nativos por
  plataforma, mal en Android); lector de HDF5 propio (formato muy complejo); pasar la rejilla a latitud/longitud
  (deforma las celdas y complica el movimiento).
