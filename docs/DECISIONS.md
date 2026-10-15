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
  - **Prohibido:** `motorway`, `trunk` y sus enlaces, obras (`construction`, `proposed`, `abandoned`), escaleras,
    `bridleway`, ferris; cualquier vía con firme sin asfaltar; pistas y sendas sin firme asfaltado declarado. Sigue las
    reglas del perfil estricto, pero con una lista más larga de firmes sin asfaltar (p. ej. `earth`, `mud`, `rock`):
    es una comprobación independiente que puede pillar huecos del perfil.
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
- Limitaciones: un tramo repetido tres veces se empareja solo con la pasada más cercana; los umbrales (5 km, 15 %, 3 km)
  salen de pocas rutas reales, todas de Madrid y León.
- Alternativas: límite al % repetido total (rechaza globos e idas y vueltas); repetición permitida solo junto a casa
  (rechaza lazos; fue la primera versión de este mismo día); contar puntos de giro (no distingue un ramal corto de una
  media vuelta en una rotonda).

