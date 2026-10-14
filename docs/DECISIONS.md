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
  con `best_match` como alternativa cuando se pida más allá de 48 h. El modelo es configurable:
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
