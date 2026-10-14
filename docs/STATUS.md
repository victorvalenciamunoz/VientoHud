# Estado

**Fase actual: 1 — Viento en consola** (pasos 1 a 3 de 6 hechos)

## Qué funciona

- Solución .NET 10 (`VientoHud.slnx`) con tres proyectos:
  - `src/VientoHud.Core`: tipos `Coordinate`, `WindSample`, `WindForecast`; interfaz `IWindForecastProvider`;
    cliente `OpenMeteoWindForecastProvider`.
  - `src/VientoHud.Console` (referencia a Core; solo imprime el nombre).
  - `tests/VientoHud.Core.Tests` (xUnit, 29 tests).
- `global.json` fija el SDK 10.0.x (en la máquina hay una preview de .NET 11 que, si no, se usaría por defecto).
- `Directory.Build.props` con ajustes comunes: `net10.0`, nullable, implicit usings y warnings como errores.
- Repositorio git inicializado con `.gitignore` estándar de .NET.
- **Cliente Open-Meteo:** pide un rango de horas en UTC con varios modelos en una sola llamada
  (por defecto AROME France y `best_match`). Para cada hora usa el primer modelo que tenga dato y lo apunta
  en `WindSample.Model`, así que más allá de las ~36-48 h de AROME sigue con `best_match`.
  Errores de red, timeout, HTTP de error y JSON inválido acaban en `WindForecastException`.
- **Proveedor falso** `FakeWindForecastProvider` en el proyecto de tests, para probar código que consume previsiones.

## Qué falta (fase 1)

- [x] Crear la solución .NET 10: `VientoHud.Core`, `VientoHud.Console`, `VientoHud.Core.Tests`.
- [x] Elegir el proveedor de previsión: **Open-Meteo**, modelo AROME France (ver `DECISIONS.md` 005).
- [x] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [ ] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [ ] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [ ] Juez: previsión vs observación de estaciones AEMET cercanas, al menos 5 días. Tabla de error aquí.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 29/29 superados.
- `dotnet run --project src/VientoHud.Console` imprime `VientoHud`.
- Paso 2: llamadas reales a Open-Meteo para Madrid (40.4168, -3.7038). AROME France, AROME France HD,
  ICON-EU, ECMWF IFS y `best_match` devuelven 48/48 horas con velocidad, dirección y rachas.
  ICON-D2 y HARMONIE (KNMI, DMI) responden "No data is available for this location".
  La Historical Forecast API devuelve previsiones pasadas de AROME (útil para el juez).
  AEMET sin clave responde 200 con cuerpo vacío.
- Paso 3: fixtures reales grabadas el 2026-10-14 en `tests/VientoHud.Core.Tests/Fixtures/OpenMeteo/`:
  respuesta con dos modelos (AROME con datos las 12 primeras horas y `null` después), respuesta con un solo modelo
  (los campos no llevan sufijo), error de latitud (HTTP 400) y "The service is overloaded" (salió de verdad
  al grabar; no se guardó el código HTTP, el test asume 503). Los tests comprueban la URL, los valores leídos,
  el cambio de modelo hora a hora y cada tipo de error. Aún no se ha llamado al cliente contra la API real
  desde código: se verá con el comando `wind` del paso 4.

## Riesgos y pendientes detectados

- **Juez (paso 6):** la observación horaria de AEMET (`/api/observacion/convencional/...`) solo cubre
  las últimas 12 horas. Los climatológicos diarios dan valores del día (viento medio, racha máxima),
  no horarios. Para tener 5 días de observaciones horarias hay que guardar la observación cada pocas horas
  durante varios días o buscar otra fuente de datos históricos horarios. Decidirlo en el paso 6,
  pero conviene empezar a guardar observaciones pronto.
- **Clave de AEMET:** pedida el 2026-10-14, pendiente de llegar. Solo hace falta para el paso 6.
- **Posible fuente alternativa para el juez (sin clave):** el archivo METAR de Iowa Environmental Mesonet
  devuelve observaciones horarias pasadas de Barajas (LEMD), Cuatro Vientos (LEVS), Getafe (LEGT)
  y Torrejón (LETO): dirección en grados, velocidad en nudos y rachas cuando se reportan.
  Probado el 2026-10-14 con 7 y 8 de octubre: 24 registros por estación y día. Falta verificar licencia y límites
  y decidir en el paso 6 (son aeródromos, no carreteras).
