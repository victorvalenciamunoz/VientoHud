# Estado

**Fase actual: 1 — Viento en consola** (pasos 1 y 2 de 6 hechos)

## Qué funciona

- Solución .NET 10 (`VientoHud.slnx`) con tres proyectos:
  - `src/VientoHud.Core` (librería, vacía de momento).
  - `src/VientoHud.Console` (referencia a Core; solo imprime el nombre).
  - `tests/VientoHud.Core.Tests` (xUnit; un test de humo).
- `global.json` fija el SDK 10.0.x (en la máquina hay una preview de .NET 11 que, si no, se usaría por defecto).
- `Directory.Build.props` con ajustes comunes: `net10.0`, nullable, implicit usings y warnings como errores.
- Repositorio git inicializado con `.gitignore` estándar de .NET.

## Qué falta (fase 1)

- [x] Crear la solución .NET 10: `VientoHud.Core`, `VientoHud.Console`, `VientoHud.Core.Tests`.
- [x] Elegir el proveedor de previsión: **Open-Meteo**, modelo AROME France (ver `DECISIONS.md` 005).
- [ ] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [ ] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [ ] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [ ] Juez: previsión vs observación de estaciones AEMET cercanas, al menos 5 días. Tabla de error aquí.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 1/1 superado (test de humo, se quitará cuando haya tests reales).
- `dotnet run --project src/VientoHud.Console` imprime `VientoHud`.
- Paso 2: llamadas reales a Open-Meteo para Madrid (40.4168, -3.7038). AROME France, AROME France HD,
  ICON-EU, ECMWF IFS y `best_match` devuelven 48/48 horas con velocidad, dirección y rachas.
  ICON-D2 y HARMONIE (KNMI, DMI) responden "No data is available for this location".
  La Historical Forecast API devuelve previsiones pasadas de AROME (útil para el juez).
  AEMET sin clave responde 200 con cuerpo vacío.

## Riesgos y pendientes detectados

- **Juez (paso 6):** la observación horaria de AEMET (`/api/observacion/convencional/...`) solo cubre
  las últimas 12 horas. Los climatológicos diarios dan valores del día (viento medio, racha máxima),
  no horarios. Para tener 5 días de observaciones horarias hay que guardar la observación cada pocas horas
  durante varios días o buscar otra fuente de datos históricos horarios. Decidirlo en el paso 6,
  pero conviene empezar a guardar observaciones pronto.
- **Clave de AEMET:** hace falta pedirla en opendata.aemet.es (gratuita, por correo) para el paso 6.
