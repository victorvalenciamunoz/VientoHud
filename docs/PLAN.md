# Plan

**Fase actual: 8**

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

## Fase 6 — Mejores rutas

Añadida por el autor el 2026-10-19 (`DECISIONS.md` 039), al ver las rutas de las fases 3 a 5.

- **Ida y vuelta:** proponer también rutas de ida y vuelta por la misma carretera junto a las vueltas, en el rumbo que
  mejor venga (ida contra el viento, o lejos de la lluvia), y que compitan en el mismo orden (rachas, lluvia, viento).
- **Evitar ciudades:** medir los km por ciudad de cada ruta y que no vuelva a pasar por la ciudad de salida a mitad de
  camino (lazos que cruzan León o Tres Cantos varias veces); evitarlas siempre que se pueda, sabiendo que a veces será
  inevitable (salir de casa).
- **Juez:** con las mismas salidas, km urbanos y pasos por la ciudad antes y después, y el ojo del autor en gpx.studio.

## Fase 7 — App MAUI (Android)

Replanteada por el autor el 2026-10-19 (`DECISIONS.md` 048): la ruta la traza el usuario tocando el mapa; la app pone
la dirección, el tiempo y el juicio. El generador automático de las fases 2 a 6 se queda en Core y en la consola.

- Pantalla única, estética de CieloHud: "Sal ahora (o espera), hacia el oeste", con el viento y la lluvia.
- Mapa con la dirección recomendada; el usuario toca un punto (ida y vuelta) o dos o tres (vuelta). Una petición a
  BRouter por ruta; distancia al momento; viento por tramos, lluvia, sentido mejor, llegada.
- Mandar el GPX al aparato de la bici, sea cual sea (compartir y abrir con).
- **Juez:** salidas reales usando solo la app: segundos hasta la ruta, datos gastados, el GPX en el Karoo y en el Fenix,
  y el ojo del autor sobre lo que pasó con el viento y la lluvia.

## Fase 8 — Ver la ruta: perfil y sobrevuelo 3D

Añadida por el autor el 2026-10-23, con la Fase 7 abierta solo por su juez (7i, salidas reales con la app): se
avanza mientras llega la ocasión de salir.

Objetivo: antes de salir, **ver** la ruta, si el usuario quiere. Dos vistas, las dos a elección del usuario desde un
botón VER junto a DETALLE y GPX (nada se abre solo); se encienden cuando la ruta ya está calculada:

- **PERFIL:** la gráfica de altitud de la ruta, coloreada por pendiente, con los km, los avisos del GPX marcados y el
  viento por tramo. Dibujada en la propia app, sin paquetes nuevos; con las alturas que ya da BRouter, así coincide
  con el GPX.
- **3D:** una cámara recorre el track sobre el terreno real; se ve el relieve, las rampas coloreadas por pendiente, el
  viento previsto en cada tramo a la hora estimada de paso y los avisos que ya lleva el GPX. Play, pausa y arrastrar.

Los **mismos rangos de color** por pendiente en las dos vistas (y en el mapa 2D si algún día se pide): los calcula
Core una vez.

### Cómo se dibuja el 3D (`DECISIONS.md` 055)

Valoradas A) WebView con una librería de mapas 3D en JavaScript (MapLibre GL JS, CesiumJS) y B) render nativo desde
C# (OpenGL ES con Silk.NET, Stride, MapLibre Native). Decisión: **A con MapLibre GL JS dentro del HybridWebView de
MAUI**. El JavaScript es una capa fina de dibujo: recibe de Core GeoJSON ya calculado (color por pendiente, flechas,
avisos, fotogramas de la cámara) y lo pinta. Toda la lógica queda en C#.

### Datos del terreno (`DECISIONS.md` 056)

Valoradas las teselas Terrarium (Mapzen, en AWS Open Data; SRTM 30 m, mundiales, sin clave), el MDT05/MDT25 del
IGN/CNIG (5 y 25 m, solo España, CC BY 4.0; descarga por hojas o servicio WCS) y MapTiler Terrain RGB (clave, solo no
comercial). Decisión: **Terrarium**, y la puerta abierta al IGN a 5 m para España convirtiéndolo en local al mismo
formato. Solo hace falta el terreno alrededor de la ruta: Core descarga el corredor y lo cachea en fichero; el
sobrevuelo funciona sin conexión una vez descargado.

Coherencia con la altimetría de la app: BRouter calcula las alturas del GPX con SRTM de 90 m. En el 3D el track va
pegado al relieve dibujado (MapLibre lo drapea) y las pendientes se muestrean sobre ese mismo relieve (30 m); el GPX
sigue como está. **Manda el relieve dibujado en la vista 3D y el GPX en el perfil**; el juez mide la diferencia para
decidir si más adelante se recalculan las alturas del GPX desde el relieve.

### Pasos

0. Documentos: esta fase en el plan, decisiones 055 y 056, STATUS.
1. Core: pendiente por tramo y rangos de color compartidos.
2. Botón VER con su panel y la vista PERFIL. Juez: el desnivel del perfil igual al del GPX; el ojo del autor en el móvil.
3. Prototipo 3D en línea: HybridWebView con MapLibre empaquetado, terreno Terrarium y una ruta ya grabada, en el
   emulador y en el móvil del autor. Medir: se ve, fotogramas por segundo, MB descargados, lo que crece el APK.
4. Corredor de relieve cacheado: Core decide qué teselas cubren la ruta más X km, las baja a fichero y las sirve al
   WebView interceptando sus peticiones. Medir: funciona en modo avión tras la primera carga; tamaño de la caché.
5. Cámara de sobrevuelo: Core genera los fotogramas (posición, rumbo, inclinación, tiempo); JavaScript los reproduce
   con play, pausa y arrastre. Medir fps en el móvil del autor.
6. Viento por tramo a la hora de paso (reutilizando la fase 3) como flechas sobre el terreno, y avisos del GPX como
   marcadores. Juez completo.

### Juez

- Desnivel acumulado y altitud máxima del perfil iguales a los del GPX; los del sobrevuelo dentro de ±5 % de los del
  GPX, o documentada la diferencia (SRTM de 90 m frente a 30 m).
- Pendientes máximas de 2-3 puertos conocidos coherentes con sus altimetrías publicadas.
- El track va pegado al terreno: ni flota ni se hunde (comprobar en tramos de valle y de cresta).
- Fluidez aceptable en el móvil real del autor (medir fps, objetivo ≥ 30).
- Resultados en STATUS.md.
