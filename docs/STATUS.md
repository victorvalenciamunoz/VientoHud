# Estado

**Fase actual: 1 — Viento en consola** (paso 1 de 6 hecho)

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
- [ ] Elegir el proveedor de previsión (Open-Meteo vs AEMET OpenData) y documentarlo en `DECISIONS.md`.
- [ ] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [ ] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [ ] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [ ] Juez: previsión vs observación de estaciones AEMET cercanas, al menos 5 días. Tabla de error aquí.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 1/1 superado (test de humo, se quitará cuando haya tests reales).
- `dotnet run --project src/VientoHud.Console` imprime `VientoHud`.
