# Estado

**Fase actual: 1 — Viento en consola** (pasos 1 a 5 de 6 hechos; paso 6 en curso: 6a, 6b y 6c hechos)

## Qué funciona

- Solución .NET 10 (`VientoHud.slnx`) con tres proyectos:
  - `src/VientoHud.Core`: tipos `Coordinate`, `WindSample`, `WindForecast`, `LocationFix`; interfaces `IWindForecastProvider`
    e `ILocationProvider`; cliente `OpenMeteoWindForecastProvider`; `Compass` (grados a 16 rumbos); regla `StartDirection`;
    `IWindObservationSource` con cliente `IemWindObservationSource` (METAR); `IPastWindForecastSource` con cliente
    `OpenMeteoPastWindForecastSource`; cálculo del error `WindVerification`.
  - `src/VientoHud.Console` (ejecutable `vientohud`): comando `wind`.
  - `tests/VientoHud.Core.Tests` (xUnit, 112 tests).
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

## Qué falta (fase 1)

- [x] Crear la solución .NET 10: `VientoHud.Core`, `VientoHud.Console`, `VientoHud.Core.Tests`.
- [x] Elegir el proveedor de previsión: **Open-Meteo**, modelo AROME France (ver `DECISIONS.md` 005).
- [x] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [x] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [x] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [ ] Juez: previsión vs observación, al menos 5 días. Tabla de error aquí. Dividido en:
  - [x] 6a. Observaciones: METAR de IEM, estaciones más cercanas a cualquier punto del mundo (`DECISIONS.md` 009).
  - [x] 6b. Previsiones pasadas de Open-Meteo: a 0-3 h (Historical Forecast API) y a 24 h (Previous Runs API),
    para AROME, `best_match` y ECMWF (`DECISIONS.md` 010).
  - [x] 6c. Cálculo del error en Core: emparejar previsión y observación por estación y hora; error medio de
    velocidad (absoluto y con signo), error angular de dirección, % de horas con la dirección dentro de ±45°. Por separado con viento
    observado flojo (< 12 km/h) y con viento (`DECISIONS.md` 011).
  - [ ] 6d. Comando `verify`; ejecutarlo en Madrid y León (referencias, `DECISIONS.md` 008) sobre 7 días;
    decidir si AROME sigue por delante de `best_match`.
  - [ ] Más adelante: AEMET como complemento para España cuando llegue la clave.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 112/112 superados.
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

## Riesgos y pendientes detectados

- **Juez con AEMET (complemento):** su observación horaria solo cubre las últimas 12 horas; habrá que ir
  guardándola durante días. Útil sobre todo donde no hay aeródromos (montaña de León).
- **Clave de AEMET:** pedida el 2026-10-14; el enlace de confirmación respondió 204 (éxito, sin contenido).
  Falta el correo con la clave. Solo hace falta para el complemento de AEMET.
- **Pocas estaciones METAR fuera de las ciudades:** en León solo LELN en 100 km. El juez dirá qué modelo acierta
  más en zonas llanas y abiertas; en valles y montaña la comparación será menos representativa.
