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

## 027 — Comando `radar`: qué cuenta como lluvia y cómo se dibuja
- Contexto: el paso 4b-2 enseña la lluvia de una imagen de radar alrededor de la salida, en texto y en un PNG que el
  autor compara a ojo con el mapa de radar de AEMET (decisión del autor, 2026-10-16).
- Decisiones técnicas (2026-10-16; `RainNearby`, `RainImage`, `PngWriter`, `RadarFrames`):
  - **Cuenta como lluvia desde 12 dBZ (≈ 0,2 mm/h)**, lo más débil que pinta AEMET, para que el texto diga lo mismo que
    los mapas. Es un valor para mirar, no para decidir: el umbral del juez (4e) lo decidirá el autor.
  - **Intensidad con los umbrales de AEMET:** débil hasta 2 mm/h, moderada hasta 15, fuerte hasta 30, muy fuerte hasta
    60 y torrencial por encima. Una celda en el tope de la reflectividad (55 dBZ) se dice "torrencial o granizo".
  - **Texto:** lluvia en el punto, la lluvia más cercana (km y hacia dónde) y la más fuerte, cuánta zona tiene datos y
    cuánta moja.
  - **PNG:** la rejilla tal cual (norte arriba, 2 píxeles por km), con los 11 colores de la leyenda de AEMET (sacados de
    su imagen de radar: un color cada 6 dBZ desde 12), negro donde está seco, gris donde no hay datos, círculos cada
    50 km y una cruz en la salida. Por el tope de 55 dBZ no aparecen los tres colores más altos. Escrito sin paquetes
    (zlib del propio .NET).
  - **Imagen pedida:** la de la hora dada redondeada hacia atrás (múltiplo de 5 min) y, si falta, hasta tres anteriores.
  - **Caché de la consola** en `%LOCALAPPDATA%\VientoHud\radar` (sin limpieza todavía).
- Alternativas: umbral de 0,1 mm/h (la mayor parte es ruido que AEMET no pinta) o de 0,5 mm/h (no diría "llueve" en
  lluvias débiles que AEMET sí pinta); colores propios (no se podría comparar con AEMET de un vistazo); paquete de
  imágenes (no hace falta para un PNG sencillo); dibujar fronteras o carreteras (más datos y trabajo; los círculos y la
  cruz bastan para comparar).

## 028 — Movimiento de la lluvia entre dos imágenes: un desplazamiento para toda la zona
- Contexto: el paso 4c tiene que saber hacia dónde y a qué velocidad va la lluvia para moverla hacia delante. El plan
  pide empezar simple: un desplazamiento global por correlación. División aceptada por el autor (2026-10-16): 4c-1 entre
  dos imágenes; 4c-2 con varias imágenes, la consola y las comprobaciones.
- Decisión (técnica, 2026-10-16; `RainMotions`, `RainMotion`):
  - **Se mueve la imagen anterior hasta que más se parece a la posterior** (correlación de Pearson sobre las celdas con
    datos en las dos). El desplazamiento entre el tiempo que las separa es la velocidad. Rumbo en grados HACIA donde va la
    lluvia (como los rumbos de la ruta), no el "desde" del viento.
  - **Se compara la forma, no la cantidad:** cada celda vale su reflectividad por encima de 12 dBZ, con un tope de 18 dB
    más (30 dBZ, ≈ 2,7 mm/h); lo seco vale 0 y cuenta. Así un núcleo fuerte, o un eco fuerte que no se mueve (montañas,
    parques eólicos), no manda sobre una zona amplia de lluvia.
  - **Búsqueda de grueso a fino:** bloques de ~4 km para todos los desplazamientos hasta 150 km/h; luego celda a celda
    alrededor del mejor bloque; al final, una parábola por el máximo para afinar por debajo de la celda. 30-170 ms con
    300 km a 1 km.
  - **"No sé" en tres casos:** menos de 200 km² con lluvia en alguna de las dos; parecido por debajo de 0,4; mejor
    desplazamiento en el borde de la búsqueda (más rápido que el límite o falso).
- Medido con imágenes reales de OPERA (parejas a 15, 30 y 45 min):

  | Caso (UTC) | Producto | Hacia | Velocidad | Parecido (quieta → movida) |
  |---|---|---|---|---|
  | Madrid, 3 oct. 05:30-06:15 (chubascos) | lluvia (2 km) | 23-30° (NNE) | 14-24 km/h | 0,11-0,55 → 0,43-0,67 |
  | Madrid, 3 oct. 05:30-06:00 | reflectividad (1 km) | 22-30° | 14-24 km/h | 0,53-0,80 → 0,75-0,89 |
  | Barajas, 6 oct. 16:15-16:45 (tormenta) | reflectividad | 56-58° (ENE) | 29-52 km/h | 0,46-0,71 → 0,69-0,88 |
  | León, 30 sept. 07:30-08:00 (frente) | reflectividad | 57-61° (ENE) | 64-73 km/h | 0,03-0,17 → 0,46-0,61 |

  La dirección sale muy estable entre parejas; la velocidad varía más (la tormenta del 6 oct. de 29 a 52 km/h).
- Cautela: con una sola pareja, el umbral de 0,4 deja poco margen (imágenes sin relación llegan a ~0,38 por azar con
  pocas manchas de lluvia). En 4c-2, varias parejas de la última media hora y su acuerdo darán la confianza.
- Alternativas: comparar los mm/h tal cual (manda el núcleo más fuerte); lluvia sí/no (pierde la forma interior);
  flujo óptico por zonas (es el paso 4f, solo si el juez lo pide); búsqueda completa celda a celda (con 30 min y
  150 km/h son ~22.800 desplazamientos sobre la rejilla entera, frente a ~1.500 sobre bloques y 81 finos).

## 029 — Movimiento de la última media hora y cómo se comprueba
- Contexto: con una sola pareja de imágenes el movimiento es frágil (`DECISIONS.md` 028). El paso 4c-2 lo calcula con
  varias y lo enseña en `radar`; comprobaciones acordadas con el autor (2026-10-16): serie de imágenes para su ojo,
  mover la imagen de hace 30 min frente a dejarla quieta, y el viento a 700 hPa.
- Decisión (técnica, 2026-10-16; `RainMotions.Combine`, `RadarFrames.GetMotionFramesAsync`):
  - **Cuatro imágenes cada 10 min** (la última y las de 10, 20 y 30 min antes): 6 parejas separadas al menos 10 min.
    Las de 5 min no se emparejan (la lluvia se mueve muy poco entre ellas).
  - **Mediana** de las velocidades este y norte de las parejas que dan movimiento; vale si lo dan al menos la mitad y
    coinciden: cada una, en la mediana, a menos de 10 km/h de la mediana (o del 40 % de la velocidad, si es más). Si
    no, "las imágenes dan movimientos distintos". Por debajo de 3 km/h, "apenas se mueve".
  - **Flecha en el PNG**: desde la salida hasta donde llega en una hora la lluvia que está encima.
  - `radar` tarda ~20 s (cuatro imágenes de reflectividad); con la caché, menos.
- Comprobación (2026-10-16; `tools/investigacion/motioncheck.cs` y `motionwind.cs`; resultados en
  `docs/verificacion/2026-10-16-fase4-movimiento*.csv`): 55 momentos cada 30 min en cinco días de lluvia (Madrid 3 y
  6 oct.; León 30 sept., 3 oct. y 24 ago.). En cada momento T, el movimiento como lo calcularía la app a T−30 (imágenes de
  T−60 a T−30), sin mirar la imagen de T:
  - **Mover la imagen de T−30 lo que recorre en 30 min se parece más a la real de T que dejarla quieta en los 55**
    (parecido medio 0,64 frente a 0,40). En los 55 hubo movimiento (6 de 6 parejas en 51; 5 de 6 en 4).
  - **Frente al viento a 700 hPa** (~3.000 m, archivo de Open-Meteo, `best_match`): diferencia de dirección mediana de
    16°, dentro de 45° en 49 de 55; la lluvia va a la velocidad del viento (relación mediana 1,07). Las diferencias
    grandes son de León el 3 oct. al mediodía, con viento en altura flojo (8-15 km/h) y cambiante, donde su dirección
    vale poco.
- Alternativas: más imágenes (una hora: más lento y mezcla movimientos que cambian); la media en vez de la mediana
  (una pareja rara la arrastra); usar el viento a 700 hPa como movimiento (es otra previsión, de un punto y por horas;
  sirve de contraste, no de sustituto).

## 030 — Previsión de lluvia en un punto: mover la última imagen y mirar una zona que crece
- Contexto: el paso 4d tiene que decir si lloverá en un punto a 30, 60 y 90 min. División aceptada por el autor
  (2026-10-16): 4d-1 en Core; 4d-2 en la consola, con la comprobación en momentos pasados. El juez completo es el 4e.
- Decisión del autor (2026-10-16): **palabra y porcentaje**, con sus cortes: por debajo del 20 % "no", del 20 al 50 %
  "puede", por encima del 50 % "probable" (`RainLikelihoods`).
- Decisiones técnicas (2026-10-16; `Nowcast`, `PointNowcast`, `RainChance`):
  - **La lluvia que habrá en un punto dentro de Δt es la que ahora está Δt·velocidad viento arriba** (hacia atrás por
    el movimiento de `DECISIONS.md` 029). Se supone que la lluvia ni crece ni se deshace.
  - **Probabilidad = parte de las celdas con lluvia en un círculo** alrededor de ese sitio, de 2 km más 5 km por hora
    desde la imagen (~5 km a 30 min, ~10 km a 90 min): la lluvia cambia y el movimiento tiene error. Desconocida si
    menos de la mitad del círculo tiene datos. El tamaño lo ajustará el juez (4e).
  - **Se cuenta desde ahora, no desde la imagen:** la imagen tiene 5-10 min, y "dentro de 30 min" es 30 min más esa
    antigüedad desde ella.
  - **Cada 5 min hasta 90.** Llega la lluvia en el primer paso "probable" si ahora no lo es; se va en el primer paso
    "no" si ahora es "probable" (entre medias, "puede", no cambia el estado).
  - **Sin movimiento conocido, la lluvia se queda donde está** (es el rival "nada se mueve" del juez).
  - **La imagen entera movida** (`Nowcast.Advect`), para dibujarla; lo que llega de fuera del recorte, sin datos.
- Alternativas: mirar solo la celda del punto (una celda de 1 km acierta o falla por un kilómetro de error); un radio
  fijo (a 90 min el error del movimiento ya es de varios km); crecimiento y desaparición de la lluvia (mucho más
  complejo; solo si el juez lo pide); probabilidades de varios movimientos posibles (lo mismo).
- Comprobación (paso 4d-2, 2026-10-16; `tools/investigacion/nowcastcheck.cs`; resultados en
  `docs/verificacion/2026-10-16-fase4-prevision-aeropuertos.csv`): 61 momentos cada 30 min en cinco días de lluvia, en
  Barajas y León aeropuerto, a +30, +60 y +90 min. Veces que llovió de verdad, según el radar en la celda del aeropuerto,
  cuando la previsión dijo cada palabra (llovió / casos):

  | Plazo | Movida: "no" | Movida: "probable" | Quieta: "no" | Quieta: "probable" |
  |---|---|---|---|---|
  | +30 min | 4/29 (14 %) | 26/29 (90 %) | 7/25 (28 %) | 19/26 (73 %) |
  | +60 min | 3/26 (12 %) | 24/29 (83 %) | 7/23 (30 %) | 16/27 (59 %) |
  | +90 min | 3/22 (14 %) | 24/33 (73 %) | 7/23 (30 %) | 13/27 (48 %) |

  Mover la lluvia gana claramente a dejarla quieta, y más cuanto más lejos. "Puede" salió poco (3-6 casos). Fallo
  típico: la tormenta del 6 oct. en Barajas a las 14:06 UTC nació allí mismo y la previsión de las 13:30 dijo "no":
  mover la lluvia no ve la que aún no existe.
  **Frente al METAR** sale peor ("no" con lluvia en el METAR: 10-14 de 22-29), pero casi siempre porque el propio radar
  no ve esa lluvia: en León el radar marca seco en la celda del aeropuerto cuando el METAR dice lluvia en 38 de 117
  casos (32 %); en Barajas, 12 de 66 (18 %). Es la lluvia débil que el radar de Palencia, a ~120 km, no alcanza (riesgo
  anotado en `DECISIONS.md` 025). El juez (4e) tendrá que medir contra los dos.

## 031 — Juez de la fase 4: la previsión de lluvia contra el radar y contra el METAR
- Contexto: el plan pide un backtest: con imágenes históricas, predecir t+30 / t+60 usando solo las anteriores y
  comparar con la real; tabla de acierto por plazo. División (como la fase 3): 4e-1 piezas en Core; 4e-2 pasar el juez.
- Decisión (del autor, 2026-10-16; aceptó las cuatro recomendaciones):
  - **Contra qué, dos tablas:** el radar después en el punto (justo para juzgar el movimiento) y el METAR (independiente,
    pero en León el radar no ve parte de la lluvia: `DECISIONS.md` 030).
  - **Qué es lluvia:** 0,2 mm/h en el radar (12 dBZ, lo que pinta AEMET); en el METAR, cualquier precipitación en el
    aeropuerto (lluvia, llovizna, chubasco, nieve, granizo), no la de "en las cercanías" (VC) ni la "reciente" (RE).
  - **Qué días:** los de lluvia de los últimos 3 meses en Madrid (Barajas, Getafe, Cuatro Vientos, Colmenar) y León,
    cada 30 min desde 2 h antes de la lluvia hasta 2 h después, para que haya también casos secos (falsas alarmas).
  - **Medidas** (`RainScores`): de la lluvia que hubo, cuánta anunció ("probable"); cuando dijo "probable", cuántas
    veces llovió; cuando dijo "no", cuántas veces llovió; si los porcentajes son de fiar (en bandas de 20 %: cuando dice
    40 %, ¿llueve ~4 de cada 10?) y un número para ellos (Brier: 0 perfecto, 0,25 decir siempre 50 %).
  - **Rivales:** "nada se mueve" (la misma previsión sin movimiento) y AROME HD cada 15 min (Open-Meteo,
    `meteofrance_arome_france_hd_15min`: 0 % o 100 % según su lluvia en el aeropuerto). Además, tres tamaños del círculo
    de la previsión para ajustarlo.
- Piezas (técnicas, 2026-10-16):
  - `IPrecipitationObservationSource` / `IemPrecipitationObservationSource`: el tiempo presente de los METAR del archivo
    de IEM (`data=wxcodes`), con `MetarWeather.HasPrecipitation` para leerlo por trozos de dos letras.
  - `IPastRainForecastSource` / `OpenMeteoPastRainForecastSource`: la lluvia de cada cuarto de hora del archivo de
    previsiones, de varios sitios en una llamada, en mm/h; `RainForecastSeries.RateAt` toma el cuarto de hora que acaba
    en la hora pedida o justo después (como el radar, que junta los minutos anteriores).
- Cautela: el archivo de Open-Meteo guarda las primeras horas de cada pasada del modelo, así que a cada hora tiene lo
  que AROME dijo poco antes: un rival generoso. Que la previsión propia le gane vale más por eso.
- Alternativas: solo el radar (se juzga a sí mismo); solo el METAR (castiga al radar por la lluvia que no ve en León);
  0,5 mm/h (solo lo que moja de verdad; se puede sacar después con los mismos datos).

## 032 — Círculo de la previsión: 4 km + 15 km/h
- Contexto: el juez de la fase 4 (`DECISIONS.md` 031; `docs/verificacion/2026-10-17-fase4-juez.md`) probó cinco tamaños
  del círculo de la previsión (`DECISIONS.md` 030). Con el de partida (2 km + 5 km/h) los porcentajes se pasaban de
  seguros: cuando decía ~96 %, llovía el 63 % de las veces. Con círculos mayores el Brier baja y los porcentajes se
  ajustan mejor, a cambio de anunciar algo menos de lluvia como "probable":

  | Círculo | Radio a +30 / +60 / +90 | Brier a +30 / +60 / +90 (radar) | Lluvia anunciada | Dice ~95 %: llueve |
  |---|---|---|---|---|
  | 2 km + 5 km/h | 5 / 7 / 10 km | 0,111 / 0,143 / 0,157 | 64 / 52 / 44 % | 63 % |
  | 3 km + 10 km/h | 9 / 14 / 19 km | 0,103 / 0,129 / 0,139 | 63 / 50 / 45 % | 67 % |
  | **4 km + 15 km/h** | 13 / 20 / 28 km | 0,100 / 0,121 / 0,129 | 60 / 49 / 43 % | 70 % |
  | 5 km + 20 km/h | 17 / 27 / 37 km | 0,098 / 0,118 / 0,126 | 59 / 47 / 40 % | 73 % |

  (Radios contando los 5 min que tarda en publicarse la imagen.)
- Decisión (del autor, 2026-10-17, con la recomendación): **4 km + 15 km/h**, por defecto en `NowcastOptions`. Se queda
  con casi toda la mejora y apenas pierde avisos; el de 5 km afina un poco más, pero a hora y media ya mira 37 km.
- Alternativas: el de partida (avisa algo más, porcentajes demasiado seguros); 5 km + 20 km/h (mejor Brier por poco,
  menos avisos); recalibrar los porcentajes aparte (otra pieza más; el círculo ya los acerca).

## 033 — Lluvia más allá de 90 min: AROME HD cada 15 min mirando una zona
- Contexto: la previsión del radar llega a 90 min y una salida dura 2-4 h (fase 5, paso 5a). El juez de la fase 4 vio
  que AROME HD en un punto acierta mal el sitio exacto. Investigado el 2026-10-17 en la documentación y el código de
  Open-Meteo y con llamadas reales:

  | | AROME HD 15 min en una zona | Conjunto ICON-EU-EPS | `precipitation_probability` |
  |---|---|---|---|
  | Detalle | 1,5 km, cada 15 min (valores propios) | 40 miembros, 6,5 km, cada hora | ~25 km (conjuntos de 0,25°), cada hora |
  | Alcance | ~4,5-5,5 h desde ahora (pasada cada hora, ~35 min de retraso) | 5 días, 3-4 h de retraso | Días |
  | Cobertura | 37,5-55,4° N, 12° O-16° E: Madrid y León sí, el sur no | Europa | Mundial |
  | Pasadas antiguas | Sí: Single Runs API (`run=`), desde abr. 2026 (comprobado) | No (habría que guardarlas) | Sin comprobar |
  | Coste | 1 llamada por punto | ~4 por punto (miembros / 10) | 1 por punto |

  Otros: AROME France 15 min sale vacío en Madrid; ICON-D2 no llega a España; las demás versiones cada 15 min reparten
  la hora en cuatro. `precipitation_probability` de distintos modelos no coincide (Madrid, mismas horas: ICON 13-35 %,
  GFS 65-97 %). MET Norway no da probabilidad fuera de los países nórdicos. AEMET (probabilidad por municipio) pide
  clave.
- Decisión (del autor, 2026-10-17, con la recomendación):
  - **AROME HD cada 15 min mirando una zona** alrededor de cada tramo (parte de la zona con lluvia, como con el radar),
    porque es la única que se puede juzgar ya con pasadas antiguas.
  - **`precipitation_probability` de `best_match` solo de reserva**: fuera de la zona de AROME o más allá de su
    alcance, avisando de que esa parte es menos fiable.
  - **Mezcla:** radar hasta 60 min; de 60 a 90 min, de radar a AROME poco a poco; AROME después. El juez dirá si el
    cambio debe ser antes o después, y el tamaño de la zona de AROME.
- Alternativas: el conjunto ICON-EU-EPS (probabilidad de verdad, pero llega con 3-4 h de retraso, más grueso y sin
  pasadas para juzgarlo); solo `precipitation_probability` (muy grueso y los modelos no coinciden).
- Piezas (técnicas, 2026-10-17, paso 5a-1):
  - **Una sola fuente de Open-Meteo para la lluvia** (`OpenMeteoRainForecastSource`, antes solo para el archivo) con
    tres modos: la previsión de ahora (`Latest`), el archivo (`Archive`, el del juez de la fase 4) y una pasada concreta
    (`SingleRun`, sin fechas: la API no las admite y el rango se corta después). También da la probabilidad por horas
    de reserva (`IRainProbabilitySource`, `best_match`).
  - **Rejilla de puntos** alrededor de la salida (`ModelRainField.GridAround`), en una sola llamada (cada punto cuenta
    como una llamada en los límites de Open-Meteo).
  - **Probabilidad en zona** (`ModelRainField.Probability`): parte con lluvia (desde 0,2 mm/h) de los puntos a menos de
    un radio y de los cuartos de hora a menos de un margen de tiempo, porque AROME suele acertar el patrón con adelanto o
    retraso. Valores provisionales (15 km, ±30 min) hasta el juez del 5a-2.

## 034 — Radar hasta 2 h, mezcla hasta 3 h y, después, solo un aviso para la zona
- Contexto: juez de AROME HD 15 min en zona (paso 5a-2, `tools/investigacion/judge5a.cs`,
  `docs/verificacion/2026-10-17-fase5-arome-zona.*`): 20 días de lluvia en Barajas y León, ~80 casos por plazo, con
  pasadas antiguas (Single Runs API; en el archivo solo hay una cada 3 h). Brier contra el radar (más bajo, mejor):

  | Plazo | Radar movido | AROME en un punto | AROME en zona (15 km, ±30 min) | Siempre la media (~21 %) |
  |---|---|---|---|---|
  | +60 | 0,118 | 0,215 | 0,191 | 0,160 |
  | +90 | 0,135 | 0,190 | 0,160 | 0,152 |
  | +120 | 0,091 | 0,203 | 0,172 | 0,160 |
  | +150 | 0,168 | 0,266 | 0,210 | 0,167 |
  | +180 | 0,133 | 0,190 | 0,146 | 0,144 |
  | +210 a +270 | — | 0,24-0,29 | 0,18-0,22 | ~0,17 |

  Mirar una zona mejora a AROME (0,224 → ~0,19 en conjunto; los radios de 5 a 20 km y los márgenes de 0 a 60 min salen
  casi iguales). Pero el radar movido le gana hasta las 3 h, y de 2,5 h en adelante AROME no gana a decir siempre la
  probabilidad media de esos días: no sirve para el kilómetro concreto. Contra el METAR, lo mismo.
- Decisión (del autor, 2026-10-18, con la recomendación):
  - **Radar hasta 2 h; de 2 a 3 h, mezcla que pasa poco a poco del radar a AROME en zona (15 km, ±30 min); después,
    AROME** (cambia la de `DECISIONS.md` 033, que era de 60 a 90 min).
  - **Más allá de 3 h no se cuenta la lluvia kilómetro a kilómetro** para ordenar las vueltas: solo un aviso general
    para la zona ("a partir de las 13:00, posibilidad de chubascos en la zona, ≈30 %"), con AROME en toda la rejilla.
- Cautela: pocos casos por plazo y pasadas de AROME de hasta ~1 h de antigüedad (en vivo, 35-95 min).
- Alternativas: contar también más allá de 3 h con su probabilidad (reordenaría las vueltas por ruido); el aviso con la
  probabilidad por horas de `best_match` (más gruesa; queda de reserva donde no llega AROME).
- Piezas (técnicas, 2026-10-18, paso 5a-3; `RainOutlook`, `RouteRain`):
  - **Un "panorama de lluvia"** (`RainOutlook`) por salida: la última imagen de radar con su movimiento, AROME en una
    rejilla alrededor de la salida (alcance de las vueltas + 15 km, cada 5 km, como mucho 15 × 15 puntos; solo si la
    salida pasa de 2 h) y la probabilidad por horas de reserva en la salida. Se pide una vez para todas las vueltas.
  - **Probabilidad en un sitio y una hora:** radar hasta 2 h; de 2 a 3 h, mezcla lineal (el peso de AROME pasa de 0 a 1);
    si falta una fuente, la otra; sin AROME (fuera de su zona), la reserva. Donde el radar no tiene datos viento arriba
    (un radar caído, o lluvia rápida que viene de fuera del recorte), AROME aunque falten menos de 2 h.
  - **Tramo a tramo** (`RouteRain`): la probabilidad en el medio de cada tramo de 1 km a la hora de paso; km con lluvia
    "probable" y "puede"; primera lluvia; más allá de 3 h, sin contar y un aviso para la zona (AROME en toda la
    rejilla cuarto de hora a cuarto de hora, o la reserva cuando AROME ya no llega).

## 035 — Orden de las vueltas con lluvia y viento
- Contexto: el paso 5b ordena las vueltas con la lluvia de cada tramo (`DECISIONS.md` 034) además del viento
  (`DECISIONS.md` 022).
- Decisión (del autor, 2026-10-17 y 2026-10-18; `RideRanking.Order`):
  1. **Menos km con racha de lado peligrosa** (más de 40 km/h), como hasta ahora: la seguridad primero.
  2. **Menos km con lluvia "probable"** (más del 50 %, contada hasta 3 h desde ahora). **Menos de 5 km de diferencia
     es un empate**: la previsión de lluvia no afina a pocos km. "Puede" no cuenta (acierta poco: juez de la fase 4).
  3. **Mejor ventaja de viento** (de cara en la primera mitad, a favor en la segunda).
- Técnica: el empate no se compara de dos en dos (0, 4 y 8 km empatarían por parejas pero no los tres): entre las
  vueltas con los mismos km de rachas, ordenadas por lluvia, cada grupo toma la más seca que queda y todas las que estén
  a menos de 5 km de ella; dentro del grupo decide el viento. Sin lluvia (no se miró), el orden es el de la fase 3.
- Alternativas: la lluvia antes que las rachas (el autor prefirió la seguridad primero); comparar los km exactos (el orden
  dependería del ruido de la previsión); sumar también los km de "puede" (acierta poco).

## 036 — ¿Salir ahora o esperar?
- Contexto: el paso 5c decide si conviene esperar para salir (hasta 2 h, cada 15 min: decisión del autor).
- Decisión (del autor, 2026-10-18, opción A; `DepartureChoice`):
  - **Esperar solo para mojarse menos o evitar rachas peligrosas, nunca por el viento solo**: una salida más tarde vale
    si quita algún km de racha de lado peligrosa (sin añadir 5 km o más de lluvia probable) o, con las mismas rachas, al
    menos 5 km de lluvia probable (el mismo umbral del empate, `DECISIONS.md` 035). Si no, se sale ahora.
  - Entre las que valen: la que más rachas quita, luego la que más lluvia quita, luego la más temprana.
- Técnicas:
  - **Las mismas vueltas para todas las salidas** (las buscadas para salir ahora), puntuadas en cada hora de salida con
    una sola petición de viento y un solo panorama de lluvia; de cada hora cuenta su mejor vuelta.
  - **Comparación justa de la lluvia:** cuanto más tarde se sale, más parte de la vuelta cae más allá de las 3 h
    contadas. Se compara solo en los km contados en las dos (los primeros km, hasta donde llega la más tardía): saber
    menos no puede hacer que una salida parezca más seca.
- Alternativas: esperar también por el viento (opción B; el viento cambia despacio y esperar tiene su coste); comparar la
  lluvia de toda la vuelta (la salida tardía saldría más seca por desconocimiento).

## 037 — La recomendación en la consola y en el GPX
- Contexto: el paso 5d junta todo en `route` y lo mete en el GPX, que es el producto (el móvil va guardado).
- Decisiones (del autor, 2026-10-18, con las recomendaciones):
  - **Se amplía `route`** en vez de crear otro comando. Mira desde ahora (o desde `--at`) hasta 2 h después.
  - **Avisos en puntos de la ruta** (`wpt` del GPX, `RideAlerts`): donde empieza cada tramo con racha de lado peligrosa,
    cada tramo con lluvia probable y la parte con aviso para la zona; como mucho 10 (se quedan primero las rachas, luego
    la lluvia más larga, luego el aviso). Nada del viento: serían demasiados y no es peligro.
  - **Nombre corto** del GPX, del estilo "Sal 10:30 · 60 km SO"; se revisará al verlo en el Karoo.
- Técnica: el GPX lleva los puntos entre los metadatos y el track, con nombre, descripción y símbolo (orden del esquema
  GPX 1.1, comprobado con el esquema oficial). Los textos en español los pone la consola; Core da los datos.
- Pendiente: que el autor compruebe en su Karoo cómo se ven el nombre, la descripción y los puntos.
- Resultado en el Karoo (2026-10-18): el panel de Hammerhead no muestra los puntos de aviso (al pulsar no sale nada) y
  pone a la vuelta el nombre del fichero, no el del GPX. Hammerhead dice oficialmente que el panel y el Karoo no muestran
  los puntos de interés de un GPX, solo los de RideWithGPS; según un usuario (sin comprobar), el panel los importa si cada
  `wpt` lleva `<type>` y `<sym>` con uno de 9 valores fijos (`Danger`, `Generic`…:
  https://github.com/mendalka/gpx-poi-karoo-fixer). **Decisión del autor:** es secundario; lo importante es que la vuelta
  y la hora se eligen por el viento y la lluvia. Los avisos se quedan en el GPX (no estorban) y no se dedica más tiempo.

## 038 — Juez de la fase 5: la recomendación con lo que se sabía, frente a lo que pasó
- Contexto: el paso 5e comprueba la recomendación completa. Sitios elegidos por el autor (2026-10-18): La Bañeza y
  Humanes de Madrid; el resto, con la recomendación: los 7 días con más lluvia (METAR, 06-17 UTC) de los últimos 3 meses en
  cada sitio, decisiones a las 9:00 y 12:00 locales, 60 km a 25 km/h, esperas de hasta 2 h.
- Método (`tools/investigacion/judge5.cs`): la app decide con lo que se sabía entonces (viento previsto del archivo
  "salgo ahora", radar de 5 min antes y su movimiento, la última pasada archivada de AROME HD 15 min ya publicada, la
  reserva de `best_match`): vuelta, sentido y hora de salida. Lo que pasó: km mojados según el radar (celda del medio
  de cada tramo a la hora de paso, desde 0,2 mm/h) y ventaja de viento con el METAR (La Bañeza: León aeropuerto, a 38 km;
  Humanes: Getafe y Cuatro Vientos). Rivales: salir ya con la mejor vuelta de la app, y salir ya con la vuelta elegida
  sin mirar el tiempo (rumbos cada 120° desde el norte).
- Resultado (2026-10-19; `docs/verificacion/2026-10-19-fase5-juez.*`): 24 casos (4 de Humanes sin vuelta válida):

  | Sitio | Casos | Esperó | Mojado app | App saliendo ya | Sin mirar | App ≤ sin mirar | Ventaja app | Ventaja sin mirar |
  |---|---|---|---|---|---|---|---|---|
  | La Bañeza | 14 | 4 | 13,8 km | 15,1 km | 15,7 km | 86 % | +9,0 km/h | +8,3 km/h |
  | Humanes | 10 | 2 | 7,1 km | 10,5 km | 8,1 km | 70 % | +0,7 km/h | −2,4 km/h |
  | Los dos | 24 | 6 | 11,0 km | 13,2 km | 12,5 km | 79 % | +5,5 km/h | +3,8 km/h |

  Esperar ayudó en 5 de 6 (17,8 km mojados frente a 26,5 saliendo ya; Humanes 3 ago.: 13 km en vez de 45) y empeoró en
  1 (La Bañeza 3 oct. 9:00: 38 frente a 18).
- Lectura: la app se moja algo menos y lleva algo más de viento que no mirar, pero la ganancia es pequeña y la muestra
  corta. Los casos caros son los que nadie previó: tormentas de verano que nacen en el sitio (Humanes 15 ago.: prevista
  0 km, mojado 45) y lluvia en toda la zona (La Bañeza 27 ago. y 29 sept.: prevista 46-51 km, mojado 45), donde ninguna
  vuelta ni espera de 2 h la evita. En 6 de los 24 casos se mojó 10 km o más con 0 km de lluvia probable prevista.

## 039 — Fase nueva antes de la app: mejores rutas
- Contexto: al ver las rutas de las fases 3 a 5, el autor (2026-10-19) vio dos cosas: lazos que pasan varias veces por la
  ciudad de salida (León, Tres Cantos) y que nunca sale una ida y vuelta, que a veces sería mejor para el viento o la
  lluvia. También pidió poder dar un rango de distancia, pero luego lo descartó: con la distancia o la duración basta.
- Decisión (del autor, 2026-10-19):
  - **Se cierra la fase 5** con su juez (`DECISIONS.md` 038) tal como salió.
  - **Fase 6 nueva, "Mejores rutas"**, antes de la app MAUI (que pasa a ser la fase 7), para que la app nazca con rutas
    mejores: primero ida y vuelta y después evitar ciudades.
  - **Sin rango de distancia.**
- Alternativas: meterlo como pasos extra de la fase 5 (la habría alargado con trabajo de rutas, que es de la fase 2);
  dejarlo para después de la app.

## 040 — Ida y vuelta: la ida de BRouter y la misma carretera de vuelta
- Contexto: fase 6, paso 6a (`DECISIONS.md` 039): proponer idas y vueltas junto a las vueltas.
- Decisión (técnica, 2026-10-19; `RouteShape.OutAndBack`, `Route.ThereAndBack`):
  - **Se pide a BRouter solo la ida** (de la salida a un punto en el rumbo) **y la vuelta es la misma ruta al revés**.
    Pedirle salida → punto → salida dejaría a BRouter volver por otra carretera si le sale más corta.
  - **El punto lejano** empieza a la mitad del objetivo entre 1,3 (cuánto más larga es la carretera que la línea recta)
    y se acerca o aleja con las distancias obtenidas, como el radio de las vueltas; si no se llega, el rumbo gira (±10°,
    ±20°, ±40°). Las mismas reglas que una vuelta: asfalto, sin autovías, ±10 % de la distancia, cerca del rumbo. La
    media vuelta del punto lejano es el destino y la repetición es desde casa: ninguna es un defecto (`DECISIONS.md` 015).
  - **El desnivel de vuelta** es la bajada de la ida (subida de ida menos lo que sube de la salida al punto lejano).
- Probado en vivo (2026-10-19, `tools/investigacion/outandback.cs`, BRouter local, 60 km, rumbos 0°, 120°, 240°): 11 de
  12 válidas, casi todas con 1-2 peticiones; las 3 de Humanes válidas (donde las vueltas solían fallar); falla Tres
  Cantos hacia el OSO (la distancia salta a 82 km).
- Alternativas: pedir salida → punto → salida (la vuelta podría ir por otra carretera); varios puntos en línea (más
  peticiones sin ganar nada).

## 041 — Una ida y vuelta por rumbo, en el mismo orden que las vueltas
- Contexto: fase 6, paso 6b: las idas y vueltas (`DECISIONS.md` 040) entran en la recomendación.
- Decisión (del autor, 2026-10-19): **una ida y vuelta por cada rumbo buscado**, además de la vuelta (hoy, 3 + 3), y
  compiten en el mismo orden (rachas de lado, lluvia probable, ventaja de viento: `DECISIONS.md` 035). Si al ampliar el
  abanico (`DECISIONS.md` 023) se añade un rumbo, también lleva las dos.
- Técnica: en la consola salen como "ida y vuelta" (en el progreso, en cada opción, en el nombre del GPX y en el del
  fichero, que es el que muestra Hammerhead: `1-ida-y-vuelta-60km-NE-…gpx`).
- Probado en vivo (2026-10-19, BRouter local, 60 km): en Humanes con viento del NE a 18 km/h, la primera es la ida y
  vuelta hacia el NE con +20 km/h de ventaja (10 de cara a la ida, 10 a favor a la vuelta), frente a +4 de la mejor
  vuelta; dos de las tres vueltas no eran válidas y las tres idas y vueltas sí. En León con viento flojo gana una ida y
  vuelta por poco (+5 frente a +2).
- Alternativas: una sola ida y vuelta, contra el viento (menos peticiones; con brouter.de importaría más).

## 042 — Cómo se mide la ciudad de una ruta
- Contexto: fase 6, paso 6c: el autor vio rutas casi todas por ciudad (Humanes) y lazos que cruzan León o Tres Cantos.
  BRouter tiene una estimación de "ciudad alrededor" (`estimated_town_class`), pero los datos de mapa que usamos no la
  traen (comprobado el 2026-10-19 en Madrid centro, León y Humanes: solo `estimated_traffic_class` y
  `estimated_crossing_class`).
- Decisión (técnica, provisional hasta el ojo del autor; `RouteUrban`):
  - **Un km está en ciudad** si tiene 2 o más nodos de casco urbano (pasos de peatones, semáforos, stops, ceda el paso)
    o va por calles (residencial, de servicio, peatonal) al menos la mitad. El cliente de BRouter guarda ya las etiquetas
    de los nodos (`Route.Nodes`).
  - **Volver a casa:** km a menos de 3 km de la salida fuera de los 5 primeros y los 5 últimos.
- Medida de hoy (2026-10-19, `tools/investigacion/urban.cs`, 60 km, rumbos 0°, 120°, 240°; tabla en
  `docs/verificacion/2026-10-19-fase6-ciudad-antes.txt`): Humanes 31-97 % en ciudad (la ida y vuelta al N, 97 %),
  Fuenlabrada 48-90 %, Tres Cantos 14-59 %, León 17-36 %, La Bañeza 10-30 %. Volver a casa casi siempre 0: los lazos
  que el autor vio cruzan la ciudad o sus alrededores más allá de 3 km, no vuelven al centro.
- Ojo del autor (2026-10-19) en Fuenlabrada: N vuelta (90 %) e ida y vuelta (77 %) "casi todo urbano"; ESE ida y vuelta
  (53 %) "gran porcentaje urbano"; OSO ida y vuelta (48 %) "la que más se parece a las que hago": la medida ordena bien.
  Sus salidas reales (`tools/investigacion/ridesurban.cs`; `docs/verificacion/2026-10-19-fase6-ciudad-salidas-autor.txt`):
  Humanes 73 km, 41 %; Humanes 21 km, 56 %; La Bañeza 95 km, 6 %; otras tres, 4-13 %. Alrededor de Humanes casi nada baja
  del 40 % (pueblos y polígonos seguidos): lo que sobra es el exceso (77-97 %), no cualquier travesía.
- Alternativas: polígonos de zonas urbanas de OpenStreetMap (otra fuente externa, más trabajo); datos de BRouter con la
  estimación de ciudad (otros mapas; sin comprobar si existen).

## 043 — Evitar ciudades: el perfil las esquiva y la ciudad cuenta en el orden
- Contexto: fase 6, paso 6d, con la medida de `DECISIONS.md` 042.
- Decisión (del autor, 2026-10-19, opción A con la recomendación):
  - **Perfil de BRouter** (técnica): calles residenciales y de servicio cuestan 2,5 (antes 1,2) y cada paso de peatones,
    stop o ceda el paso añade 30, cada semáforo 60 (antes 20). Así, entre dos caminos parecidos, BRouter toma el de
    fuera. El interruptor de BRouter para evitar ciudades (`consider_town`) no sirve: nuestros mapas no traen la
    estimación de ciudad.
  - **Orden de las rutas:** rachas de lado peligrosas, lluvia probable (empate por debajo de 5 km), **km en ciudad
    (empate por debajo de 10 km)** y, al final, ventaja de viento. Una ruta muy urbana pierde aunque tenga mejor viento;
    dos parecidas en ciudad se deciden por el viento.
  - `route` dice la ciudad de cada ruta ("Ciudad: 15,9 km (28 %) por calles o travesías"), también en el GPX.
- Medida (2026-10-19, `urban.cs`, las mismas 30 salidas de `DECISIONS.md` 042; `docs/verificacion/2026-10-19-fase6-
  ciudad-despues.txt`): con el perfil nuevo la media baja del 42 % al 37 % (Humanes 62 → 51 %, Fuenlabrada 70 → 58 %,
  León 27 %, Tres Cantos 37 → 38 %, La Bañeza 15 → 13 %); ruta a ruta varía mucho porque el generador acaba en otras
  carreteras. En vivo con `route` (viento del NE): en Humanes pasa a primera la ida y vuelta al E (28 %) y la del NE
  (80 %, mejor viento) baja a la cuarta. En Fuenlabrada todas las candidatas tienen 61-90 % de ciudad: los rumbos que
  se buscan con viento del NE van todos hacia Madrid.
- Alternativas: una regla dura (descartar las de más del 60 % si hay otras; no distingue entre 30 % y 55 %); la ciudad
  antes que la lluvia (mojarse importa más).

## 044 — Buscar rutas con menos ciudad: variantes del rumbo y rumbos a los lados
- Contexto: el autor (2026-10-19) vio que la ida y vuelta de Humanes al E podía ir un poco más al sur (Torrejón de
  Velasco) con mucha menos ciudad, y que la de Fuenlabrada al N tenía "muchísima ciudad": "la ciudad, en la medida de lo
  posible, debemos evitarla". El generador se quedaba con la primera ruta válida del rumbo pedido.
- Decisión (del autor, 2026-10-19, opción A, con lo técnico):
  - **Variantes de cada ida y vuelta:** además del rumbo pedido, el mismo girado ±15° y ±30°, y se queda con la válida
    con menos km de ciudad (las vueltas no: cuestan más peticiones). Giros mayores (±40°) solo si ninguna es válida.
  - **Rumbos a los lados y hacia atrás:** si la mejor ruta tiene más del 50 % de ciudad, se buscan también los rumbos a
    ±90° y 180° del viento (vuelta e ida y vuelta) y se ordena todo otra vez.
- Probado en vivo (2026-10-19, BRouter local, 60 km, viento del NE): Humanes, primera la ida y vuelta al E con 35 % de
  ciudad (antes 80 % al NE); Fuenlabrada amplía a SE, NO y SO y la primera baja a 54 % (la del SE, 42 %, empata en
  ciudad y pierde por el viento); León, 10 %. Desde Humanes, las idas y vueltas hacia el ESE y el SE (115°-125°, por
  Torrejón de Velasco) dan 26-29 %: el suelo de esa zona con esta medida (la salida real del autor, 41 %). Cuesta más
  peticiones a BRouter (Fuenlabrada, 119): sin problema con el local; con brouter.de, a una por segundo, serían ~2 min.
- Alternativas: variantes también para las vueltas (cada una necesita varias peticiones); buscar siempre los seis rumbos
  (el doble de peticiones en todos los casos).
