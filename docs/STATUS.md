# Estado

**Sin fase en marcha.** La Fase 10 se cerró el 2026-10-27, como la 9; la 8, el 2026-10-26. La Fase 7 queda **abierta
solo por su juez** (7i, salidas reales con la app).

**Versión para probar:** la 0.4.0 (2026-10-27, `DECISIONS.md` 052), con las fases 8, 9 y 10; sustituye a la 0.3.0 en
GitHub Releases y se instala encima.

Aquí solo va lo vivo: la fase en marcha y las cerradas con algo por ver. Las demás (1 a 6 y 9), con sus jueces y
cómo se validaron, están en `historia.md`, fuera del repo público (`DECISIONS.md` 067): sus datos de sitios reales,
juntos, dejaban deducir por dónde sale el autor.

> Los sitios reales van con etiquetas neutras ("pueblo A", "ciudad 1", "aeropuerto 2"…). Los ficheros de
> `docs/verificacion`, `tools/investigacion` y `tools/brouter-local` citados aquí, y las grabaciones de sitios
> reales de los tests, viven fuera del repo público (`DECISIONS.md` 067).

## Publicar el código (fuera de las fases)

Un solo repo público, sin datos de dónde vive ni por dónde sale el autor; lo privado, solo en su disco
(`DECISIONS.md` 067).

- [x] 1. Código y tests sin ubicaciones (2026-11-05). Los tests con puntos inventados usan `TestPlaces.Origin`; los
  que se apoyan en grabaciones reales viven en `C:\desarrollos\VientoHud-privado\tests` y se compilan solo si la
  carpeta existe.
- [x] 2. Documentación (2026-11-05): `README`, `PLAN`, `STATUS` y `DECISIONS` con etiquetas neutras (unos
  75 sitios, más de 900 menciones), sin coordenadas ni nombres de teselas, y el README sin capturas hasta el paso 3.
  `docs/verificacion`, `docs/capturas`, `tools/investigacion` y `tools/brouter-local` pasan a la carpeta privada
  (copia comprobada fichero a fichero; los scripts siguen compilando desde la raíz del repo). Un agente leyó los
  documentos enteros buscando sitios; encontró además coordenadas con un decimal en `UsageTests` y la tesela de
  zoom 8 de ciudad 1 en `TileStoreTests`, ya cambiadas.
- Validado: `dotnet build` de la solución; `dotnet test` con la carpeta privada, 750 en verde, y sin ella
  (`VIENTOHUD_PRIVATE_DIR` a una carpeta que no existe), 658 en verde; búsqueda en todo el árbol de los ~100 nombres
  de sitios y de pares de coordenadas, sin resultados.
- De paso, el CI (Linux) destapó que el suavizado del perfil dependía de una pizca de longitud de la ruta y de la
  máquina (`DECISIONS.md` 068); arreglado en Core, con un test que lo fija.
- [x] 3. Publicar en `VientoHud` (2026-11-05). Capturas nuevas en el emulador con un día grabado en un sitio sin
  relación con el autor (al pie del Mont Ventoux, 19 oct. 2026), sin metadatos. Dos repasos de privacidad del árbol
  entero por un agente con ojos frescos: encontraron una altitud exacta en un test, ejemplos de coordenadas en la
  consola, la tabla de los puertos y datos que juntos dejaban deducir las dos ciudades (distancias a aeropuertos, radar,
  fechas y horas de chubascos); arreglados, y `historia.md` pasa entera a la carpeta privada (decisión del autor). El
  código entra en el repo público encima de su único commit (el de las descargas, con la versión 0.4.0), sin el historial
  anterior; el `.git` viejo queda guardado en la carpeta privada y `VientoHud-codigo`, archivado.

## Fase 10

**Cerrada el 2026-10-27** (`DECISIONS.md` 066): el mapa libre pasa de un tercio de la pantalla a dos tercios, y el viento
y la lluvia se leen de un vistazo en el mapa y en el 3D. Quedan por ver en una salida, como en la fase 8, un día con el
viento que cambia entre la ida y la vuelta y una ruta circular con el dibujo nuevo.

### Qué funciona (fase 10)

- **La pantalla nueva** (paso 1, 2026-10-27): sin el título, el mapa llega de la barra del móvil a la fila de botones.
  Arriba, una sola tarjeta: el titular y una línea (distancia y llegada, o lo que la app está haciendo) y "›"; tocarla
  abre el DETALLE, que ahora empieza por SALIDA (el porqué de esperar, que antes iba en la tarjeta). "DÍA GRABADO" va
  en la tarjeta. Abajo, IDA Y VUELTA / VUELTA y las opciones en una fila, y un botón grande: PROPONER sin ruta; GPX y
  VER con ella; QUITAR PUNTO si hay puntos. La leyenda va detrás de un "?" sobre el mapa (el viento, la mira, tu
  posición, la zona azul y los triángulos) y los créditos, pequeños al lado. La pista de abajo sale hasta que se pone
  el primer punto en cada tipo de ruta. El botón de atrás del móvil cierra el panel abierto (antes salía de la app).
- **Los avisos en la tarjeta** (paso 1, 2026-10-27): al pasar el porqué de esperar al DETALLE, la lluvia no se veía en
  la pantalla principal (lo notó el autor). Ahora, bajo el titular y en rojo, una línea corta con los avisos del GPX en
  orden de marcha (`RideText.Warnings`): "Lluvia km 0-11", "rachas km 30-34", "chubascos desde km 43"; sin avisos, no
  sale.
- **El viento y la lluvia en el mapa** (paso 2, `DECISIONS.md` 065, `RideMarking`): la flecha grande del viento, blanca y
  sobre la carretera, dice cómo sopla; en las de ida y vuelta, una por sitio si el viento es parecido en las dos
  pasadas y una por pasada, cada una en su lado, si cambió. Las flechas pequeñas del sentido de la marcha van de color
  según cómo te da en esa pasada, a la derecha de quien va: la ida a un lado de la carretera y la vuelta al otro. Una
  gota azul junto a la flecha pequeña de la pasada que se moja. Sin rótulos de km sobre el mapa. El "?" lo explica.
- **Lo mismo en el 3D** (paso 2, `FlyoverWeather.Marks`): flechas del viento blancas, una flecha pequeña de color cada km
  sobre la ruta y gotas azules donde llueve al pasar; fuera los tramos teñidos y los círculos de los avisos (las rachas
  y la lluvia del punto siguen en el texto bajo el vuelo).

### Qué falta (fase 10)

- [x] 0. Documentos: la fase en `PLAN.md` (pedida por el autor el 2026-10-27, `DECISIONS.md` 064) y este estado.
- [x] 1. La pantalla nueva: mapa de arriba abajo, una tarjeta arriba (tocarla abre el DETALLE), una fila de botones
  abajo, la leyenda detrás de un "?". Hecho el 2026-10-27.
- [x] 2. El viento y la lluvia de un vistazo: flecha blanca de cómo sopla, flechas pequeñas de color de cómo te da en
  cada pasada y gota azul donde llueve (rehecho con el autor el 2026-10-27; sustituye a los pasos 2 y 3). Hecho el
  2026-10-27.

### Cómo se ha validado (fase 10)

- Punto de partida (captura del día grabado, 2026-10-27, `docs/verificacion/2026-10-27-fase9-4c-movil-avisos.png`): con
  una ruta propuesta, el mapa ocupa la mitad de la pantalla y el recuadro de arriba tapa un tercio de esa mitad: libre,
  un tercio de la pantalla.
- Paso 1: un test (la línea de avisos de la tarjeta: los tres tipos en orden, "y 2 más" y nada con lluvia solo
  posible). **En el móvil del autor**, con el día grabado de pueblo A y la misma ruta (PROPONER: 84 km hacia el SSO): el mapa libre pasa del 34 % de la pantalla al 64 % (contando
  la barra del móvil y la de navegación; del espacio de la app, del 36 % al 72 %). Sin la línea de "DÍA GRABADO", unos
  3 puntos más. Comprobados la tarjeta que abre el DETALLE, el "?" con la leyenda, PROPONER que deja paso a GPX, VER y
  QUITAR PUNTO, y el botón de atrás. Con la línea de avisos ("Lluvia km 0-11") la tarjeta crece una línea: el mapa
  libre queda en el 62 %. `docs/verificacion/2026-10-27-fase10-1-movil-*.png`.
- Paso 2: 3 tests en Core (`RideMarkingTests`): ida y vuelta con viento parecido (una flecha por sitio, la ida de cara y
  la vuelta a favor, la gota solo en la pasada mojada y a su derecha); con el viento que gira entre pasadas (una flecha
  por pasada, cada una en su lado); y carretera hecha una vez (flecha sobre la carretera, ninguna con viento flojo).
  **En el móvil del autor**, con el día grabado de pueblo A, tras varias pruebas (gota que se toca, triángulos de
  más): "ahora me gusta mucho más; lo veo más limpio". `docs/verificacion/2026-10-27-fase10-2-movil-viento-y-lluvia.png`.
  El 3D, un test más (las marcas cada km por sensación y las gotas a su derecha solo donde llueve) y el del GeoJSON
  cambiado; en el móvil, "está perfecto" (`docs/verificacion/2026-10-27-fase10-2-movil-3d.png`). Arreglado de paso: las
  flechas pequeñas y las gotas del mapa 2D no salían hasta mover el mapa (Mapsui carga sus imágenes al leer los datos
  de las capas, no al repintar: ahora se le pide que los lea).
  No visto aún en el móvil: un día con el viento que cambia entre la ida y la vuelta, y una ruta circular.

### Juez de la fase 10

- **Mapa libre, en el móvil del autor con la versión normal** (pueblo B, 2026-10-27, PROPONER: ida y vuelta de 81 km):
  el 66-67 % de la pantalla contando las barras del móvil, el 74 % del espacio de la app. Antes, el 34 % (36 %). Pedido:
  dos tercios. `docs/verificacion/2026-10-27-fase10-juez-pueblo-b.png`.
- **Ida y vuelta sin pisarse** (día grabado de pueblo A y pueblo B, mapa y 3D): la ida a un lado de la carretera y la
  vuelta al otro, cada una con su color; el autor, "lo veo más limpio" y "está perfecto". El viento fue parecido en las
  dos pasadas: el caso de dos flechas blancas solo está probado en Core.
- **La lluvia del día grabado**: las gotas salen junto a la ida en los primeros km y no en la vuelta, como vio el radar
  (juez de la fase 9).

## Fase 8

### Qué funciona (fase 8)

- **Pendiente por tramo y rangos de color** (paso 1): `RouteProfile.From(route)` da el perfil de altitud de la ruta con las
  alturas que ya trae BRouter: puntos cada 100 m suavizados sobre 300 m, tramos por banda de pendiente (`GradeBand`:
  bajada, llano, suave 2-4 %, media 4-7 %, dura 7-10 %, muy dura), desnivel subido y bajado (sin contar vaivenes de
  menos de 10 m, la misma idea que el "filtered ascend" de BRouter), altitud mínima y máxima y rampa máxima. Los
  umbrales y los colores (`GradeBands.Color`, "#rrggbb") viven en Core para que el perfil, el 3D y el mapa pinten
  igual. Textos en `RideText.Profile` y `RideText.Grade`. Null si a la ruta le falta alguna altura.
- **Botón VER y vista PERFIL** (paso 2): VER junto a DETALLE y GPX en cuanto hay ruta; abre un panel bajo "VER LA RUTA"
  con PERFIL y 3D (3D apagado hasta sus pasos). PERFIL es un panel bajo sobre el mapa, como el del GPX: la gráfica de
  altitud (`GraphicsView` de MAUI, `Hud/ProfileDrawable`, solo dibujo) con el suelo coloreado por banda de pendiente,
  los km, las alturas, una franja con el viento por tramos en los colores de las flechas y los avisos del GPX como
  triángulos donde empiezan; debajo, la línea del perfil (`RideText.Profile`) y la leyenda de bandas. Nada se abre solo.
  `docs/verificacion/2026-10-23-fase8-2-emulador-panel-ver.png`, `...-perfil.png`.
  Visto en el móvil del autor el 2026-10-23: la gráfica estiraba la altura a todo el alto y 150 m de rango en 83 km
  parecían "el puerto 4" (`...-movil-perfil-exagerado.png`); ahora el dibujo exagera como mucho 30 veces la escala
  horizontal (`ProfileDrawable.MaxExaggeration`), una banda tiene que durar 500 m y cada tramo toma el color de su
  pendiente media una vez unido. Bajo la gráfica, la leyenda del viento de la franja (pedida por el autor).
- **Prototipo 3D** (paso 3): el botón 3D del panel VER abre `FlyoverPage`: un HybridWebView de MAUI con MapLibre GL JS
  6.15.0 empaquetado en el APK (`Resources/Raw/wwwroot`, +0,45 MB), el relieve de las teselas Terrarium, el mapa de
  fondo de Thunderforest y la ruta coloreada por banda de pendiente encima. Core compone la escena (`FlyoverScene`:
  GeoJSON por tramos, inicio, caja) y C# se la pasa al JavaScript, que solo dibuja y devuelve fps y teselas. En .NET 10
  la página tiene que cargar `_framework/hybridwebview.js` y los mensajes del puente llegan en un hilo secundario (se
  pasan al principal). `docs/verificacion/2026-10-23-fase8-3-movil-3d-lienzo.png` (el lienzo tal como lo pintó WebGL
  en el móvil del autor).
- **El 3D sin conexión** (paso 4): al abrir el 3D, Core calcula el corredor de relieve de la ruta (`TerrainTiles.Corridor`:
  las teselas Terrarium de zoom 8 a 12 a 2 km del track) y lo baja a la caché del móvil (`TileStore`, carpeta
  `cache/flyover`, una subcarpeta por fuente) mientras se mira; arriba sale "relieve guardado (N teselas, X MB)". El
  JavaScript pide todas las teselas, relieve y mapa de fondo, a la propia página (`tiles/terrarium/…`,
  `tiles/base/…`) y C# las sirve de la caché o las baja una vez y las guarda (`DECISIONS.md` 057). Lo ya visto
  funciona en modo avión.
- **Cámara de sobrevuelo** (paso 5): Core calcula el recorrido (`FlyoverPath`): un punto cada 50 m, mirando 1 km por
  delante con el rumbo suavizado sobre 1,5 km y un giro de como mucho 90° por km (un cambio de sentido se reparte en
  unos 2 km), 1,5 s por km de vuelo entre 40 s y 2,5 min. El JavaScript solo lo reproduce. Abajo en el 3D: la línea
  "km 27,6 · 535 m · −4 %" (`RideText.FlyoverAt`), una barra para arrastrar a cualquier punto, PLAY/PAUSA (empieza
  en pausa; un dedo sobre el mapa lo pausa) y CERCA/LEJOS (zoom 14,5 y 72° de inclinación, o 12,8 y 58°).
  `docs/verificacion/2026-10-26-fase8-5-movil-vuelo.png`.

- **Viento y avisos en el 3D** (paso 6): con la ruta ya juzgada, el 3D pinta sobre el relieve las flechas del viento
  cada 2,5 km (0,8 km de largo, más cortas que en el mapa porque la cámara vuela baja), cada una con el viento de su
  tramo a la hora de paso y en los colores de siempre, y los avisos del GPX como círculos (amarillo rachas, rojo
  lluvia, gris chubascos de la zona). Bajo el vuelo, una segunda línea: "De cara 15 km/h · pasas hacia las 10:45 ·
  rachas de lado de hasta 48 km/h" (`FlyoverWeather.At`, `RideText.FlyoverWeatherAt`). Sin viento aún, sin flechas;
  los avisos esperan a la lluvia, como el GPX.
  Dos juegos de flechas, uno por vista: en LEJOS las de arriba; en CERCA una cada km, de 250 m y a 100 m de la
  carretera (las otras quedaban fuera de la imagen, 2026-10-26). Y solo las del tramo que se vuela: de 0,5 km por
  detrás de la cámara a 3 km (CERCA) u 8 km (LEJOS) por delante; en una ida y vuelta las de la otra pasada están al
  lado y la roja de la ida se leía como la de la vuelta (el autor, 2026-10-26). Donde el viento de cara o de lado no
  pasa de 5 km/h no hay flecha, como en el mapa; la línea de abajo dice "Viento flojo".
  `docs/verificacion/2026-10-26-fase8-6-movil-viento-flojo.png`.

### Qué falta (fase 8)

- [x] 0. Documentos: la fase en `PLAN.md` (dos vistas a elección del usuario, PERFIL y 3D, desde un botón VER),
  decisiones 055 (MapLibre GL JS en el HybridWebView de MAUI) y 056 (relieve Terrarium, IGN a 5 m como mejora) y este
  estado. Opciones y fuentes comprobadas el 2026-10-23 (licencias, mantenimiento, soporte en Android; tamaños y tiempos
  medidos contra las teselas Terrarium y el WCS del IGN).
- [x] 1. Core: pendiente por tramo y rangos de color compartidos. Hecho el 2026-10-23; juez abajo.
- [x] 2. Botón VER con su panel y la vista PERFIL. Hecho el 2026-10-23 en el emulador; juez abajo. **Pendiente el ojo
  del autor en el móvil.**
- [x] 3. Prototipo 3D en línea (MapLibre empaquetado, terreno Terrarium, la ruta de la pantalla). Hecho el 2026-10-23;
  medidas abajo.
- [x] 4. Corredor de relieve cacheado y servido al WebView; funciona en modo avión tras la primera carga. Hecho el
  2026-10-26; juez abajo.
- [x] 5. Cámara de sobrevuelo con play, pausa y arrastre; fps en el móvil del autor (objetivo ≥ 30). Hecho el
  2026-10-26; juez abajo.
- [x] 6. Viento por tramo a la hora de paso y avisos del GPX en el 3D. Hecho el 2026-10-26; visto en el móvil del autor
  (flechas en LEJOS y CERCA, la línea del viento); **pendiente un día con viento a la ida y a la vuelta**. Juez completo
  abajo.

### Cómo se ha validado (fase 8)

- Paso 0: sin código. Las medidas de las fuentes de relieve están en `DECISIONS.md` 056.
- Paso 1: 26 tests (bandas y umbrales; subida constante en un solo tramo con su pendiente; cortes donde cambia la
  banda; un vaivén de ±4 m no cuenta ni colorea; un repecho de 30 m sí; sin alturas no hay perfil; entradas malas).
  **Juez: nuestro desnivel frente al "filtered ascend" de BRouter** en las cuatro rutas grabadas (tests, con la
  salida en el log):

  | Ruta | km | BRouter | Perfil (sube / baja) | Diferencia |
  |---|---|---|---|---|
  | ciudad 1 norte (vuelta) | 17,4 | 97 m | 115 / 111 m | +19 % |
  | ciudad 1 este | 79,7 | 290 m | 302 / 302 m | +4 % |
  | ciudad 1 sur | 21,3 | 142 m | 155 / 155 m | +9 % |
  | pueblo C SO | 48,1 | 384 m | 374 / 371 m | −3 % |

  El filtro de BRouter es algo más fuerte que nuestra histéresis de 10 m; la suma bruta de subidas daría 162-659 m
  (el doble). Probado también calcular el desnivel sobre la serie suavizada: perdía un 28 % en pueblo C (carretera
  de toboganes cortos), así que el desnivel sale de los puntos remuestreados sin suavizar y el suavizado queda para
  dibujar y para las bandas. Las rutas cerradas bajan lo que suben (±4 m). El test exige quedar entre −20 % y +25 %
  de BRouter.
- Paso 2, en el emulador (ciudad 1, PROPONER, 49 km ida y vuelta): el perfil dice "Sube 341 m y baja 341 m, entre … m;
  rampa máxima 6 %" y DETALLE (BRouter) "Desnivel 343 m": −0,6 %. Dos cosas corregidas al verlo: el panel
  llenaba el mapa y una subida de 340 m parecía una montaña (ahora el panel es bajo y la gráfica tiene altura fija), y
  una carretera de toboganes se pintaba en tiras de 100 m (ahora una banda tiene que durar 300 m, `MinStretchKm`,
  o se une a la vecina; con test). Sin rachas ni lluvia ese día: los avisos del GPX quedan por ver con un día de viento.
- Paso 3, **en el móvil del autor** (OPPO CPH2699, ida y vuelta desde pueblo B): el relieve se ve con el mapa y la ruta
  encima; **60 fps (media 58), 102 teselas**. En el emulador (API 34): MapLibre carga, pide 95 teselas (5,8 MB de red
  con el mapa de fondo) y va a 60 fps, pero el lienzo queda vacío: la GPU emulada falla al subir texturas (errores de
  OpenGL en su registro) y se cerró sola; el 3D se juzga en el móvil. APK de depuración: de 101,16 a 101,62 MB.
- Paso 4: 15 tests (la tesela de una posición, el corredor de un track con su margen y sus zooms, las URL, la caché:
  baja una vez y sirve del disco, no guarda lo que falla, vuelve a bajar lo viejo de 90 días, el corredor por delante
  sin repetir). **En el móvil del autor** (2026-10-26): el 3D abierto con conexión y luego en modo avión sobre la
  misma ruta se ve igual. Medida del primer intento (2026-10-23, ida y vuelta desde pueblo B, relieve y mapa de fondo):
  161 teselas, 9,6 MB en la caché. Ese primer intento no pintaba: las teselas servidas por C# a peticiones a otros
  servidores no llevaban la cabecera CORS y el navegador las descartaba; de ahí pedirlas a la propia página.
  Caché medida en el móvil el 2026-10-26 con esta versión, tras varias rutas: relieve 56 teselas, 6,1 MB; mapa de
  fondo 442 teselas, 8,9 MB.
- Paso 5: 10 tests (carretera recta mirando adelante, giro en ángulo recto repartido en 1-3 km, la vuelta de una ida y
  vuelta sin saltos, duración, el fotograma de un punto del vuelo, el JSON, una ruta grabada). Sin el límite de giro la
  cámara se daba la vuelta en un solo fotograma en el punto lejano. **En el móvil del autor** (2026-10-26): **70 fps de
  media en vuelo** (67 en la captura). Dos fallos vistos allí y corregidos: el vuelo no arrancaba (el primer fotograma
  llegaba con una marca de tiempo anterior a PLAY y pedía el punto −1) y MapLibre avisa de "giro" también cuando se
  mueve la cámara por programa (solo un dedo pausa ahora). Al cerrar el 3D, el JavaScript seguía mandando el contador
  cada segundo a una página ya cerrada: ahora se apaga y se suelta el WebView (pendiente de verlo en el móvil).

### Juez de la fase 8

Tres puertos elegidos por el autor (2026-10-26): uno tendido (puerto 1) y dos duros (puerto 2 y puerto 3).
`tools/investigacion/judge8.cs` los traza en el BRouter local con el perfil de la app (las nacionales al precio de
una local: la app las encarece y el puerto 1 salía por 32 km de carreteras locales) y calcula el perfil dos veces, con las alturas del GPX (BRouter, SRTM 90 m) y con el relieve que pinta el 3D (Terrarium, SRTM
30 m, `TerrainSampler`). La subida va de la salida al punto más alto. Se compara con lo publicado de cada puerto; la tabla
con las cifras, los GPX y las fuentes queda fuera del repo (`DECISIONS.md` 067).

- **Relieve del 3D frente al GPX (±5 %)**: la cima, en los tres a +0,1 % como mucho. El desnivel, +4,2 % en el
  puerto 1 y −1,3 % en el puerto 2; **puerto 3 se pasa: +8,6 %**. En su valle estrecho las celdas de 30 m mezclan la
  carretera con las laderas (rampas imposibles de 33 % en 100 m). Como dice `DECISIONS.md` 056, las cifras y los colores
  del 3D salen del GPX; el relieve solo da la forma. Lo que queda por ver es si en esos tramos la línea sube por la
  ladera en el 3D (ojo del autor).
- **Altitud medida**: en una señal de nivelación del IGN del puerto 1, el relieve del 3D da 1,4 m más que la altitud
  medida (la tesela real y su test, en la parte privada).
- **Pendientes frente a lo publicado**: con el GPX, la media y la cima cuadran en los tres. La máxima en 100 m cuadra
  en el puerto 2 y en el puerto 3 (dentro de 1-2 puntos); en el puerto 1 sale ~3 puntos alta, ruido de la malla de 90 m
  en un puerto tendido. La "rampa máxima" que dice la app se mide en 300 m y se queda corta frente a lo publicado en
  los puertos duros (2-4 puntos menos que sus rampas). Decisión del autor (2026-10-26): se queda en 300 m y el texto lo
  dice ("rampa máxima 13 % (en 300 m)").
- **Fluidez**: 70 fps de media en vuelo en el móvil del autor (paso 5), objetivo ≥ 30.
- **Pegado al terreno** (ojo del autor, 2026-10-26, en el móvil): la ruta baja a cruzar un arroyo y sube por la loma
  sin flotar ni hundirse (`docs/verificacion/2026-10-26-fase8-juez/movil-valle-y-cresta.png`, 103 fps de media).
- **Pendiente**: un día con viento a la ida y a la vuelta, para ver flechas de los dos colores en el 3D.

### Riesgos y pendientes (fase 8)

- Las teselas Terrarium las sirve AWS Open Data pero Mapzen cerró; el camino alternativo (IGN, Copernicus) está
  pensado y el formato en local sería el mismo.
- BRouter da alturas de SRTM 90 m y el relieve del 3D es de 30 m: el perfil (GPX) y el sobrevuelo no coincidirán al
  100 %; el juez mide la diferencia.
- El WebView del sistema varía con el móvil: en el OPPO del autor, 60 fps. El emulador no sirve para el 3D (GPU).
- La foto del lienzo que `FlyoverPage` guardaba para el juez se quitó al cerrar la fase (obligaba a WebGL a conservar
  cada imagen).
- La caché del 3D (`cache/flyover`) tiene tope de 50 MB desde la fase 9 (paso 3). Android puede vaciarla; si crece,
  ponerle límite como al radar. Las teselas de más de 90 días se vuelven a bajar al pedirlas.

Fase 8 — Ver la ruta: perfil y sobrevuelo 3D: terminada el 2026-10-26 (pasos 0 a 6 hechos, juez ejecutado con el puerto 1,
el puerto 2 y puerto 3; el autor la cierra con un pendiente: ver en el 3D, un día con viento, flechas de los dos colores a
la ida y a la vuelta, `DECISIONS.md` 058).

## Fase 7

### Qué funciona (fase 7)

- **Tocar el mapa** (paso 7e): cada toque añade un punto (QUITAR PUNTO quita el último); con uno, ida y vuelta; con
  dos o más, vuelta por ellos. `TracedRideAdvisor` va en dos pasos para que la ruta se dibuje en cuanto BRouter
  contesta (`RouteAsync`, línea cian) y después llegue el juicio (`AdviseAsync`: viento en los dos sentidos, lluvia,
  salir ahora o esperar; la ruta se redibuja en el sentido mejor). El panel muestra la recomendación, el sentido con la
  ventaja, los tramos, las rachas, la lluvia, la ciudad, el resumen de la ruta (con "pasa a X km de un punto" cuando
  BRouter mueve un punto a la carretera asfaltada más cercana) y la llegada. BRouter es brouter.de (una petición por
  segundo); con `VIENTOHUD_APP_BROUTER_URL` al compilar, otro (el local desde el emulador: `http://10.0.2.2:17777/`).
  El radar se guarda 3 h en el móvil y la lluvia se reutiliza 10 min.
- **Mapa a pantalla completa, panel plegable y leyenda** (pedido por el autor el 2026-10-20): sobre el mapa solo una
  barra con el titular ("Sal ahora (09:38)." o "Hacia el O") y una línea (km, llegada, qué está haciendo) con DETALLE;
  el panel completo se despliega sobre el mapa y se cierra con CERRAR; va por secciones (SENTIDO, VIENTO POR TRAMOS,
  RACHAS DE LADO, LLUVIA, CIUDAD, RUTA) con una idea por línea; el panel ocupa el hueco del mapa y las secciones se desplazan dentro (con la letra grande del
  móvil no cabían y no había desplazamiento; corregido el 2026-10-20). Bajo el
  mapa, la leyenda de las flechas (de cara, a favor, de lado) mientras haya flechas.
  `docs/verificacion/2026-10-20-fase7-7e-emulador-panel-plegable.png`.
  `docs/verificacion/2026-10-20-fase7-7e-emulador-detalle-secciones.png`.
- **Duración y ritmo** (paso 7g): chip "2 H · 25 KM/H" que abre un panel con la duración (1 h a 5 h) y la media
  (18-33 km/h), guardadas en las preferencias del móvil; al cerrar se recalculan el abanico (alcance = la mitad de la
  distancia entre 1,3), PROPONER y, si hay ruta, las horas de paso y la llegada.
- **Trazar más fácil** (`DECISIONS.md` 053, por la opinión del primer amigo): PROPONER pone una ida y vuelta de 2 h
  hacia el rumbo recomendado (en el emulador, 43 km hacia el OSO en ~20 s,
  `docs/verificacion/2026-10-20-fase7-7e-emulador-proponer.png`); chips IDA Y VUELTA / VUELTA con la pista de qué
  hacer; la mira del centro del mapa (anillo ámbar) es el único sitio que pone puntos: se mueve el mapa y se toca la
  mira. Ni toques, ni pulsaciones largas, ni arrastres ponen puntos (el primer dedo de un pellizco contaba como
  pulsación larga).
- **Los puntos se ponen con una pulsación larga** (2026-10-20): en el emulador cada zoom acababa como un toque y ponía
  un punto, y el autor no podía trazar una vuelta. El toque corto solo cierra paneles. Comprobado: tocar y arrastrar no
  pone nada; dos pulsaciones largas hacen una vuelta (`docs/verificacion/2026-10-20-fase7-7e-emulador-vuelta-pulsacion-larga.png`,
  con flechas rojas de cara y verdes a favor). Donde ida y vuelta comparten carretera las flechas se pisaban: ahora cada
  una va 400 m a la derecha del sentido de marcha, como una señal, y las de ida y vuelta caen en lados opuestos
  (`docs/verificacion/2026-10-20-fase7-7e-emulador-flechas-a-un-lado.png`).
- **Botones + y − de zoom** sobre el mapa (2026-10-20): con una mano en el móvil, y en el emulador la única forma sin
  pellizco (la lupa de la barra del emulador amplía la ventana, no el mapa; el pellizco allí es Ctrl + arrastrar).
- **Hora de salida** (pedido por el autor el 2026-10-20, solo de hoy): el botón SALIDA · AHORA abre un panel propio
  (sin diálogo del sistema, que saldría en blanco) con AHORA, las horas que quedan de hoy y los cuartos; al cerrar, si
  cambió, se recalculan abanico, ruta y juicio para esa salida y los textos pasan a "Sal a las 13:00". Si la hora ya
  pasó, vale "ahora". A más de 3 h vista el panel avisa de que la lluvia es solo un aviso de zona. Con un panel abierto,
  tocar el mapa lo cierra en vez de añadir un punto (un toque en un hueco del panel llegaba al mapa).
  `docs/verificacion/2026-10-20-fase7-7e-emulador-hora-salida.png`.
- **El viento antes que la lluvia** (`TracedRideAdvisor.JudgeWindAsync` y `JudgeRainAsync`): la app juzga primero con
  el viento (una petición: flechas, sentido, rachas y una primera salida, con "Lluvia: pidiendo la previsión…") y
  después con la lluvia, que en el móvil la primera vez tarda (4 cuadros de radar, ~9 MB); el campo de viento se
  reutiliza. El autor no veía las flechas porque miraba antes de que llegara la lluvia.
- **Flechas del viento** (`WindArrows`, pedido por el autor el 2026-10-19): con la ruta trazada, una cada 5 km con el
  viento que habrá al pasar por ahí (apunta hacia donde va el viento: un empujón), de color según se siente (de cara
  rojo, a favor verde, de lado ámbar), con halo blanco; **ninguna donde el viento es flojo** (su dirección sería ruido).
  Cada flecha lleva su km ("km 7,5", pedido por el autor el 2026-10-20; `docs/verificacion/2026-10-20-fase7-7e-emulador-flechas-km.png`:
  pueblo B con viento del O a 16 km/h, flechas ámbar de lado hacia el E y el abanico hacia el O).
  Hubo una flecha grande en casa con el viento medio de una salida de 2 h: el autor la vio contradecir a las de la
  ruta (viento flojo que gira cada hora; su ida y vuelta duraba 27 min) y se quitó: el abanico ya dice hacia dónde.
- **Ubicación y dirección en la app** (paso 7d): `GeolocationProvider` (pide el permiso y la posición al móvil, con la
  interfaz de Core) y `DirectionAdvisor` en Core (el viento de las horas de la salida en el sitio, `StartDirection`, y
  el abanico `DirectionFan`: un sector de ±45° hasta donde llega una ida y vuelta de la distancia; `RidePlanner` lo usa
  también). La pantalla muestra "Hacia el O" (`RideText.DirectionHeadline`), la frase del viento, la posición como
  punto ámbar con el mapa centrado en ella y el abanico en cian translúcido; mientras tanto, qué está haciendo; si
  algo falla, por qué y un botón REINTENTAR. Salida de 2 h fija hasta 7g.
- **Esqueleto de la app** (`src/VientoHud.App`, paso 7c, `DECISIONS.md` 050): .NET MAUI solo Android, con las letras y
  la paleta de CieloHud (`HudTheme`), un mapa de Mapsui a pantalla completa con las teselas de OpenCycleMap
  (Thunderforest, clave leída al compilar de `VIENTOHUD_THUNDERFOREST_KEY`; sin ella, OpenStreetMap y la cabecera lo
  dice), la atribución con enlace en el pie, el cliente HTTP con reintentos de Core y los avisos de terceros dentro de
  la app. Arranca en el emulador (Android 14). Centrado en ciudad 1 hasta que llegue la ubicación (7d).
- **Core preparado para el móvil** (paso 7b, `DECISIONS.md` 049): `ResilientHttp.CreateClient()` reintenta los fallos
  pasajeros (5xx, 408, red) hasta 3 veces con esperas de 2-8 s, no los 429; `CachedRainOutlookSource` reutiliza la
  lluvia 10 min para el mismo sitio (cada punto nuevo cuesta ~25 llamadas a Open-Meteo en vez de ~250);
  `OperaOptions.CacheMaxAge` borra los cuadros viejos del radar. La consola usa las tres cosas menos la limpieza.
- **Los textos en Core** (`SpanishText`, `RideText`, `RideGpx` en `VientoHud.Core.Text`; paso 7a-3): la recomendación
  ("Sal ahora" / "Mejor espera…"), el viento, la ventaja y los tramos, las rachas, la lluvia, la ciudad, la llegada, los
  resúmenes, y el nombre, el nombre de fichero y los avisos del GPX; en español, con la zona horaria como parámetro.
  La consola solo decide en qué línea va cada cosa.
- **Ruta trazada por el usuario** (`TracedRideAdvisor`, `TracedRequest`, `TracedRide`; paso 7a-2, `DECISIONS.md` 048):
  un punto es una ida y vuelta hasta él, dos o más una vuelta por ellos; **una petición** a BRouter y el mismo juicio
  que el planificador (viento en los dos sentidos, lluvia, salir ahora o esperar, ciudad), más los tramos prohibidos y
  a qué distancia pasa de cada punto. En la consola, `vientohud trace --via lat,lon[;lat,lon] ...` con un GPX.
- **La recomendación entera en Core** (`RideAdvisor`, `RideRequest`, `RideAdvice`; paso 7a-1): rutas, lluvia y hora de
  salida en una sola llamada, con avance por rumbo y cancelar; si la lluvia falla, sigue sin ella y dice por qué.
  `route` la usa.
- Nada todavía en la app. El entorno ya está listo (comprobado el 2026-10-19): .NET 10.0.401 con la parte de Android,
  Java 17, SDK de Android 35-37, emulador con móviles virtuales (Android 12-14) y la virtualización activa; la app de
  CieloHud se compiló para Android en este ordenador el 2026-10-14.
- Móvil del autor: OPPO CPH2699, Android 16 (API 36), arm64, conectado por USB con depuración. Tiene la app de
  Hammerhead (1.56.0), que acepta ficheros `application/gpx+xml` desde el botón de compartir, y Garmin Connect (5.30.1),
  que no sale al compartir un GPX pero sí lo abre ("Abrir con").

### Qué falta (fase 7)

La fase queda **abierta solo por el juez 7i** (decisión del autor, 2026-10-23): 7h se da por hecha con la pantalla
actual y el APK firmado de `DECISIONS.md` 052; el juez se hará en la primera salida real que pueda hacer el autor.

Primera división (`DECISIONS.md` 047) superada por la decisión 048 (2026-10-19): la ruta la traza el usuario en el
mapa. Lo hecho hasta entonces: 7a-1 (`RideAdvisor`, lo usa `route`) y la medida de 7b. División nueva, **pendiente del
visto bueno del autor**:

- [ ] 7a. Core para la app:
  - [x] 7a-1. La recomendación automática entera en Core (`RideAdvisor`); se queda para la consola.
  - [x] 7a-2. **Ruta trazada por el usuario**: de unos puntos (uno: ida y vuelta hasta ahí; dos o más: vuelta por ahí) a
    la ruta por BRouter en una petición, con las reglas de carretera, la distancia, el viento por tramos en los dos
    sentidos, la lluvia, salir ahora o esperar, la ciudad y la llegada. Todo son piezas que ya existen, enlazadas.
    Juez: tests con el motor falso, y en vivo desde la consola (un comando que recibe los puntos) en los sitios de
    prueba: segundos y el ojo del autor.
  - [x] 7a-3. Los textos en Core (recomendación, resumen de la ruta, nombre y descripción del GPX y del fichero).
- [x] 7b. Preparar Core para el móvil: borrar las imágenes viejas del radar (~2 MB cada una, hoy sin límite) y
  reintentar una vez las respuestas pasajeras de Open-Meteo (503 "sobrecargado", 429): el 2026-10-19 un 503 del viento
  tiró una recomendación entera. Hecho con Microsoft.Extensions.Http.Resilience y la lluvia reutilizada
  (`DECISIONS.md` 049).
- [x] 7c. **Elegir el mapa** (opciones comprobadas en su fuente: licencia, mapas de fondo y sus normas de uso, clave,
  mantenimiento; decisión del autor: Mapsui con OpenCycleMap, `DECISIONS.md` 050) y esqueleto de la app con el mapa y
  la estética de CieloHud; arranca en el emulador y en el móvil del autor.
- [x] 7d. Ubicación del móvil y la dirección: "hacia el oeste", el viento de las próximas horas y un abanico en el
  mapa. Una llamada a Open-Meteo: segundos. Hecho; **pendiente verlo en vivo con viento** (el 2026-10-19 Open-Meteo
  no tenía cupo: solo se vio la posición y el camino de error con REINTENTAR).
- [x] 7e. **Tocar el mapa**: un punto, ida y vuelta; dos o tres, vuelta. Distancia al momento, la ruta dibujada, y
  con ella el viento por tramos, la lluvia, salir ahora o esperar, el sentido mejor y la llegada. Juez: los mismos
  puntos en la consola dan lo mismo; segundos hasta la ruta. Hecho; **pendiente verlo en vivo con viento y lluvia**
  (cupo de Open-Meteo) y el ojo del autor en el móvil.
- [x] 7f. **Mandarla al aparato**, sea cual sea: botón GPX junto a DETALLE con un panel COMPARTIR (Hammerhead y la
  mayoría) / ABRIR CON… (Garmin Connect), apagados hasta tener viento y lluvia; el GPX es el mismo que escribe la consola (`RideGpx.ForTraced`, DECISIONS 054). Hecho y
  juzgado el 2026-10-21 en el móvil del autor: COMPARTIR deja la vuelta en el panel de Hammerhead (Karoo) y ABRIR CON…
  la abre en Garmin Connect (Fenix 5), en el mismo sentido que la app. Guardar en Descargas se deja para si algún aparato lo pide.
  La ruta lleva **marcas de sentido** (triángulos claros cada 2,5 km, `RouteMarks.Along`): el autor leyó las flechas del
  viento como el sentido de marcha al comparar con Garmin Connect (2026-10-21; iban igual).
  `docs/verificacion/2026-10-21-fase7-7e-emulador-marcas-de-sentido.png`.
  `docs/verificacion/2026-10-21-fase7-7f-emulador-gpx-botones.png`, `...-panel-gpx.png`.
- [x] 7g. Opciones con un toque: duración (1 h a 5 h) y velocidad media (18-33 km/h) en un panel propio, recordadas entre
  sesiones; el abanico, PROPONER y las horas de paso van con ellas. La hora de salida ya estaba (panel SALIDA).
  Desde el 2026-10-21 hora, duración y media van en una sola ficha "AHORA · 3 H · 28 KM/H" y un solo panel (la barra
  de abajo estaba llena; DECISIONS 054). `docs/verificacion/2026-10-21-fase7-7g-emulador-panel-opciones.png`.
  `docs/verificacion/2026-10-20-fase7-7g-emulador-duracion.png`. Pedido por el autor el 2026-10-20.
- [ ] 7h. Pantalla definitiva con la estética de CieloHud y versión instalable firmada.
- [ ] 7i. Juez de la fase: salidas reales usando solo la app (segundos hasta la ruta, datos gastados, GPX en el Karoo
  y en el Fenix, ojo del autor sobre el viento y la lluvia que hubo).
  - **Primera salida real** (2026-10-25, un amigo del autor, zona de pueblo A, con la app y el GPX en su Garmin): "funciona
    perfecto". Lo que decía el Garmin no coincidió con lo que encontraron en algún punto; ellos lo achacan a que fueron
    más despacio que la media elegida (las horas de paso, y con ellas el viento de cada tramo, se corren) y a que el
    viento pudo cambiar. Día de poco viento: no pone a prueba el juicio del viento. Sin medidas (segundos, datos, viento
    observado). Pendiente: salidas del autor con viento, y comparar con lo que marcaron las estaciones.

### Cómo se ha validado (fase 7)

- Paso 7f (2026-10-21): 1 test nuevo (632 en total): el GPX de una ruta trazada lleva el nombre "Sal 08:00 · 60 km E
  ida y vuelta", el fichero `ida-y-vuelta-60km-E-20260930-0800.gpx`, todos los puntos de la ruta y, en la descripción,
  el consejo (sal ahora, tipo y km, sentido y ventaja, llegada) y los créditos. La consola `trace` escribe ahora ese
  mismo GPX. En el emulador: con la ruta aparece GPX junto a DETALLE; su panel espera al viento y la lluvia y luego COMPARTIR
  abre la hoja del sistema con `ida-y-vuelta-15km-S-20261021-1020.gpx`; el fichero de la caché tiene nombre, descripción
  y créditos. Al cambiar la hora de salida nada se mueve abajo (antes los botones se iban y volvían). **En el móvil**: ABRIR CON… abre
  Garmin Connect con la vuelta de 89 km (mismo sentido que la app: horario; el GPX lo dice y el trazado lo confirma).
  2 tests nuevos (634): marcas cada 2,5 km con el rumbo del tramo, al revés si la ruta se recorre al revés.
- Paso 7a-1 (2026-10-19): 5 tests nuevos (596 en total): rutas, lluvia pedida una vez (también AROME, al pasar de 2 h)
  y las 9 salidas ordenadas, con cada opción apuntando a su rumbo y forma; sin radar, ordena igual y dice por qué; sin
  ninguna ruta válida, no pide la lluvia; con la salida más tarde, la lluvia se pide desde ahora; la hora tiene que ir en
  UTC. **Antes y después** con el BRouter local (60 km, salida a las 17:00, las dos versiones seguidas): ciudad 1 y
  ciudad 3 dan el mismo texto y los mismos GPX, byte a byte (6 y 12 ficheros). ciudad 3 busca a los lados: 121
  peticiones y 220-250 s incluso con el BRouter local.
- Paso 7a-2 (2026-10-19): 6 tests nuevos (602 en total): un punto hace una ida y vuelta con una sola petición (solo la
  ida; 20 km × 1,5 × 2 = 60 km, acaba en casa), 9 salidas y la lluvia pedida una vez; dos puntos hacen una vuelta
  casa-A-B-casa en una petición, recorrida como se trazó o al revés; las autovías se miden (km prohibidos), no se
  rechazan; sin radar, aconseja y dice por qué; un punto que BRouter no puede unir sale como `Unreachable`; hace falta un
  punto y las horas en UTC. **En vivo** con `trace` y el BRouter local: ciudad 1 ida y vuelta hacia el E (65 km, 20 % de
  ciudad) en **14 s** en total; pueblo B vuelta por pueblo L y pueblo M (49 km, 35 %, sentido horario por el viento);
  pueblo A ida y vuelta (56 km, 21 %). Una pasada cayó por un 503 pasajero de Open-Meteo en el viento
  (sin reintento; va a 7b). Los GPX quedan para el ojo del autor.
- Paso 7a-3 (2026-10-19): 6 tests nuevos (608 en total): "Sal ahora (10:30)" y "Mejor espera: sal a las 11:00 en vez
  de ahora; evitas 3 km con rachas de lado peligrosas y te ahorras 5,5 km de lluvia probable"; la ventaja de viento como
  se siente; las rachas de lado solo si las hay o no se conocen; números y puntos cardinales en español; el nombre del
  GPX y del fichero ("1-vuelta-60km-SO-20261019-1030.gpx", "-descartada"); los avisos como puntos con nombre corto.
  **Antes y después** con el BRouter local, las dos versiones seguidas: `trace` en pueblo B (vuelta) y ciudad 1 (ida y
  vuelta) y `route` en ciudad 1 dan el mismo texto y los mismos GPX, salvo un km contado de más en una lluvia por el minuto
  que pasa entre las dos pasadas (el horizonte de 3 h se mueve con la hora). Open-Meteo dio 503 a ratos toda la tarde y
  hubo que repetir la comparación tres veces: razón de más para el reintento de 7b.
- Flechas del viento (2026-10-19): 3 tests nuevos (629 en total): la flecha apunta hacia donde va el viento, con la
  cabeza echada hacia atrás 150° y 0,3 del largo; en casa, el viento medio o nada si se anula; a lo largo de una salida,
  una cada 5 km (km 2,5, 7,5…) con el viento del tramo y cómo se siente. En el emulador: la de casa en claro y las de la
  ruta en gris (viento flojo), `…7e-emulador-flecha-casa.png` y `…7e-emulador-flechas-ruta.png`, donde además se ve
  la vuelta pasando por pueblo I exacto con el perfil de `DECISIONS.md` 051.
- Paso 7e (2026-10-19): 1 test nuevo (625 en total): la ruta sola es una petición y no pide tiempo; el juicio encima
  da lo mismo que todo de una vez. **En el emulador** contra **brouter.de** (posición en pueblo A): un toque cerca de
  pueblo I → ida y vuelta de 13 km dibujada en 2-3 s (`…7e-emulador-ida-y-vuelta.png`); un segundo toque al oeste →
  vuelta de 10 km que no llega al segundo punto (`…7e-emulador-vuelta.png`): desde el ordenador brouter.de da lo mismo
  ("pasa a 2,96 km de un punto"): el punto cayó donde no hay asfalto y BRouter lo movió a la carretera más cercana. La
  app lo dice en el resumen de la ruta, que ahora se queda aunque el tiempo falle. El viento y la lluvia fallaron por
  el cupo de Open-Meteo. Más tarde volvió el cupo y, con los cálculos ya en un hilo aparte, el panel entero salió en
  vivo en el emulador ("Sal ahora (15:14)", ida y vuelta +6 km/h, km 0-11 flojo, lluvia no se espera, ciudad 46 %,
  11,1 km, llegada 15:41; `…7e-emulador-panel.png`) mientras se arrastraba el mapa sin que se bloqueara.
- Paso 7d (2026-10-19): 7 tests nuevos (624 en total): el abanico (25 puntos a 23 km de casa para 60 km, de 225° a
  315° alrededor de 270°, bien alrededor del norte, ninguno con viento flojo o variable); `DirectionAdvisor` pide las
  horas de la salida en el sitio (3 para 60 km a las 8:00, 4 desde las 8:50) y da rumbo y abanico, o ninguno con
  calma; hora en UTC; y los titulares "Hacia el SO", "Da igual hacia dónde", "Sin rumbo claro". **En el emulador**
  con la posición puesta en pueblo A y el permiso dado: punto ámbar en pueblo A, mapa centrado, "Sin previsión de
  viento: … Daily API request limit exceeded" y REINTENTAR (`docs/verificacion/2026-10-19-fase7-7d-emulador.png`).
  **En el móvil**: el permiso se pidió y el autor lo dio; captura pendiente (el móvil se bloquea solo). El viento y el
  abanico en vivo quedan para cuando Open-Meteo vuelva a dar cupo. Pendiente de pulir: el mensaje de error es el
  técnico de Core, en inglés; en 7h habrá que darle una frase llana.
- Paso 7c (2026-10-19): sin tests (no hay lógica en la app; la solución entera compila y los 617 de Core pasan con el
  proyecto nuevo dentro). **En el emulador** (Pixel, Android 14): pantalla de arranque con el icono, y el mapa de
  OpenCycleMap centrado en ciudad 1 con relieve, pueblos y la ruta ciclista 1, la cabecera "VIENTOHUD · OPENCYCLEMAP" y la
  atribución en el pie; estable (dos capturas iguales con 10 s entre ellas). Captura en
  `docs/verificacion/2026-10-19-fase7-7c-emulador.png`. **En el móvil del autor** (OPPO, Android 16): lo mismo, estable
  (`docs/verificacion/2026-10-19-fase7-7c-movil.png`); la app se abre detrás de la pantalla de bloqueo si el móvil
  está dormido, como cualquier otra. Una tesela de prueba pedida con la clave desde el ordenador:
  HTTP 200, PNG de 22 KB. En la primera instalación el mapa se fue solo hacia el este; tras cerrar y abrir la app no
  volvió a pasar (lanzador `monkey` durante la instalación); se vigila en el móvil. La atribución salía dos veces
  (la caja blanca de Mapsui y la nuestra): se apaga la de Mapsui y queda la nuestra, con enlace al tocarla.
- Paso 7b (2026-10-19): 9 tests nuevos (617 en total). Reintentos: un 503 y un 502 seguidos de un 200 no se notan
  (3 peticiones); tras 3 reintentos el fallo vuelve tal cual (4 peticiones); un 403 (brouter.de) y un 429 (cupo de
  Open-Meteo) no se reintentan; el cliente lleva el agente y los 60 s. Lluvia reutilizada: a menos de 1 km, menos de
  10 min y dentro del alcance y las horas, la misma; pide 10 km y 1 h de más; vuelve a pedir al moverse 5 km, a los 11
  min, con más alcance o más horas; nunca sirve una hecha después de la hora pedida. Radar: al guardar un cuadro con
  `CacheMaxAge` de 3 h se borra el de hace 4 h y la carpeta vacía, y se queda el de hace 1 h. **En vivo**: `radar`
  (OPERA) funciona a través del cliente nuevo; `trace` no se pudo probar porque se agotó el cupo diario de Open-Meteo
  (el reintento de aquel 429 esperó 14 s para nada: por eso el 429 se quita). Queda probar `trace` mañana.
- Paso 7b, medida (2026-10-19; `brouterspeed.cs`, `requests.cs`, `raintime.cs`;
  `docs/verificacion/2026-10-19-fase7-peticiones.txt`): una petición a brouter.de tarda lo mismo que al local
  (0,2-2,5 s). El tiempo es el número de peticiones: 40 (pueblo A), 43 (ciudad 1), 56 (pueblo B, pueblo C), 121
  (ciudad 3, buscando a los lados). Solo 2 de 316 piden los mismos puntos que otra: no hay nada que ahorrar guardando
  respuestas. Las idas y vueltas (5 variantes del rumbo, `DECISIONS.md` 044) se llevan dos tercios; las vueltas que no
  salen válidas gastan 12 cada una. La lluvia tarda 1-9 s pero baja ~8,7 MB de radar cuando los cuadros son nuevos, y
  AROME cuesta 225 llamadas a Open-Meteo por recomendación (tres seguidas en un minuto dan 429).

### Riesgos y pendientes (fase 7)

- **APK repartido** (`DECISIONS.md` 052): la clave de Thunderforest va dentro y consume del plan gratuito del autor;
  la clave de firma está solo en `%USERPROFILE%.vientohud` (sin copia de seguridad no hay actualizaciones). Las
  opiniones de los amigos van al ojo del autor y al juez de la fase (7i).

- **La espera de la recomendación automática** (medida en 7b): de ~50 s (ciudad 1, pueblo A) a ~4 min (ciudad 3) con
  brouter.de, la mitad por la espera de 1 s entre peticiones que nos imponemos (brouter.de no publica normas y nos
  bloqueó dos veces). Lo que en `DECISIONS.md` 044 ponía "~2 min" solo contaba esa espera. Resuelto para la app por
  otro camino: la ruta la traza el usuario y cuesta una petición (`DECISIONS.md` 048).
- Datos en el móvil: ~8,7 MB de radar por recomendación cuando los cuadros son nuevos; AROME, 225 llamadas a Open-Meteo
  (reutilizadas 10 min desde 7b).
- **Cupo diario de Open-Meteo** (10.000 llamadas por conexión): se agotó el 2026-10-19 con los jueces y las pruebas. Una
  recomendación nueva cuesta ~250 y cada punto nuevo ~25: de sobra para una persona, pero no se pueden hacer jueces y
  pruebas en vivo el mismo día. Pendiente: probar `trace` con el cliente nuevo (7b) en cuanto vuelva el cupo.
- Las imágenes del radar (~2 MB cada una) se guardan sin límite: en el móvil hay que borrar las viejas (7b).
- Android puede cortar una búsqueda larga si la pantalla se apaga o se cambia de app (ColorOS de OPPO es estricto).
  Menos grave desde `DECISIONS.md` 048: una ruta trazada son segundos.
- **Ojo del autor en el móvil** (2026-10-19, punto cerca de pueblo J): la ruta se pintó, pero al hacer zoom la app se
  quedó colgada y Android la mató (ANR: 5 s sin atender la pantalla, 133 % de CPU). Causa: los cálculos de Core (leer
  los cuadros del radar, su movimiento, puntuar la ruta en 9 salidas y dos sentidos) corrían en el hilo de la pantalla.
  Arreglado: van en un hilo aparte (`Task.Run`). Queda por confirmar en el móvil que el zoom responde mientras calcula.
- Un punto tocado donde no hay asfalto se mueve (BRouter) a la carretera más cercana, a veces a 2-3 km (el autor lo
  vio dos veces el 2026-10-19, en pueblo J y pueblo K; comprobado desde el ordenador: en pueblo K el perfil
  "trekking" de BRouter remata por un camino de tierra). Desde entonces la app pinta dónde pasa de verdad la ruta
  (`TracedRoute.Reached`: punto relleno) unido al tocado (hueco) con una línea fina, y el resumen dice "(no hay asfalto
  más cerca)". **Y la causa de fondo** (`DECISIONS.md` 051): el perfil de la fase 2 recortaba los desvíos a los puntos de
  paso; las rutas trazadas usan ahora la variante `StrictRoadProfileKeepingViaPoints` y pasan por los puntos. Queda el
  ojo del autor.
- Thunderforest "se reserva retirar el plan gratuito": si pasa, la app sigue con OpenStreetMap (sin relieve ni firme)
  hasta cambiar de proveedor; es una línea.
- Las teselas ya vistas del mapa se guardan en la caché del móvil (Mapsui/BruTile `FileCache`), con tope de 50 MB desde
  la fase 9 (paso 3); Android puede vaciarla.

Fase 6 — Mejores rutas: terminada el 2026-10-19 (pasos 6a a 6d hechos, 6e probado y retirado, juez ejecutado; el autor la
cierra, `DECISIONS.md` 046).

