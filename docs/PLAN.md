# Plan

**Fase actual: 5**

---

## Fase 1 — Viento en consola

Objetivo: para una ubicación, conocer el viento previsto por horas (velocidad, dirección, rachas)
y comprobar lo que acierta la previsión.

- Proveedor de previsión detrás de una interfaz. Candidatos: Open-Meteo (sin clave) y AEMET OpenData (clave gratuita).
  Comparar y documentar la elección en DECISIONS.md.
- Consola: `dotnet run --project src/VientoHud.Console -- wind [--lat --lon] [--hours 6]`
  → tabla por horas: velocidad, rachas, dirección (grados + punto cardinal).
- Regla básica: "empieza hacia X" (contra el viento medio de las próximas horas).
- **Juez:** comparar previsión vs observación de estaciones AEMET cercanas en varios días pasados.
  Apuntar el error medio en dirección y velocidad en STATUS.md.

Hecho cuando: tests en verde y una tabla de error previsión vs observación con al menos 5 días.

## Fase 2 — Rutas circulares + GPX

Objetivo: desde una ubicación y una distancia o duración objetivo, generar varias rutas circulares ciclables
y exportarlas a GPX.

- Decidir el motor de rutas (documentarlo en DECISIONS.md). Opciones a valorar:
  - Servicio externo con rutas circulares (p. ej. openrouteservice, GraphHopper): rápido de integrar,
    con límites de uso y clave.
  - Motor propio sobre un extracto de OpenStreetMap: más trabajo y más control.
  - Enfoque mixto: generar puntos de paso propios (p. ej. polígono orientado según un rumbo)
    y usar un motor externo solo para unirlos por carreteras reales.
- Solo bici de carretera (ver DECISIONS 003):
  - Solo vías asfaltadas; nada de pistas ni caminos.
  - Evitar autopistas y autovías; preferir carreteras secundarias y de poco tráfico.
  - Evitar en lo posible tramos urbanos largos (la salida de Madrid es el tramo más delicado).
- Exportar GPX 1.1 (track con puntos) compatible con GPS de bici (Garmin, Wahoo, etc.).
- **Juez:** el GPX valida contra el esquema GPX 1.1, se abre bien en un visor (p. ej. gpx.studio)
  y se carga en el GPS de la bici. La distancia real queda dentro de ±10 % del objetivo y la ruta no pisa tramos sin asfaltar ni autovías
  (comprobado con los tipos de vía de OpenStreetMap a lo largo del track).

## Fase 3 — Viento en la ruta

Objetivo: elegir la mejor ruta y el sentido de recorrido según el viento.

- Estimar a qué hora pasarás por cada tramo (velocidad media configurable, repartida según la pendiente; el viento
  no ajusta la velocidad, solo puntúa: DECISIONS 021).
- Para cada tramo: componente de viento de cara o a favor según rumbo y hora.
- Puntuación: preferir viento de cara en la primera mitad y a favor en la segunda; penalizar rachas fuertes de lado.
- Consola: ranking de rutas candidatas con su puntuación y explicación ("km 0-18: cara 12 km/h…").
- **Juez:** recalcular la puntuación de rutas de días pasados con el viento OBSERVADO
  y ver si la elegida seguía siendo buena.

## Fase 4 — Radar y lluvia a corto plazo

Objetivo: predecir la lluvia a 30-90 min en una zona.

- Primero investigar qué ofrece el radar de AEMET (formato, frecuencia, resolución, cobertura, licencia).
  Si se queda corto, valorar alternativas. Decidir antes de programar el nowcasting.
- Descargar y guardar imágenes; convertirlas a una rejilla de intensidad de lluvia.
- Estimar el movimiento entre imágenes consecutivas (empezar simple: desplazamiento global por correlación;
  luego por zonas) y proyectarlo hacia delante.
- **Juez (backtest):** con imágenes históricas, predecir t+30 / t+60 usando solo las anteriores
  y comparar con la imagen real. Tabla de acierto por horizonte en STATUS.md.

## Fase 5 — Combinarlo

- Lluvia prevista a lo largo de cada ruta según la hora estimada de paso.
- Recomendación final: salir ahora / esperar hasta X, ruta elegida, sentido, hora estimada de llegada.
- GPX con nombre y descripción que incluya el resumen.

## Fase 6 — App MAUI (Android)

- Pantalla única de recomendación, estética HUD.
- Mapa de la ruta y botón para compartir o descargar el GPX.
- Elegir duración o distancia con un toque.
