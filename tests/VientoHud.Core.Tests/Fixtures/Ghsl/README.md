Datos reales de GHSL BUILT-S R2023A, época 2020, WGS84 a 3 segundos de arco (© European Union, CC BY 4.0;
`https://jeodpp.jrc.ec.europa.eu/ftp/jrc-opendata/GHSL/`), recortados el 2026-10-19 (fase 6, paso 6e-1).

- `GHS_BUILT_S_E2020_GLOBE_R2023A_4326_3ss_V1_0_R5_C18.zip`: 2 × 2 bloques (filas 41-42, columnas 28-29) del cuadro
  R5_C18, alrededor de Humanes y Fuenlabrada (40,353° a 39,927° N, 4,035° a 3,608° O, 512 × 512 casillas). Los
  bloques comprimidos (LZW) se copian tal cual en un BigTIFF pequeño con el punto de anclaje movido, dentro de un zip
  con el nombre del cuadro, como los sirve GHSL (`tools/investigacion/ghslfixture.cs`). Leído con `GhslBuiltUpSource`,
  da las mismas casillas que el cuadro entero (comprobado el 2026-10-19).
