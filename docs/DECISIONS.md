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
