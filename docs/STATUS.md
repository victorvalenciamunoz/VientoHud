# Estado

**Fase actual: 6 — Mejores rutas** (en curso: 6a a 6c hechos y 6d a falta del ojo del autor; añadida por el autor el 2026-10-19, `DECISIONS.md` 039)

## Fase 6

### Qué funciona (fase 6)

- **Ida y vuelta** (`RouteShape.OutAndBack`, `Route.ThereAndBack`; `DECISIONS.md` 040): el generador hace también idas y
  vueltas: la ida de BRouter hasta un punto en el rumbo y la misma carretera de vuelta, con la distancia ajustada moviendo
  el punto y las mismas reglas que una vuelta.
- **En la recomendación** (`RidePlanner`; `DECISIONS.md` 041): una vuelta y una ida y vuelta por rumbo, en el mismo orden;
  `route` las llama "ida y vuelta" en el texto, el GPX y el nombre del fichero.

### Qué falta (fase 6)

División aceptada por el autor el 2026-10-19:

- [x] 6a. Ida y vuelta en el generador.
- [x] 6b. Ida y vuelta en la recomendación: **una por rumbo buscado** (decisión del autor, `DECISIONS.md` 041) junto a
  las vueltas, en el mismo orden (rachas, lluvia, viento). **Ojo del autor** (2026-10-19): la de León está bien; la de
  Humanes hacia el NE va casi toda por ciudad o cerca de ciudades (Fuenlabrada, Getafe): es lo que deben arreglar 6c y 6d.
- [x] 6c. Medir las ciudades: km por ciudad de cada
  ruta y si vuelve a pasar cerca de la salida a mitad de camino, en las salidas de prueba, antes de cambiar nada
  (`DECISIONS.md` 042).
- [ ] 6d. Evitar ciudades (generador y perfil de BRouter) y juez de la fase: antes y después, en números y a ojo; los
  límites, con los datos del 6c. Hecho (`DECISIONS.md` 043, **decisión del autor**: la ciudad entre la lluvia y el viento,
  empate por debajo de 10 km); **falta el ojo del autor** y decidir qué hacer cuando todos los rumbos van a ciudad.

### Cómo se ha validado (fase 6)

- Paso 6a (2026-10-19): 5 tests nuevos (580 en total). Ida y vuelta: la misma carretera en los dos sentidos, doble de
  distancia, acaba en la salida, desnivel de vuelta (50 m de subida con 30 m de diferencia: 20 m de vuelta). Generador:
  un punto a 60 / 2 / 1,3 ≈ 23 km en el rumbo pedido, una sola petición, válida y repitiendo más del 90 % sin ser ramal;
  con carreteras más largas mueve el punto hasta encajar la distancia; si no se llega, gira el rumbo; una por rumbo. **En
  vivo** (`outandback.cs`, BRouter local, 60 km): 11 de 12 válidas en León, La Bañeza, Humanes y Tres Cantos.
- Paso 6b (2026-10-19): los tests del planificador cuentan ya vuelta e ida y vuelta por rumbo (580 en total); la ida y
  vuelta contra el viento sale primera con viento claro. **En vivo** con `route` (BRouter local, 60 km): Humanes, viento
  del NE a 18 km/h, primera la ida y vuelta al NE (+20 km/h de ventaja frente a +4 de la mejor vuelta; dos vueltas no
  válidas, las tres idas y vueltas sí); León, viento flojo, gana una ida y vuelta al N por poco.
- Paso 6c (2026-10-19): 6 tests nuevos (586 en total): km con 2 cruces o semáforos, o media calle, en ciudad; tramos
  seguidos; etiquetas `crossing` solas; un ocho que pasa por casa a mitad (6 km); los nodos de la vuelta de León grabada
  (más del 30 % en ciudad) y al dar la vuelta a la ruta. **Medida en vivo** (`urban.cs`): Humanes 31-97 %, Fuenlabrada
  48-90 %, Tres Cantos 14-59 %, León 17-36 %, La Bañeza 10-30 %.
  Ojo del autor en Fuenlabrada: la medida ordena bien; sus salidas reales dan 4-56 % (Humanes 73 km, 41 %).
- Paso 6d (2026-10-19): 3 tests nuevos (589 en total): menos ciudad gana a mejor viento; menos de 10 km de diferencia es
  empate y decide el viento; la lluvia va antes que la ciudad. Perfil nuevo: media de ciudad 42 → 37 % en las mismas 30
  salidas. En vivo: Humanes, primera la ida y vuelta al E (28 %) en vez de la del NE (80 %); León, la ida y vuelta al
  ESE (15 %); Fuenlabrada, todas 61-90 % (los rumbos contra el viento del NE van a Madrid).


Fase 5 — Combinarlo: terminada el 2026-10-19 (pasos 5a a 5e hechos y el juez ejecutado; el autor la da por buena).

## Fase 5

### Qué funciona (fase 5)

- **Lluvia en la ruta** (`RainOutlook`, `RouteRain`; `DECISIONS.md` 033-034): radar hasta 2 h, mezcla con AROME HD 15 min
  en zona hasta 3 h, aviso para la zona después; reserva de `best_match` fuera de AROME.
- **Orden con lluvia y viento** (`RideRanking.Order`; `DECISIONS.md` 035) y **salir ahora o esperar**
  (`DepartureChoice`; `DECISIONS.md` 036).
- **`route` con todo** (`DECISIONS.md` 037): la recomendación arriba ("Sal ahora" o "Mejor espera: sal a las 11:00…"),
  cada vuelta con viento, lluvia, rachas y llegada, y un GPX por vuelta con nombre corto, el resumen en la descripción y
  avisos en puntos de la ruta (`RideAlerts`, `GpxWaypoint`).

### Qué falta (fase 5)

División aceptada por el autor el 2026-10-17:

- [ ] 5a. Lluvia a lo largo de una ruta: probabilidad en cada tramo de 1 km a la hora de paso; radar hasta 90 min y
  después un modelo. Fuente elegida por el autor: AROME HD 15 min mirando una zona, con la probabilidad de `best_match`
  de reserva, y paso del radar a AROME entre 60 y 90 min (`DECISIONS.md` 033). División aceptada el 2026-10-17:
  - [x] 5a-1. Core: previsión de AROME en una rejilla (ahora, archivo y pasada concreta), probabilidad en zona con
    margen de tiempo y probabilidad de reserva.
  - [x] 5a-2. Juez de AROME en zona con pasadas antiguas: 20 días de lluvia (cabían más que los 12 acordados con el mismo
    gasto). **Decisión del autor:** radar hasta 2 h, mezcla hasta 3 h, AROME en zona (15 km, ±30 min) después, y más
    allá de 3 h solo un aviso para la zona (`DECISIONS.md` 034).
  - [x] 5a-3. Lluvia en cada tramo de una ruta a la hora de paso (radar, mezcla, AROME, reserva) y aviso para la zona.
- [x] 5a terminado (2026-10-18).
- [x] 5b. Puntuar y ordenar las vueltas con lluvia y viento. **Decisión del autor:** primero menos km con racha de lado
  peligrosa, luego menos km con lluvia "probable" (menos de 5 km de diferencia es empate), luego mejor ventaja de viento
  (`DECISIONS.md` 035).
- [x] 5c. ¿Salir ahora o esperar? Las mismas vueltas saliendo ahora, en 15 min, 30… **hasta 2 h** (decisión del autor).
  Esperar solo para mojarse menos (5 km o más de lluvia probable) o evitar rachas peligrosas, no por el viento solo
  (**decisión del autor**, `DECISIONS.md` 036).
- [x] 5d. Consola y GPX: la recomendación final; el GPX con nombre y descripción con el resumen y **avisos en puntos de
  la ruta** (decisión del autor: probar si el Karoo los muestra). División y decisiones del autor del 2026-10-18
  (`DECISIONS.md` 037): se amplía `route`; avisos de rachas, lluvia probable y aviso para la zona (como mucho 10); nombre
  corto "Sal 10:30 · 60 km SO".
  - [x] 5d-1. Core: puntos de aviso en el GPX y los avisos de una vuelta.
  - [x] 5d-2. Consola: `route` con lluvia, orden y salir ahora o esperar; GPX con todo; prueba en vivo y en el Karoo
    (2026-10-18: el Karoo no muestra los avisos ni el nombre de dentro del GPX; **el autor lo deja así**, es secundario).
- [x] 5e. Juez: en días pasados, lo que habría recomendado con lo que se sabía entonces frente a lo que pasó (radar y
  METAR después), comparado con salir ya con una vuelta cualquiera. Sitios del autor: La Bañeza y Humanes de Madrid
  (`DECISIONS.md` 038). **El autor la da por buena** (2026-10-19).

### Cómo se ha validado (fase 5)

- Paso 5a-1 (2026-10-17): 7 tests nuevos (546 en total). Rejilla de 5 × 5 puntos cada 5 km con Barajas en el centro,
  ordenada de norte a sur y de oeste a este; probabilidad como parte de puntos y cuartos de hora con lluvia, con y sin
  margen de tiempo, dejando fuera los puntos lejanos; fuera de la rejilla o más allá de la previsión, desconocida; por
  debajo de 0,2 mm/h, seco. Con respuestas reales grabadas: la pasada de AROME HD del 6 oct. a las 12:00 UTC en 3 × 3
  puntos alrededor de Barajas (sin fechas en la petición; dura 6 h; 1,2 mm/h de 13:15 a 13:30; probabilidad > 30 % a
  las 13:30 y < 10 % a las 17:00), la previsión de ahora con fechas y la probabilidad por horas de `best_match`.
- Paso 5a-2 (2026-10-17), **juez de AROME en zona** (`judge5a.cs`, ~80 min, 5.994 llamadas a Open-Meteo):
  los 10 días más lluviosos de Barajas y de León, 4 salidas por día tras cada pasada archivada (cada 3 h), plazos de
  1 a 4,5 h, 79 casos por plazo. Tabla y conclusiones en `DECISIONS.md` 034: la zona mejora a AROME en un punto, el
  radar movido gana a los dos hasta las 3 h y más allá AROME no gana a decir la probabilidad media de esos días.
- Paso 5a-3 (2026-10-18): 11 tests nuevos (557 en total). Radar hasta 2 h; mezcla a medias a las 2 h 30 y solo AROME a
  las 3 h; más allá, sin contar; sin radar, AROME; sin AROME, la reserva (también en la mezcla); nada, desconocido; lluvia
  que llega movida por el radar; aviso para la zona desde la primera hora con un 20 % (AROME) y con la reserva cuando
  AROME acaba; una sola petición a cada fuente y AROME solo para salidas de más de 2 h (15 × 15 puntos con 20 km de
  alcance, nunca más de 225); fuera de AROME, la reserva; en la vuelta norte de León (fixture), tramos con radar, mezcla y
  sin contar, y aviso. **En vivo con días pasados** (`tools/investigacion/rainroute.cs`, con lo que se sabía entonces):
  salida del autor de Humanes (73 km) el 6 oct. a las 13:00 UTC: seca hasta el km 45 como vio el radar; al final "puede"
  (20-22 %) donde el radar vio lluvia (km 65-73). La Bañeza (96 km) el 30 sept. a las 06:00 UTC con el frente: "probable"
  en los km 20-35 (06:50-07:15) y el radar vio lluvia en los km 25-30 a las 07:02 (5 de los 11 km "probables" se mojaron,
  ninguno fuera); desde el km 75, sin contar y aviso para la zona desde las 09:00 (25 %, de la reserva).
- Paso 5b (2026-10-18): 5 tests nuevos (562 en total). La más seca va antes que la de mejor viento y las rachas antes que
  la lluvia; 4 km de diferencia de lluvia es empate y decide el viento; 0, 4 y 8 km: 0 y 4 empatan y 8 va detrás aunque
  tenga el mejor viento; sin lluvia, el orden de la fase 3; con lluvia al norte de León, el rectángulo que va al sur pasa
  delante del que va al norte, que tenía mejor viento. `RidePlanner` acepta el panorama de lluvia y lo pasa al orden.
- Paso 5c (2026-10-18): 7 tests nuevos (569 en total). Salidas cada 15 min hasta 2 h; con una banda de lluvia encima que
  se va al este, espera (15-75 min) y se ahorra 5 km o más de lluvia; seco con mejor viento más tarde, sale ahora; lluvia
  que viene, sale ahora; en la ida y vuelta de La Bañeza con chubascos lejos de casa de 2 a 3 h desde ahora, la salida 2 h
  más tarde no gana por tener sin contar la parte lejana; rachas de 60 km/h las dos primeras horas, espera y se las
  ahorra; sin vueltas, nada que elegir. `RideRanking.Rank` puntúa con un campo de viento ya pedido.
- Paso 5e (2026-10-19), **juez de la fase 5** (`tools/investigacion/judge5.cs`, BRouter local; resultados en
  `docs/verificacion/2026-10-19-fase5-juez.*`): 7 días con lluvia por sitio, decisiones a las 9:00 y 12:00, 60 km a 25 km/h.
  La primera pasada perdió 10 casos por el límite por minuto de Open-Meteo; el juez ahora espacia las peticiones (≤ 500
  llamadas/min) y reintenta tras un 429, y la segunda pasada salió completa con la caché. 24 casos (4 de Humanes sin
  vuelta válida):

  | Sitio | Casos | Esperó | Mojado app | App saliendo ya | Sin mirar | App ≤ sin mirar | Ventaja app | Ventaja sin mirar |
  |---|---|---|---|---|---|---|---|---|
  | La Bañeza | 14 | 4 | 13,8 km | 15,1 km | 15,7 km | 86 % | +9,0 km/h | +8,3 km/h |
  | Humanes | 10 | 2 | 7,1 km | 10,5 km | 8,1 km | 70 % | +0,7 km/h | −2,4 km/h |
  | **Los dos** | **24** | **6** | **11,0 km** | **13,2 km** | **12,5 km** | **79 %** | **+5,5 km/h** | **+3,8 km/h** |

  Km mojados según el radar a la hora de paso; ventaja de viento real con el METAR. Esperar ayudó en 5 de 6 (Humanes 3 ago.:
  13 km en vez de 45) y empeoró en 1. Ganancia pequeña y muestra corta; los casos caros son tormentas de verano que nacen
  en el sitio y lluvia en toda la zona (lectura en `DECISIONS.md` 038).
- Paso 5d-2 (2026-10-18): 1 test nuevo (575 en total): las salidas tardías caen en cuartos de hora en punto (sin los
  que caen a menos de 5 min de la primera). La consola no tiene tests: probada en vivo con el BRouter local. León, 60 km,
  día seco: "Sal ahora (10:13)", tres vueltas sin lluvia y la tercera con un aviso de racha de lado en el GPX ("Racha
  lado 41", km 24-26). Tres Cantos, 60 km, con lluvia en el centro: "Mejor espera: sal a las 11:00 en vez de ahora; te
  ahorras 21,2 km de lluvia probable"; aun así, lluvia probable en los km 9-41 hacia las 11:22 (75 %), con el aviso
  "Lluvia ~11:22" en el GPX. El GPX lleva el nombre corto ("Sal 11:00 · 54 km ESE"), la descripción con la
  recomendación, el viento por tramos, la lluvia, las rachas, el resumen de la vuelta y las atribuciones.
- Paso 5d-1 (2026-10-18): 5 tests nuevos (574 en total). GPX con puntos de aviso antes del track, válido con el esquema
  oficial, con nombre, descripción y símbolo. Avisos: dónde empieza cada tramo de lluvia probable (con su máximo), las
  rachas y el aviso para la zona en orden de marcha; "puede" no marca nada; con demasiados, primero las rachas y la lluvia
  más larga.

### Riesgos y pendientes (fase 5)

- **Fase 5: lluvia que nadie prevé** (juez 5e): en 6 de 24 casos se mojó 10 km o más con 0 km de lluvia probable prevista
  (tormentas que nacen en el sitio; más allá de 2 h, AROME no afina). Y con lluvia en toda la zona ninguna vuelta ni
  espera de 2 h la evita: la app podría decirlo ("lloverá en cualquier vuelta") en vez de ordenar.
- **Todos los rumbos hacia la ciudad** (fase 6): los rumbos se eligen por el viento; si todos van hacia una ciudad
  (Fuenlabrada con viento del NE, hacia Madrid), el orden solo elige la menos mala.
- **Humanes sin vuelta válida** en 4 de 14 casos: el generador de la fase 2 en el suroeste de Madrid.

Fase 4 — Radar y lluvia a corto plazo: terminada el 2026-10-17 (pasos 4a a 4e hechos y el juez ejecutado; el autor elige el
círculo de la previsión; 4f no hace falta).

## Fase 4

### Qué funciona (fase 4)

- Fuente de radar elegida: **OPERA** de EUMETNET (`DECISIONS.md` 025): sin clave, CC BY 4.0, reflectividad cada
  5 min a 1 km y lluvia en mm/h cada 15 min a 2 km, con histórico desde 2012 y Madrid y León cubiertos.
- **Rejilla de lluvia** (`RainGrid`; `DECISIONS.md` 026): un cuadrado de celdas alrededor de un punto, en la
  proyección del radar (fila 0 al norte, columna 0 al oeste), con la lluvia en mm/h de cada celda: 0 si está seco,
  NaN si no hay datos. Sabe en qué celda cae un sitio, dónde está el centro de cada celda y la lluvia en un sitio.
  `LambertAzimuthalEqualArea`: la proyección de OPERA, ida y vuelta. `Reflectivity`: de dBZ a mm/h (Marshall-Palmer,
  tope en 55 dBZ).
- **Lector de OPERA** (`OdimComposite`): lee de un fichero HDF5 de OPERA solo el cuadrado alrededor del punto
  (reflectividad DBZH o lluvia RATE), con PureHDF. Un cuadrado de 300 km a 1 km tarda 1-4 s.
- **Fuente de OPERA** (`IRadarSource`, `OperaRadarSource`): la hora de la última imagen publicada y la imagen de una
  hora dada, recortada (bucket de 24 h o histórico según la edad, y el otro si no está; null si falta la imagen).
  Caché opcional en disco. Sin clave. Atribución "Radar: EUMETNET OPERA (CC BY 4.0)".
- **Lluvia alrededor** (`RainNearby`, `RainIntensities`; `DECISIONS.md` 027): de una imagen, la lluvia en el punto, la
  más cercana y la más fuerte (km y rumbo HACIA ella), y cuánta zona tiene datos y moja; cuenta como lluvia desde
  12 dBZ (≈ 0,2 mm/h). Intensidad con los umbrales de AEMET. `RadarFrames`: la imagen de una hora (redondeada hacia
  atrás) o hasta tres anteriores si falta.
- **Imagen PNG** (`RainImage`, `PngWriter`): la rejilla con los colores de la leyenda de AEMET, negro seco, gris sin
  datos, círculos cada 50 km y una cruz en la salida; PNG escrito sin paquetes.
- **Comando `radar`**: `radar [--lat --lon] [--at "aaaa-mm-dd hh:mm"] [--radius 20-300] [--out carpeta]`. Sin `--at`,
  la última imagen. Dice la hora (local) y la antigüedad de la imagen, la lluvia en el punto, la más cercana y la más
  fuerte, y escribe `radar-aaaammdd-hhmm.png`. Caché en `%LOCALAPPDATA%\VientoHud\radar`.
- **Movimiento entre dos imágenes** (`RainMotions`, `RainMotion`; `DECISIONS.md` 028): un desplazamiento para toda la
  zona, el que más parecidas hace las dos imágenes (forma de la lluvia, no cantidad), de grueso a fino y afinado por
  debajo de la celda. Da la velocidad hacia el este y el norte, la velocidad y el rumbo HACIA donde va, y el parecido
  con y sin moverla. "No sé" con poca lluvia, poco parecido o al borde de la búsqueda (150 km/h).
- **Movimiento de la última media hora** (`RainMotions.Combine`, `RadarFrames.GetMotionFramesAsync`; `DECISIONS.md`
  029): cuatro imágenes cada 10 min, la mediana de sus 6 parejas si al menos la mitad dan movimiento y coinciden.
  `RainMotions.Similarity`: el parecido para un desplazamiento dado (para comprobar). `radar` dice "La lluvia va hacia
  el ENE (57°) a 39 km/h (6 de 6 parejas…)" o por qué no se sabe, y el PNG lleva una flecha hasta donde llega la lluvia
  en una hora.
- **Previsión en un punto** (`Nowcast`, `PointNowcast`, `RainLikelihoods`; `DECISIONS.md` 030): cada 5 min hasta
  90 desde ahora, la probabilidad de lluvia (parte del círculo con lluvia viento arriba, de 4 km más 15 km por hora,
  elegido con el juez: `DECISIONS.md` 032),
  en palabras (no, puede, probable: cortes del autor en 20 % y 50 %), y la hora a la que llega o se va la lluvia. Sin
  movimiento, la lluvia se queda quieta. `Nowcast.Advect`: la imagen entera movida hacia delante.
- **`radar` con previsión**: "Previsión aquí, desde las 15:30: ahora no (0 %); +30 min …; +60; +90" y "La lluvia llega
  hacia las …", "Deja de llover hacia las …" o que no se espera; con movimiento conocido, guarda además las imágenes
  previstas a +30 y +60 min (`radar-aaaammdd-hhmm-mas30.png`).
- **Piezas del juez** (`DECISIONS.md` 031): lluvia de los METAR (`IemPrecipitationObservationSource`,
  `MetarWeather.HasPrecipitation`: en el aeropuerto, no "en las cercanías" ni "reciente"); previsión pasada de AROME HD
  cada 15 min (`OpenMeteoPastRainForecastSource`, varios sitios en una llamada, en mm/h); medidas (`RainScores`: cuánta
  lluvia anunció, cuántas veces llovió con "probable" y con "no", fiabilidad de los porcentajes y Brier).

### Qué falta (fase 4)

División aceptada por el autor el 2026-10-16:

- [x] 4a. Investigar el radar de AEMET y las alternativas; decidir la fuente (`DECISIONS.md` 025).
- [x] 4b. Una imagen (**decisiones del autor**: PureHDF, reflectividad cada 5 min, texto + PNG; `DECISIONS.md` 026):
  - [x] 4b-1. Core: descargar una imagen de OPERA (la última o la de una hora pasada) y recortar la zona alrededor de
    la salida (~150 km) como rejilla de lluvia en mm/h, distinguiendo "seco" de "sin datos".
  - [x] 4b-2. Consola `radar`: si llueve en el punto, dónde está la lluvia más cercana (km y hacia dónde) y cuánta
    zona tiene datos, y un PNG de la zona con una cruz en la salida. Comprobación en 3-4 momentos de lluvia en Madrid
    y León: a ojo frente al mapa de radar de la web de AEMET y frente a la lluvia de los METAR.
- [x] 4c. Movimiento (división aceptada por el autor el 2026-10-16):
  - [x] 4c-1. Core: el movimiento entre dos imágenes (un desplazamiento para toda la zona, por correlación).
  - [x] 4c-2. Varias imágenes de la última media hora y su acuerdo; `radar` dice hacia dónde va la lluvia y pinta una
    flecha. Comprobación en los días de lluvia ya vistos: serie de PNG para el ojo del autor, mover la imagen de hace
    30 min y ver que se parece más a la de ahora que dejarla quieta, y frente al viento a 700 hPa de Open-Meteo.
- [x] 4d. Proyección (división aceptada por el autor el 2026-10-16; palabra y porcentaje, **decisión del autor**):
  - [x] 4d-1. Core: probabilidad de lluvia en un punto cada 5 min hasta 90, hora de llegada y de fin, imagen movida.
  - [x] 4d-2. `radar` dice la lluvia prevista a +30, +60 y +90 y cuándo llega o se va, y guarda las imágenes previstas.
    Comprobación en momentos pasados en Barajas y León frente a lo que vio el radar después y frente al METAR.
- [x] 4e. Juez (backtest; `DECISIONS.md` 031, **decisiones del autor**: contra el radar y contra el METAR, lluvia desde
  0,2 mm/h, días de lluvia de los últimos 3 meses en Madrid y León, rivales "nada se mueve" y AROME HD 15 min):
  - [x] 4e-1. Core: lluvia de los METAR (IEM), previsión pasada de AROME HD cada 15 min (Open-Meteo) y las medidas.
  - [x] 4e-2. Pasar el juez y sacar las tablas (2026-10-17). El autor elige el círculo de 4 km + 15 km/h (`DECISIONS.md` 032).
- [ ] 4f. Movimiento por zonas: **no hace falta por ahora** (2026-10-17). El desplazamiento único gana a "nada se mueve" y a
  AROME en todos los plazos y coincide con el viento a 700 hPa; los fallos que se ven son lluvia que nace en el sitio y
  lluvia que el radar no ve, que el movimiento por zonas no arregla. Queda como mejora.

### Cómo se ha validado (fase 4)

- Paso 4a (2026-10-16): documentación oficial (EUMETNET Open Radar Data, especificación de AEMET OpenData y su FAQ,
  nota legal de AEMET, catálogos de datos.gob.es y MITECO, API de RainViewer, Open-Meteo) y llamadas reales: listado
  y descarga anónimos de OPERA (hoy y un día de enero de 2025 en el histórico), lectura de un fichero RATE desde un
  script de .NET 10 con PureHDF (0,4 s; Madrid y León con datos válidos, A Coruña sin datos); AEMET OpenData sin clave
  responde 200 con cuerpo vacío; descarga GeoTIFF de la web de AEMET (composición de 692×497 px, últimas 4 h); RainViewer
  (13 imágenes de pasado, sin previsión); Open-Meteo AROME HD cada 15 min con datos en Madrid y León y sin datos en
  Sevilla. Comparación completa en `DECISIONS.md` 025.
- Paso 4a, comprobación con la clave del autor (2026-10-16): el radar regional de Madrid de AEMET OpenData es un GIF
  de 480×530 px (10 KB) con las fronteras provinciales, el logo, la leyenda de colores y la fecha dibujados encima; sus
  metadatos dicen "image/gif, cada 10 minutos". La composición nacional respondió 404 en ese momento. Confirma que no
  sirve como datos.
- Paso 4b-1 (2026-10-16): 49 tests nuevos (430 en total). Proyección: las cuatro esquinas de la rejilla de OPERA
  (calculadas por EUMETNET con PROJ) forman un rectángulo de 3.800 × 4.400 km al metro; ida y vuelta al centímetro en
  Madrid, León, Canarias, Reikiavik, Moscú y Ciudad del Cabo; conserva el área; rechaza otras proyecciones.
  Marshall-Palmer: 20, 30, 40 y 50 dBZ dan 0,65, 2,7, 11,5 y 48,6 mm/h; tope en 55 dBZ. Rejilla: celda de un sitio,
  norte arriba y oeste a la izquierda, centros a 1 km, fuera de la rejilla nada. Con los ficheros reales del 3 oct. a
  las 6:00 UTC (chubasco en Barajas según el METAR; fixtures): cuadrado de 301 × 301 celdas de 1 km con Madrid en el
  centro, lluvia en Barajas (1,0 mm/h), sin huecos y con el 26 % mojado; el producto RATE en 151 × 151 celdas de
  2 km; en la esquina del compuesto y en Nueva York, sin datos; un fichero que no es HDF5 da error de radar. Fuente:
  con listados reales, la última imagen (11:00 DBZH y 10:45 RATE a las 11:07), también pasada la medianoche y con
  listados en varias páginas; imagen vieja del histórico, reciente del bucket de 24 h y luego del histórico, falta en
  los dos (null), fichero de otra hora (error), fallos de red y servidor, horas que no son de imagen, caché.
  **En vivo** (`tools/investigacion/radarlive.cs`): Madrid a las 11:13 UTC, última imagen de las 11:05 (8 min de
  retraso), seco, 3 s; León el 3 oct. a las 10:35 (METAR: chubasco fuerte), 1,8 mm/h en el punto y el 39 % mojado.
- Paso 4b-2 (2026-10-16): 36 tests nuevos (466 en total): lluvia más cercana y más fuerte con su distancia y rumbo, la
  del punto a distancia 0, ecos por debajo de 12 dBZ no cuentan, celdas sin datos fuera de la cuenta; umbrales de
  intensidad de AEMET; de mm/h a dBZ y vuelta; colores de cada escalón de la leyenda, tamaño de la imagen (2 píxeles por
  km), norte arriba, cruz y círculo de 50 km; el PNG se vuelve a leer (firma, CRC de cada trozo con otro cálculo, píxeles
  iguales); imagen redondeada hacia atrás y hasta tres anteriores si falta. La consola no tiene tests: probada en vivo.
  **Frente a los METAR** (`radar` en el aeropuerto; hora UTC):

  | Momento | METAR | `radar` en el aeropuerto |
  |---|---|---|
  | Barajas, 3 oct. 06:00 | SHRA (chubasco) | llueve, débil (1,0 mm/h); lo más fuerte, 39 mm/h a 105 km al SSO |
  | León, 3 oct. 10:35 | +SHRA (chubasco fuerte) | llueve, moderada (5,2 mm/h); 31 % de la zona mojada |
  | León, 30 sept. 08:00 | RA BR (lluvia y neblina) | llueve, débil (0,9 mm/h) |
  | Barajas, 6 oct. 16:45 | +TSRA (tormenta fuerte) | llueve, moderada (6,5 mm/h); granizo probable a 14 km al SSO |
  | Barajas, 6 oct. 15:30 | VCSH (chubascos cerca, no en el aeropuerto) | no llueve; lluvia débil a 2,1 km al ONO |

  Cuadra en los cinco. Las intensidades del radar parecen bajas para un "chubasco fuerte" o una "tormenta fuerte":
  Marshall-Palmer es para lluvia continua y se queda corto con las tormentas; anotado para el juez.
  **Frente a AEMET** (composición GeoTIFF de su web, 16 oct. 10:30 UTC): toda la Península, mismas zonas de lluvia
  (suroeste de Francia, núcleo al sur de León, células del sureste); alrededor de León, el mismo grupo de células a
  ~75 km al S con la misma forma. AEMET pinta además una banda de lluvia frente a Galicia que en OPERA no tiene datos
  (falta el radar de A Coruña). Imágenes para el autor en la carpeta temporal de la sesión. **El autor las da por
  buenas** (2026-10-16).
- Paso 4c-1 (2026-10-16): 14 tests nuevos (480 en total). Manchas de lluvia inventadas desplazadas: 6 km al E y 4 al N
  en 30 min dan 12 y 8 km/h (rumbo 56°); medias celdas y cuartos dentro de 0,25 celdas; 35 km al SO en 30 min
  (búsqueda gruesa); lluvia quieta, 0 km/h; mismo desplazamiento en 15 min, el doble de velocidad; con huecos sin datos;
  la misma lluvia tres veces más fuerte se sigue igual. "No sé": una sola tormenta pequeña (poca lluvia), imágenes sin
  relación y lluvia más rápida que el límite. Real (fixture RATE del 3 oct. 05:30 y 06:00): hacia el NNE a ~19 km/h,
  mucho más parecida movida que quieta. **Medido** en Madrid (3 y 6 oct.) y León (30 sept.) con parejas a 15-45 min:
  la dirección sale estable (tabla en `DECISIONS.md` 028).
- Paso 4c-2 (2026-10-16): 9 tests nuevos (489 en total): cuatro imágenes cada 10 min dan 6 parejas y su mediana; una
  imagen rara no arrastra la mediana; parejas que no coinciden, "movimientos distintos"; sin parejas suficientes, la
  razón más repetida; imágenes a menos de 10 min no se emparejan; el parecido es mayor en el desplazamiento verdadero;
  "apenas se mueve" por debajo de 3 km/h; las imágenes del movimiento (falta una: se deja fuera); la flecha llega a
  30 km al E con 30 km/h. **Comprobación** (`motioncheck.cs`, `motionwind.cs`; `docs/verificacion/2026-10-16-fase4-
  movimiento*.csv`): 55 momentos de cinco días de lluvia en Madrid y León, con el movimiento calculado 30 min antes:
  mover la imagen se parece más a la real que dejarla quieta en **los 55** (0,64 frente a 0,40 de media); frente al
  viento a 700 hPa, diferencia de dirección mediana de 16° (49 de 55 dentro de 45°) y misma velocidad (relación 1,07).
  Serie de imágenes con flecha para el autor en la carpeta temporal de la sesión: **le cuadra** (2026-10-16).
- Paso 4d-1 (2026-10-16): 16 tests nuevos (505 en total). Una tormenta de 30 km con su borde a 20 km al oeste, hacia
  el este a 30 km/h, llega en 35-45 min; lluvia encima alejándose a 40 km/h se va en 15-30 min; la que pasa por el
  norte no llega; el borde de una zona de lluvia da un 30-70 %; sin movimiento, la lluvia se queda; con la imagen de
  hace 10 min, los pasos cuentan desde ahora y dan lo mismo para la misma hora; lluvia que viene de donde no hay datos,
  desconocida; el círculo crece (un chubasco a 10 km entra a los 90 min); la imagen movida; los cortes del autor.
- Paso 4d-2 (2026-10-16): la consola no tiene tests; probada en vivo. **Comprobación** (`nowcastcheck.cs`;
  `docs/verificacion/2026-10-16-fase4-prevision-aeropuertos.csv`): 61 momentos en Barajas y León a +30, +60 y +90 min.
  Según el radar en el aeropuerto, cuando dijo "probable" llovió el 90 % / 83 % / 73 % de las veces y cuando dijo "no",
  el 12-14 %; dejando la lluvia quieta, 73 % / 59 % / 48 % y 28-30 %. Frente al METAR sale peor porque el radar no ve
  parte de la lluvia: en León marca seco con lluvia en el METAR en el 32 % de los casos (Barajas, 18 %). Tabla en
  `DECISIONS.md` 030.
- Paso 4e-1 (2026-10-16): 33 tests nuevos (538 en total). METAR: 18 grupos de tiempo presente (lluvia, llovizna, chubasco,
  tormenta con lluvia, granizo, nieve; no cuentan neblina, niebla, trueno sin lluvia, "en las cercanías", "reciente" ni
  nieve levantada); con la respuesta real de IEM del 6 oct. (Barajas y León, 12-15 UTC): 16 informes, la tormenta de
  Barajas desde las 14:06, León sin tiempo presente; rango, errores. AROME HD 15 min con respuestas reales del 6 oct.:
  Barajas y León en una llamada, 3,1 mm de 16:00 a 16:15 son 12,4 mm/h; el cuarto de hora que contiene cada hora;
  Sevilla fuera del modelo, con el motivo; errores. Medidas: cada palabra y cuánto llovió; Brier 0, 0,25 y 1;
  fiabilidad por bandas; sin casos.
- Paso 4e-2 (2026-10-17), **juez de la fase 4** (`tools/investigacion/judge4.cs`, ~70 min; resultados en
  `docs/verificacion/2026-10-17-fase4-juez.*`): 16 jul. a 15 oct. 2026, los 35 días con lluvia en el METAR (Madrid con
  Barajas, Getafe, Cuatro Vientos y Colmenar; León), cada 30 min de 2 h antes a 2 h después de la lluvia: 686 momentos,
  ~1.650 casos por plazo. Se "sale" 5 min después de cada imagen (lo que tarda en publicarse). Brier (0 perfecto; más
  bajo, mejor) y, entre paréntesis, la lluvia que anunció como "probable":

  | Contra | Plazo | Movida (2+5) | Movida (3+10) | Movida (4+15) | Movida (5+20) | Quieta | AROME HD |
  |---|---|---|---|---|---|---|---|
  | Radar | +30 | 0,111 (64 %) | 0,103 (63 %) | 0,100 (60 %) | 0,098 (59 %) | 0,157 (50 %) | 0,236 (18 %) |
  | Radar | +60 | 0,143 (52 %) | 0,129 (50 %) | 0,121 (49 %) | 0,118 (47 %) | 0,199 (37 %) | 0,230 (18 %) |
  | Radar | +90 | 0,157 (44 %) | 0,139 (45 %) | 0,129 (43 %) | 0,126 (40 %) | 0,207 (31 %) | 0,222 (19 %) |
  | METAR | +30 | 0,132 (60 %) | 0,121 (60 %) | 0,117 (56 %) | 0,115 (54 %) | 0,156 (53 %) | 0,180 (28 %) |
  | METAR | +60 | 0,141 (56 %) | 0,132 (50 %) | 0,127 (46 %) | 0,125 (43 %) | 0,180 (42 %) | 0,180 (28 %) |
  | METAR | +90 | 0,151 (48 %) | 0,139 (45 %) | 0,132 (41 %) | 0,128 (37 %) | 0,189 (35 %) | 0,180 (28 %) |

  Círculo: km al principio + km por hora. Llovió en el 18-20 % de los casos según el radar y en el 15-16 % según el METAR.
  **Conclusiones:** mover la lluvia gana a "nada se mueve" y a AROME en todos los plazos y contra los dos; AROME, como
  previsión de un punto cada 15 min, acierta poco el sitio exacto ("probable": llovió el 32 % de las veces). Con el
  círculo actual (2+5) los porcentajes se pasan de seguros (dice 96 %: llueve el 63 %); con círculos mayores mejoran
  (5+20: dice 92 %, llueve el 73 %) a cambio de anunciar algo menos de lluvia. En Madrid el radar ve lluvia que el
  METAR no da (llovió 18 % frente a 11 %: lluvia que no llega al suelo en verano); en León, al revés (25 % frente a
  29 %). **El autor elige el círculo de 4 km + 15 km/h** (2026-10-17, `DECISIONS.md` 032), por defecto desde entonces.

### Riesgos y pendientes (fase 4)

- **León, lejos del radar:** lo cubre Palencia a ~100-120 km. **Confirmado** (4d-2): en la celda del aeropuerto el radar
  marca seco cuando el METAR dice lluvia en el 32 % de los casos (Barajas, 18 %): lluvia débil que no ve.
- **Huecos en la red de AEMET:** en renovación; hoy faltan varios radares en OPERA (no Madrid ni Palencia). Visto el
  16 oct.: AEMET pinta lluvia frente a Galicia y OPERA no tiene datos allí (sin el radar de A Coruña).
- **Tormentas con poca intensidad:** Marshall-Palmer da 5-7 mm/h donde el METAR dice "fuerte"; para tormentas se usan
  otras relaciones. El juez dirá si importa.
- **Tamaño:** cada fichero de OPERA es Europa entera (~2 MB). Para el móvil (fase 6) convendrá leer solo la zona
  (el GeoTIFF optimizado lo permite por rangos); en la consola no es problema.
- **Lectura lenta de la reflectividad:** 1-4 s por imagen aunque solo se lea la zona (la de RATE, 0,02 s). Para el
  juez (cientos de imágenes) habrá que contar con ello o guardar los recortes.
- **La caché no se limpia sola:** ~2 MB por imagen; hará falta antes del juez.
- **RATE sale más seco que la reflectividad** (0,24 frente a 1,0 mm/h en Barajas el 3 oct.): no se sabe aún cuál
  acierta más.
- **Paquete de una sola persona** (PureHDF): si se abandona, el lector queda aislado en `OdimComposite`.
- **`radar` tarda ~20 s** con el movimiento (cuatro imágenes de reflectividad de 1-4 s cada una, más la descarga).
- **Lluvia que nace en el sitio:** mover la lluvia no ve las tormentas que se forman encima (Barajas, 6 oct. 14:06 UTC).
- **Falsos ecos:** en León el 30 sept. a las 11:00 UTC sale una raya recta de eco débil (interferencias en el radar,
  no lluvia). Cuenta como lluvia en el texto y en el movimiento; si estorba, habrá que filtrarlos.
- **Viento flojo, movimiento dudoso:** con lluvia lenta (León 3 oct., ~17 km/h) el viento en altura no confirma la
  dirección; el juez dirá si el movimiento sirve igual.

Fase 3 — Viento en la ruta: terminada el 2026-10-16 (pasos 3a a 3e hechos y el juez ejecutado; el autor la da por buena).

## Fase 3

### Qué funciona (fase 3)

- **Tramos de la ruta** (`RouteLegs.Split`, `RouteLeg`; `DECISIONS.md` 019): parte una ruta en tramos iguales de ~1 km
  (N = longitud redondeada) con su principio y final, rumbo de la línea recta (grados HACIA), rectitud (1 = recto,
  ~0 = vuelve sobre sí mismo), lo que sube o baja y hora de paso al principio, a la mitad y al final (UTC), desde la
  hora de salida. La ruta entera dura longitud / velocidad media (25 km/h por defecto) y ese tiempo se reparte por la
  pendiente: un 13 % más lento por cada 1 % de subida y más rápido en bajada, hasta ±4 % (`DECISIONS.md` 021). Sin
  altitudes, velocidad fija. Sin red.
- **Viento en cada tramo** (`RouteWind`, `LegWind`, `WindField`; `DECISIONS.md` 020): pide la previsión en un punto
  cada 5 km de la ruta, en una sola llamada (AROME → `best_match`; si AROME no cubre algún punto, todos pasan a
  `best_match`), y da a cada tramo el viento de su punto medio a la hora de paso, mezclando los dos puntos más cercanos
  y las dos horas que lo rodean. De ese viento saca cuánto viene de cara (negativo: a favor), cuánto de lado (positivo:
  por la derecha) y la racha de lado. `IWindForecastProvider` admite ya varios puntos por llamada.
- **Puntuación y orden** (`RideRanking`, `RideScore`, `RideOption`; `DECISIONS.md` 022): ventaja = viento de cara
  medio en la primera mitad − en la segunda; tramos con racha de lado > 40 km/h (límite del autor). Ordena primero
  por km con racha peligrosa y después por ventaja. `RankAsync` puntúa varias vueltas en los dos sentidos
  (`Route.Reversed`) con una sola llamada de previsión.
- **Rumbos según el viento** (`RideBearings`; `DECISIONS.md` 023): con viento claro, una vuelta justo contra él y las
  demás a ±45°, ±90°…; con calma o viento variable, repartidas como en la fase 2.
- **Explicación por tramos** (`WindStretches`): agrupa los tramos de 1 km en tramos de al menos 5 km según cómo se nota
  el viento (de cara, a favor, de lado por la derecha o la izquierda, flojo), con su media y la racha de lado máxima.
- **Planificador** (`RidePlanner`; `DECISIONS.md` 023): viento en la salida durante las horas de la ruta → rumbos →
  vueltas → orden, cada vuelta en su mejor sentido. Si contra el viento no sale ninguna válida, amplía el abanico a
  ±90° y 180° hasta tener una. `DepartureTime` da la próxima vez que llega una hora local; `Route.TurnsClockwise`.
- **Comando `route` con viento**: `--at hh:mm` (por defecto, ahora) y `--speed` también con `--km`. Dice el viento
  de la salida y los rumbos buscados, escribe un GPX por vuelta válida en su mejor sentido, numerado por puesto
  (`1-vuelta-60km-SO-20261016-1000.gpx`), y explica cada una: ventaja, tramos ("km 0-15: de cara 11 km/h · …"),
  rachas de lado peligrosas, resumen de la vuelta y hora de llegada. Las descartadas siguen saliendo como en la fase 2.
- **Piezas del juez** (`ObservedWind`, `RideJudge`; `DECISIONS.md` 024): el METAR de las estaciones pasado a un campo de
  viento por horas; repuntuar las vueltas de un día con el viento real: acierto, pérdida y ganancia frente a no mirar
  el viento.

### Qué falta (fase 3)

División aceptada por el autor el 2026-10-15:

- [x] 3a. Tramos y horas de paso: cortar la ruta en tramos de ~1 km con su rumbo (grados HACIA) y la hora a la que se
  pasa por cada uno (hora de salida + velocidad media). En Core, sin red, probado con rutas grabadas.
- [x] 3b. Viento en cada tramo: previsión de Open-Meteo (AROME → `best_match`, como en la fase 1) en pocos puntos de la
  ruta y en una sola llamada, a la hora de paso; componente de cara/a favor y de lado (viento DESDE frente a rumbo HACIA).
- [x] 3c-1. Hora de paso: calibrada con 8 salidas reales del autor. **Decisión del autor:** el viento no ajusta la
  velocidad (efecto pequeño e incierto); la pendiente sí reparte el tiempo (`DECISIONS.md` 021).
- [x] 3c-2. Puntuación: viento de cara en la primera mitad, a favor en la segunda, penalizar rachas fuertes de lado;
  cada vuelta en los dos sentidos. Sin pesos: ventaja de viento por mitades y límite de racha de lado de 40 km/h
  (**decisión del autor**). Ejemplos revisados por el autor (abajo).
- [x] 3d-1. Core: rumbos contra el viento ±45° (**decisión del autor**) y explicación por tramos.
- [x] 3d-2. Consola: `route` usa los rumbos según el viento, ordena por puntuación con la explicación por tramos
  ("km 0-18: de cara 12 km/h…"), admite la hora de salida (`--at`, por defecto ahora) y escribe un GPX por vuelta en su
  mejor sentido (**decisión del autor**). Si no hay vuelta contra el viento, abanico más ancho (**decisión del autor**).
- [x] 3e-1. Core del juez: viento observado (METAR) por horas y repuntuación de las vueltas (`DECISIONS.md` 024).
- [x] 3e-2. Pasar el juez: León, Fuenlabrada, Tres Cantos y La Bañeza (aparte), ~15 días con viento de los últimos 3
  meses, previsión "salgo ahora" y "el día antes" (**decisión del autor**); tabla abajo.

Para desarrollar, BRouter en local (`tools/brouter-local/`, `DECISIONS.md` 018): no cargar brouter.de.

### Cómo se ha validado (fase 3)

- Paso 3a (2026-10-15): 21 tests nuevos (292 en total). Rutas inventadas: una recta de 10 km da 10 tramos con su rumbo
  y una hora cada 2,4 min a 25 km/h (3 min a 20 km/h); 10,4 km dan 10 tramos de 1,04 km y 10,6 km dan 11; un cuadrado
  recorrido en sentido horario da los rumbos 90°, 180°, 270° y 0°; ir y volver 500 m da un tramo con rectitud ~0;
  cruza bien el meridiano 180°. Salidas grabadas: en la de Boadilla los tramos van seguidos, empiezan sobre el track
  y suman su longitud; en la de La Bañeza (ida y vuelta) la ida y la vuelta apuntan en sentidos opuestos (más de 170°).
  Rechaza salida que no esté en UTC, velocidades fuera de 5-60 km/h, tramos fuera de 0,1-10 km y rutas sin longitud.
  A mano con `tools/investigacion/legs.cs` sobre La Bañeza: 96 tramos, ida al suroeste (210-275°), vuelta al noreste,
  y la zona del giro (km 46-51) con tramos poco rectos (0,6-0,7) y rumbos variados, como corresponde.
- Paso 3b (2026-10-15): 31 tests nuevos (323 en total). Proveedor: varios puntos en una llamada con una respuesta real
  grabada (los 19 puntos de la salida de La Bañeza, AROME, 16 oct. 08-12 UTC); Madrid + Tarifa: AROME rechaza la
  llamada entera (respuesta real) y los dos pasan a `best_match` (respuesta real), en orden. Campo de viento: la muestra
  exacta en su sitio y hora; mezcla de horas (350° y 10° dan cerca del norte, no del sur); la racha de la hora en que
  cae el momento; dos sitios con vientos distintos no se anulan; el más cercano pesa más; solo los dos más cercanos;
  fuera de las horas, error; pide todas las horas de la salida en una llamada. Tramos: viento de cara, a favor y de
  lado por la derecha y por la izquierda en los rumbos principales y en diagonal; la rectitud reduce el de cara; un
  viento del suroeste en la ida y vuelta de La Bañeza da de cara a la ida (> 12 km/h de media con 20) y a favor a la
  vuelta; con la previsión real grabada, el viento del sur entra por la derecha a la vuelta.
  A mano con `tools/investigacion/routewind.cs` (previsión real para el 16 oct. a las 8:00 UTC): 96 tramos, 19 puntos,
  una llamada; viento flojo (2-9 km/h) que rola al sur y sube; en la vuelta (rumbo noreste) entra por la derecha
  (5-8 km/h). **Medida previa** (la que llevó a elegir 5 km): ver `DECISIONS.md` 020.
- Paso 3c-1 (2026-10-15): 5 tests nuevos (328 en total): 10 km llanos y 10 km al 4 % a 25 km/h siguen durando 48 min,
  con la subida al 48 % de la velocidad en llano; bajada al 2 %, un 26 % más rápida; el 8 % cuenta como el 4 %; si
  falta alguna altitud, velocidad fija; la salida de Boadilla con sus altitudes mantiene la media y varía la velocidad.
  Las salidas grabadas de los tests leen ahora su altitud. **Calibración** con 8 salidas del autor (Garmin, con hora y
  altitud, 575 tramos; los ficheros no entran en el repositorio) y **comprobación** de la hora de paso: error medio
  2,9 → 1,6 min y máximo 13,4 → 4,8 min al repartir por pendiente (`DECISIONS.md` 021, `calibrate.cs`, `timing.cs`).
- Paso 3c-2 (2026-10-15): 13 tests nuevos (340 en total): de cara a la ida y a favor a la vuelta da ventaja +40 con
  20 km/h, al revés −40; tramos con racha de lado sobre el límite (estrictamente mayor) unidos en tramos seguidos; la
  racha de cara no cuenta; límite configurable; aviso si falta la racha; una salida sin rachas peligrosas va antes
  que otra con más ventaja; `Route.Reversed`; con viento constante los dos sentidos de un rectángulo puntúan igual;
  con un viento que sube de 5 a 45 km/h gana el sentido que lo pilla más tarde; una sola llamada para dos rutas y sus
  dos sentidos, ordenadas. **Ejemplos** (`tools/investigacion/rank.cs`, BRouter local): La Bañeza, 60 km, 6 rumbos,
  salida el 30 sept. 2026 a las 8:00 UTC con el viento archivado de AROME (13 km/h del SSO, rachas de 28): 5 vueltas
  válidas (hacia el sur ninguna). Primera la de 240° (contra el viento, ventaja +12,1 y +11,5 en el otro sentido),
  luego 120° (+4,2), 300° (+0,9) y últimas 0° y 60° (−13 a −16). Rachas de lado de 31-38 km/h: ninguna sobre 40.
  **El autor miró los GPX** (2026-10-15) y el orden le parece correcto: "al ser circulares siempre hay tramo que da
  mal el aire", pero la elegida pone la mayor parte del viento de cara al principio.
- Paso 3d-1 (2026-10-15): 12 tests nuevos (352 en total): rumbos 194°, 239°, 149° con viento del 194°; 6 rumbos
  abriéndose a ±45°, ±90° y 135°; uno solo, contra el viento; con calma o viento variable, 0°, 120°, 240°; abertura
  configurable. Tramos: de cara, de lado por la derecha, a favor, flojo y de lado por la izquierda en su sitio y con
  su media; un tramo corto se pega al vecino más largo y dos iguales se unen al quitar el que los separaba; una salida
  más corta que el mínimo es un solo tramo; la racha de lado máxima. Con el ejemplo de La Bañeza (`rank.cs`), la
  ganadora queda en 4 tramos: km 0-15 de cara 11 km/h, 15-30 de lado por la izquierda 10, 30-42 a favor 11 y 42-60 de
  lado por la derecha 15.
- Paso 3d-2 (2026-10-15): 18 tests nuevos (370 en total): el planificador busca 194°, 239° y 149° con viento del
  194° y pone primera la de 194°, con dos llamadas de previsión (salida y vueltas); con calma, 0°, 120° y 240°; con
  `--bearing`, repartidas desde él y sin ampliar; sin vueltas válidas no pide más viento; cada vuelta del orden apunta
  a su rumbo; abanico ampliado hasta la primera válida (284°) o hasta agotar 284°, 104° y 14°; hora de salida: hoy,
  mañana, el mismo minuto, horario de invierno tras el 25 oct. y la hora que no existe el 28 mar. 2027; sentido de
  giro; mejor sentido de cada vuelta. La consola no tiene tests: probada en vivo con el BRouter local. La Bañeza, 60 km,
  mañana a las 10:00: viento flojo (3,5 km/h), rumbos cada 120°, 3 vueltas y el aviso de que el orden apenas importa.
  Fuenlabrada, 50 km, a las 8:00: viento del E a 7 km/h; las tres del sector E fallan (distancia), se amplía al S
  (falla) y al N (válida, 52,4 km); 52 peticiones.
- Paso 3e-1 (2026-10-16): 11 tests nuevos (381 en total). Con el METAR real de LELN (1 oct.): una muestra por hora
  con el informe de la hora en punto, pasando nudos a km/h; el VRB02KT de las 8:00 toma la dirección del informe de
  las 8:30 (20°); sin informes, nada. Informe más cercano a ±15 min y el anterior en empate; calma sin dirección →
  norte; el campo deja fuera la estación sin datos. Juez: con el viento previsto, acierta y no pierde nada; con el
  viento dado la vuelta, falla y pierde más de 20 km/h; frente a la vuelta elegida sin viento gana más de 20 km/h;
  repuntuar con el mismo viento da lo mismo y con el contrario, lo contrario.
- Paso 3e-2 (2026-10-16), **juez de la fase 3** (`tools/investigacion/judge3.cs`, BRouter local; resultados en
  `docs/verificacion/2026-10-16-fase3-juez.*`): 20 jul. a 14 oct. 2026, los 15 días con más viento observado (de 10:00
  a 12:24) en cada sitio, 60 km a 25 km/h. 129 casos (9 perdidos: Open-Meteo cortó por el límite por minuto o devolvió
  algo que no era JSON). La "ventaja" es viento de cara medio en la 1.ª mitad − en la 2.ª, con el viento observado.

  | Sitio (estación) | Previsión | Casos | Acierto | Pérdida | Ventaja real elegida | Sin mirar el viento | Ganancia | Ventaja > 0 |
  |---|---|---|---|---|---|---|---|---|
  | León (LELN 7 km) | salgo ahora | 15 | 87 % | 0,9 | +9,8 | −0,9 | +10,7 | 93 % |
  | León | el día antes | 15 | 73 % | 0,6 | +7,8 | −0,9 | +8,7 | 93 % |
  | Fuenlabrada (LEGT, LEVS) | salgo ahora | 13 | 85 % | 0,9 | +9,8 | −1,8 | +11,6 | 92 % |
  | Fuenlabrada | el día antes | 15 | 73 % | 0,9 | +6,6 | −2,5 | +9,1 | 93 % |
  | Tres Cantos (LECV, LEMD) | salgo ahora | 12 | 100 % * | 0,0 | +3,8 | +1,4 | +2,4 | 92 % |
  | Tres Cantos | el día antes | 12 | 100 % * | 0,0 | +3,3 | +0,7 | +2,6 | 75 % |
  | **Los tres** | **salgo ahora** | **40** | **90 %** | **0,6** | **+8,0** | **−0,5** | **+8,5** | **93 %** |
  | **Los tres** | **el día antes** | **42** | **81 %** | **0,6** | **+6,1** | **−1,0** | **+7,1** | **88 %** |
  | La Bañeza (LELN 38 km) | salgo ahora | 12 | 42 % | 5,4 | +8,4 | +2,4 | +6,0 | 83 % |
  | La Bañeza | el día antes | 13 | 54 % | 4,2 | +8,0 | +1,3 | +6,7 | 85 % |

  Km/h; viento medio observado 13-20 km/h. \* En Tres Cantos siempre hubo una sola vuelta válida (9 veces ampliando el
  abanico): su acierto no dice nada. **Conclusión:** elegir por el viento da unos 8 km/h de ventaja real frente a casi
  cero sin mirarlo; decidirlo la víspera empeora poco. El autor la da por buena (2026-10-16).

### Riesgos y pendientes (fase 3)

- **Pocas vueltas entre las que elegir:** en el juez, Tres Cantos tuvo siempre una sola válida y Fuenlabrada 1 o 2 en
  26 de 28 casos; solo León suele tener las 3. Es el generador de la fase 2 (una de cada cuatro vueltas inválida en su
  juez), no el viento. Con más candidatas la elección por el viento ganaría más.
- **Ventaja prevista pequeña = elección frágil:** los dos fallos más caros (León 8 sept., Fuenlabrada 24 ago., ~11 km/h
  perdidos) fueron días con +2 km/h de ventaja prevista. Idea: decir cuándo la diferencia entre vueltas es pequeña.
- **Rachas de lado sin aviso:** 7 casos, en tramos de 1 a 3 km. El METAR solo da rachas significativas.
- **Límite por minuto de Open-Meteo** (600): el juez lo tocó al pedir muchos puntos seguidos; para un usuario no es
  riesgo, pero un reintento tras un minuto evitaría perder la consulta.
- **El viento "real" es el de un aeropuerto:** igual en toda la ruta. En La Bañeza (estación a 38 km) el acierto baja
  a la mitad.
- La consola `route` no tiene tests propios (solo Core); probada a mano y en vivo.

Fase 2 — Rutas circulares + GPX: terminada el 2026-10-15 (pasos 1 a 6 hechos y el juez ejecutado; el 7 queda como mejora).

Fase 1 — Viento en consola: terminada el 2026-10-14 (los 6 pasos hechos y el juez ejecutado).
Lo hecho en la fase 1 está más abajo, a partir de "Qué funciona".

## Fase 2

### Qué funciona (fase 2)

- **Escritor de GPX** (`VientoHud.Core.Gpx`): `GpxTrack` (nombre, puntos, hora UTC, descripción y copyright opcionales),
  `GpxTrackPoint` (coordenada y altitud opcional) y `GpxWriter`. Escribe GPX 1.1 con un track y un segmento (lo que los
  GPS de bici importan como curso), UTF-8 sin BOM, punto decimal siempre, coordenadas a 7 decimales y altitud al
  decímetro. `GpxCopyright.OpenStreetMap` pone la atribución que pide la licencia ODbL.
- **Motor de rutas** (`VientoHud.Core.Routing`): interfaz `IRouteEngine` (ruta por una lista de puntos de paso; para una
  vuelta, el último es la salida), `Route` (puntos con altitud, distancia, desnivel, atribución) y `RouteSegment`
  (tramos consecutivos con las etiquetas OSM, p. ej. `highway`, `surface`). Los fallos son `RouteException` con
  `RouteFailure.Unreachable` (algún punto no se puede unir: moverlo puede ayudar) o `RouteFailure.Service`
  (red, servidor o respuesta rara).
- **Cliente de BRouter** (`BRouterRouteEngine`): sube el perfil estricto `road-strict.brf` (recurso incrustado en Core)
  una vez y pide rutas en GeoJSON con el id que devuelve el servidor. Si el servidor responde 500 sin texto (perfil
  perdido) lo sube de nuevo y repite una vez. Como mucho una petición por segundo (configurable). También admite un
  perfil del servidor (`ServerProfile`, p. ej. `fastbike`) sin subir nada. El perfil permite `trunk` con coste 3 (solo
  cuando el rodeo por otras carreteras es largo; `DECISIONS.md` 016) y prohíbe autovías, tierra y obras. Tiene activado
  `correctMisplacedViaPoints` sin tope: BRouter recorta la ida y vuelta a un punto de paso intermedio (sin probar en
  vivo todavía).
- **Análisis de una ruta, sin red** (`RouteAnalyzer`, `RoadRules`; `DECISIONS.md` 014): km por tipo de vía y firme con
  su categoría (carretera, camino asfaltado, `trunk`, prohibido, desconocido), distancia de cada punto de paso pedido a la línea
  del track y fracción del recorrido repetido. Devuelve cada tramo repetido (dónde está y dónde su otra pasada) y si es
  un **ramal**: entrar y salir por la misma carretera sin salida en mitad de la ruta, lo que hace una estrella. Las
  cuerdas de un globo, los cuellos de un lazo y las idas y vueltas desde casa no son ramales (`DECISIONS.md` 015).
  También las **medias vueltas** de más de 200 m (entrar y salir por el mismo sitio, aunque sea corto) y si son el destino.
- **Puntos de paso de una vuelta** (`LoopWaypoints`): abanico desde la salida (la salida, n − 1 puntos a R km
  repartidos en 180° alrededor del rumbo, y la salida otra vez; n = 5 por defecto), con sentido horario en el mapa;
  radio inicial = objetivo / (polígono recto 3,85 R × factor de carretera 1,5) ≈ objetivo / 5,8. `Coordinate` sabe
  ya el rumbo hacia otro punto (`BearingTo`) y el punto a X km en un rumbo (`Destination`), en el círculo máximo.
- **Generador de una vuelta** (`LoopGenerator`, cualquier `IRouteEngine`): desde la salida, hacia un rumbo y de una
  distancia objetivo; mueve los puntos que provocan ramales, corrige el radio y gira el rumbo si hace falta, con un tope
  de peticiones. Cada intento lleva su análisis y sus problemas (ramales, medias vueltas, distancia, prohibido, rumbo).
  Varias candidatas (`GenerateManyAsync`) avisando de cada una al terminar; si el motor deja de responder, devuelve lo
  que tenía (`EngineFailure`) y no prueba más rumbos.
- **Comando `route`** de la consola: genera las vueltas, escribe un GPX por rumbo y un resumen (ver paso 5).
- **BRouter local para desarrollar** (`tools/brouter-local/`, `DECISIONS.md` 018): `start.ps1` descarga BRouter 1.7.10
  y los datos de Madrid y León y arranca el servidor en el puerto 17777; con `VIENTOHUD_BROUTER_URL` la consola lo usa
  en lugar de brouter.de. Da las mismas rutas que brouter.de.
- `GpxTrack.FromRoute` (track con altitudes y licencia de OpenStreetMap) y `RideDistance` (km a partir de horas).

### Qué falta (fase 2)

- [x] 1. Elegir el motor de rutas: **BRouter** con perfil estricto propio y puntos de paso calculados en Core
  (`DECISIONS.md` 013).
- [x] 2. Escritor de GPX 1.1 en Core (track con puntos), validado en los tests contra el esquema oficial.
- [x] 3. Cliente de BRouter detrás de una interfaz: perfil estricto en el repositorio (prohíbe también carreteras en
  obras; `trunk` prohibido en todo el mundo, `DECISIONS.md` 013, luego permitido con coste 3, `DECISIONS.md` 016),
  subida del perfil, ruta entre puntos de paso,
  lectura de las etiquetas de cada tramo, errores ("target island", 400, 500) y fixtures grabadas.
- [x] 4. Generador de vueltas. Dividido en:
  - [x] 4a. Análisis de una ruta, sin red: desvío de cada punto pedido, % repetido y km por tramos prohibidos
    (`DECISIONS.md` 014). Probado con rutas reales grabadas. Ampliado con los tramos repetidos y los ramales
    (`DECISIONS.md` 015), calibrado con seis salidas reales del autor.
  - [x] Prueba en vivo antes de 4b (2026-10-15, `tools/investigacion/livetest.cs`, registros en
    `docs/verificacion/fase2-prueba-vivo-*.txt`): 8 vueltas de 40-80 km (León capital N, E, S, O; Tres Cantos N, E;
    Madrid centro SO; Fuenlabrada S), hasta 12 peticiones cada una: abanico, mover los puntos de ramal, ajustar R y
    girar el rumbo ±20-40°. Éxito: ≤ 10 % en ramales, ±10 % de la distancia, 0 km prohibidos, centro de la ruta a
    menos de 45° del rumbo; meta 6 de 8.
    - Perfil estricto: **3 de 8** (León O, León S, Fuenlabrada S). 18 "target island" en 92 peticiones; la distancia
      salta mucho con R (quitar un ramal: 75 → 39 km); los ramales vuelven al rehacer el abanico.
    - `trunk` permitido (coste 1,5, como una carretera normal), contado como válido: **6 de 8**, con 0-17,5 km por
      `trunk` en las válidas (León S 14-17,5 km). 6 "target island" en 96 peticiones. Tres Cantos N y E fallan igual.
    - Sin bloqueos de brouter.de: peticiones cada 3 s, 67 + 92 con 8 minutos entre tandas.
    - Fallo del script: en León E (estricto) repitió las mismas peticiones en bucle (salían de la caché, sin red).
    - Salidas del autor emparejadas con BRouter (`tools/investigacion/matchrides.cs`): 21 km por `trunk` en una vuelta
      por Soto de la Vega (antigua N-VI), 2,8 km en otra y 12 km en una de Humanes. El lazo de Boadilla no se pudo
      emparejar ("target island" en un punto).
    - Decisión del autor: `trunk` permitido con penalización fuerte (`DECISIONS.md` 016), coste elegido con la prueba
      de abajo.
    - Prueba con costes 2, 3 y 5 (2026-10-15, registros `fase2-prueba-vivo-trunk{2,3,5}-*.txt`; script corregido para no
      repetir peticiones): **6 de 8** válidas con los tres. Km por `trunk` sumados en las seis válidas: 24,1 (coste 2),
      13,9 (coste 3) y 18,2 (coste 5); 2-4 "target island" por tanda; 45-49 peticiones por tanda, sin bloqueos. Entre 3 y
      5 la diferencia es ruido: depende de qué puntos de paso acaba usando el generador (León E: 0,8 km con coste 3 y
      6,3 km con coste 5, con rutas distintas). Tres Cantos N y E fallan siempre por distancia (geografía).
    - **Coste 3**, decidido por el autor y aplicado (2026-10-15): `road-strict.brf` y `RoadRules` (categoría `Trunk`,
      `RouteAnalysis.TrunkKm`). 2 tests nuevos y 4 adaptados (210 en total). Es el mismo perfil que usó la tanda en vivo
      de coste 3, así que brouter.de lo acepta.
  - [x] 4b. Colocar los puntos de paso a partir de la salida, el rumbo (hacia la parte más lejana de la vuelta) y la
    distancia objetivo: abanico de n = 5 (la salida y 4 puntos a R km en `rumbo - (90 - 180·i/n)`), R inicial ≈
    objetivo / 5,8. Sentido de giro fijo (horario en el mapa); invertirlo es cosa de la fase 3. (`LoopWaypoints`)
  - [x] 4c. Generador. Límites decididos por el autor (`DECISIONS.md` 017): como mucho 12 peticiones por candidata,
    3 candidatas por defecto, sin límite aparte para el desvío de un punto. Dividido en:
    - [x] 4c1. Una vuelta hacia un rumbo (`LoopGenerator`): abanico, mover los puntos de ramal, corregir R (entre un
      radio corto y uno largo cuando se conocen los dos) y girar el rumbo ±20°, ±40° ante "target island" o sin vuelta
      válida. Válida: ≤ 10 % en ramales, ±10 % de distancia, 0 km prohibidos o desconocidos, centro de la ruta a menos
      de 45° del rumbo. Devuelve la válida o la menos mala, las peticiones hechas y todos los intentos. Probado con un
      motor falso.
    - [x] 4c2. Varias candidatas (rumbos separados), orden de preferencia (válidas, distancia más cercana, menos
      `trunk`) y el caso "hacia allí no hay vuelta". Pasada en vivo en Madrid y León con el cliente real.
      - [x] Código y tests: `LoopGenerator.GenerateManyAsync` (una vuelta por rumbo, ordenadas) y
        `LoopGenerator.SpreadBearings` (3 rumbos cada 120° por defecto, decidido por el autor; se puede fijar el primero).
        Cada resultado dice si encontró vuelta (`Found`).
      - [x] Pasada en vivo: a medias por un bloqueo de brouter.de (ver riesgos), completada después con el comando `route` y con el juez (paso 6). Hechos 5 de los 8 casos de la
        prueba anterior: León N, E, S y O válidos (en 4, 3, 2 y 1 peticiones; igual o menos que el script), Tres Cantos N
        sin vuelta (12 peticiones). Faltan Tres Cantos E, Madrid SO, Fuenlabrada S y las tres candidatas cada 120° en
        León, Fuenlabrada y Tres Cantos (`tools/investigacion/generate.cs`, registro en
        `docs/verificacion/fase2-generador-core-vivo-2026-10-15.txt`).
      - [x] Medias vueltas (2026-10-15): el autor vio en gpx.studio que las vueltas de León "válidas" entraban y salían
        de ramales cortos (Ferral del Bernesga 610 m; en León N tres, una de 2,4 km por la N-630). Corregido en el
        análisis (medias vueltas de más de 200 m, `DECISIONS.md` 015), en el generador (problema `TurnBacks` y mover el
        punto de paso al inicio del ramal) y en el perfil (`correctMisplacedViaPoints`). Esas vueltas de León ya no
        valdrían: la pasada en vivo pendiente dirá cuántas salen limpias.
- [x] 5. Comando de consola `route --km 60` o `--hours 2` (con velocidad media configurable),
  `[--lat --lon] [--count 3]`: escribe los GPX y un resumen.
  - [x] Código (2026-10-15): `vientohud route (--km <10-300> | --hours <0,5-12> [--speed <10-45>]) [--lat --lon]
    [--count <1-6>] [--bearing <0-360>] [--out <carpeta>]`. Sin `--lat/--lon`, la ubicación de Windows. Velocidad
    media por defecto 25 km/h (`RideDistance`). Rumbos cada 360/count grados desde `--bearing` (0 por defecto). Enseña
    cada rumbo según termina y luego la lista ordenada; un GPX por rumbo (`vuelta-60km-SO-aaaammdd-hhmm.gpx`, con
    `-descartada` si no es válida) con un resumen en la descripción (km, desnivel, km por `trunk`, caminos asfaltados,
    % repetido). Si BRouter deja de responder, enseña y guarda lo que ya tenía y no prueba más rumbos. Código de salida
    0 con al menos una vuelta válida, 1 si ninguna o error, 2 argumentos incorrectos, 130 cancelado.
  - [x] Prueba en vivo con brouter.de (junto con la pasada pendiente del 4c2). El bloqueo se levantó el mismo día
    (una petición de prueba respondió 200).
    - [x] León capital, 60 km, 3 rumbos (`vientohud route --km 60 --lat 42.5987 --lon -5.5671`). Primera ejecución:
      3 válidas en 9 peticiones; las medias vueltas en seco bajan a 50-180 m (BRouter las recorta), pero la del norte
      subía 2,4 km por la N-630 y bajaba con una vuelta pequeña al final (ramal del 7 %, por debajo del 10 %). Con la
      regla de ramales de más de 200 m (`DECISIONS.md` 015): N 56,4 km y ESE 63,9 km válidas, **0 % repetido y sin
      medias vueltas**; OSO sin vuelta válida (la mejor 51,1 km, −15 %) en 12 peticiones; 23 peticiones en total.
      Antes OSO salió válida: rechazar un ramal cambia el orden de los intentos y el ajuste del radio acaba en otro
      sitio. **Mejora pendiente:** control de la distancia más robusto.
      El autor revisó en gpx.studio las dos válidas (N y ESE) y las da por buenas.
    - [x] Fuenlabrada y Tres Cantos, 60 km, 3 rumbos (26 y 25 peticiones, sin bloqueos). Fuenlabrada: N 58,6 km válida
      (0 % repetido); ESE (+35 %) y OSO (+20 %) sin vuelta válida. Tres Cantos: ESE 61,7 km válida (21 % repetido, sin
      ramales); N (−38 %, una media vuelta de 240 m) y OSO (+65 %) sin vuelta válida.
    - Resumen con las reglas actuales: **4 de 9 válidas**, todas limpias (medias vueltas restantes de 40-80 m). Las 5
      que fallan, fallan por distancia; dos de ellas tienen buena forma pero se pasan (+20 %, +35 %). **Siguiente
      mejora: el control de la distancia.**
    - [x] Control de la distancia mejorado (2026-10-15): al girar de rumbo, el radio inicial sale de la mediana de km de
      ruta por km de radio medida hasta entonces; giros ±10°, ±20°, ±40°; hasta 4 radios por rumbo. Repetido en vivo
      con La Bañeza en lugar de León capital (a petición del autor, conoce mejor sus carreteras), registro en
      `docs/verificacion/fase2-route-vivo-2-2026-10-15.txt`, 62 peticiones sin bloqueos:
      - La Bañeza: **3 de 3** en 12 peticiones (N 62,7 km, ESE 54,3 km, OSO 60,4 km), las tres sin repetir nada, sin
        medias vueltas y con 0-0,1 km por `trunk`.
      - Fuenlabrada: 2 de 3 (antes 1): N 58,6 km; ESE 54,4 km (globo, 50 % repetido sin ramales, 11,6 km por `trunk`);
        OSO se queda en −11 %.
      - Tres Cantos: 1 de 3, como antes (ESE 61,7 km); N (−15 %, se aleja del rumbo) y OSO (+65 %) sin vuelta: la
        geografía (El Pardo, autovías).
      - Total **6 de 9** (antes 4 de 9, con León capital en lugar de La Bañeza). **Pendiente:** que el autor revise
        las tres de La Bañeza en gpx.studio.
- [x] 6. Juez: km por tipo de vía y firme a lo largo del track, distancia dentro de ±10 %, GPX válido; pasada en Madrid
  y León con varias distancias; abrir en gpx.studio y cargar en el GPS de la bici.
  - [x] Cargar en el GPS de la bici (2026-10-15): se ve bien; todas sus carreteras son de las que el autor usa en bici.
    El autor cargó en su Hammerhead Karoo la vuelta de La Bañeza hacia el OSO (60,4 km) generada
    por `vientohud route`.
  - [x] Pasada del juez (2026-10-15, `tools/investigacion/judge.cs` contra BRouter local; tabla completa en
    `docs/verificacion/fase2-juez-2026-10-15.md`; dos pasadas, tabla idéntica): 5 salidas × 40, 60 y 100 km × 3 rumbos.
    **33 de 45 válidas** (La Bañeza 8/9, León capital 9/9, Madrid centro 6/9, Fuenlabrada 6/9, Tres Cantos 4/9), en
    291 peticiones. En las válidas (2.274 km): **0 km prohibidos o desconocidos**, 74 km por `trunk` (3 %), 0,9 km de
    caminos asfaltados; todos los GPX válidos contra el esquema GPX 1.1. De los 12 fallos, 11 son por distancia (y
    alguno además por rumbo o media vuelta), casi todos en Tres Cantos y en Madrid y Fuenlabrada a 40 y 100 km.
    - Sin dato de firme en OpenStreetMap: 591 km (26 %); las reglas los dan por asfaltados. 291 km son carreteras
      (`tertiary` o más), 151 calles y 44 carriles bici; **105 km (4,6 %) son `unclassified` o `service`**, donde
      la duda es mayor. Riesgo residual: el ojo del autor en gpx.studio (y en la bici) es la comprobación final.
- [ ] 7. (Pendiente como mejora, no bloquea la fase: el juez muestra poca repetición y un 7 % de calles.) Probablemente: mejores puntos de paso (pueblos, cruces) si el juez muestra mucha repetición o mucho tramo
  urbano (la salida de Madrid).
  - Observado por el autor en la vuelta de La Bañeza hacia el OSO (2026-10-15): **callejeo por los pueblos**, la ruta
    deja la carretera principal, cruza unas calles y vuelve a ella (Posadilla de la Vega; un pueblo junto a la CL-621).
    Unos cientos de metros; no es para descartar la ruta. Causas posibles: un punto de paso que cae dentro del pueblo
    (entra por una calle y sale por otra, así que ni BRouter ni el detector de medias vueltas lo ven) o el recargo por
    tráfico del perfil (`consider_traffic` 0,3) que abarata una calle frente a la carretera.

### Cómo se ha validado (fase 2)

- Paso 1 (2026-10-14): llamadas reales a BRouter (brouter.de), Valhalla y OSRM (FOSSGIS); openrouteservice y
  GraphHopper responden 401 sin clave. Vueltas desde Madrid centro, Tres Cantos y León capital contando km por tipo
  de vía y firme con las etiquetas que devuelve BRouter (y `trace_attributes` en Valhalla). Resultados en
  `DECISIONS.md` 013. Los scripts de prueba no se han guardado en el repositorio: el cliente real llega en el paso 3.
- Paso 2 (2026-10-14): 13 tests nuevos (130 en total). Cada GPX se valida contra el esquema oficial `gpx.xsd` de
  TopoGrafix, guardado en `Fixtures/Gpx/`; un test comprueba que la validación detecta una coma decimal. Otro fuerza la
  configuración española (`es-ES`) para comprobar que los decimales salen con punto. Se escribieron con `GpxWriter` dos
  vueltas reales de BRouter del paso 1 (León 103 km, Madrid 54 km): ~2.000 puntos y ~175 KB cada una.
  Se abren bien en gpx.studio (comprobado por el autor). Cargado en el Hammerhead Karoo del autor el 2026-10-15 (la vuelta hacia el OSO
  de La Bañeza generado por `vientohud route`; dashboard de Hammerhead → Routes → New Route → Import, y sincronizar).
- Paso 3 (2026-10-14): 20 tests nuevos (150 en total) con respuestas reales grabadas en `Fixtures/BRouter/` (vuelta de
  17,4 km en León, "target island", subida de perfil bien y con error). Comprueban la URL y el orden lon,lat de los
  puntos (también con cultura `es-ES`), la subida única del perfil, la lectura de puntos, distancia, desnivel y
  etiquetas, que los tramos cubren la ruta de punta a punta (la suma coincide al metro con la distancia de BRouter),
  la nueva subida cuando el servidor pierde el perfil, cada tipo de error y la espera entre peticiones.
  Prueba en vivo contra brouter.de, con el cliente subiendo el perfil él mismo: Madrid centro hacia el este 72,9 km,
  Tres Cantos hacia el oeste 113,6 km, León hacia el este 80,2 km, **0 km prohibidos** en las tres (sin `trunk`,
  pistas, obras ni firmes sin asfaltar). León hacia el oeste con radio 11 km da `Unreachable` ("target island").
  Las distancias siguen muy lejos de lo pedido: eso es el paso 4.
- Paso 4a (2026-10-15): 43 tests nuevos (193 en total). Casos calculados a mano (ida y vuelta, vuelta cuadrada,
  "piruleta" que sale y vuelve por la misma carretera, punto a 2 km de una recta de 10 km sin puntos intermedios,
  meridiano de 180°) y tres rutas reales nuevas grabadas en `Fixtures/BRouter/` (ver su README):

  | Ruta grabada | Km | Repetido | Prohibido | Desvío máx. de un punto |
  |---|---|---|---|---|
  | León norte, R = 3 km, estricto | 17,4 | 7 % | 0 | 0,14 km |
  | León este, R = 6 km, estricto | 79,7 | 84 % | 0 | 1,07 km |
  | León sur, R = 3 km, `fastbike` | 21,3 | 5 % | 2,53 km (pistas, una senda y 85 m de `trunk`) | 0,25 km |
  | Tres Cantos suroeste, R = 5 km, estricto | 48,1 | 74 % | 0 | 2,69 km (punto en el Monte de El Pardo) |

  Las rutas del perfil estricto dan 0 km prohibidos también con la lista ampliada de firmes. La repetición cambia menos
  de 1 punto al revisar cada 50 m o al recorrer la ruta al revés. Los GPX de León este y Tres Cantos se escribieron en
  el scratchpad para mirarlos en gpx.studio (pendiente de que el autor los vea).
- Ampliación del 4a, cuerda (2026-10-15): 4 tests nuevos y otros ampliados (197 en total). Casos a mano: ida y vuelta
  (todo cuerda), piruleta (1 km de cuerda + vuelta), vuelta cuadrada (nada), cuadrado con un ramal de 2 km en una
  esquina (todo fuera de la cuerda), track que no acaba donde empieza. Con datos reales: el globo del autor
  (`Fixtures/Rides/`, solo posiciones y altitud) repite el 50 %, con 38,9 km de cuerda y 0 % fuera de ella; las
  estrellas grabadas de León este y Tres Cantos, más del 70 % fuera de la cuerda. **Fallo corregido:** en un track
  abierto (que no acaba justo en la salida, como una salida grabada) la repetición daba siempre 0. No afectaba a las
  cifras anteriores, porque las rutas de BRouter acaban exactamente en la salida.
  Después, con una ida y vuelta pura del autor (La Bañeza, 96 km, también en `Fixtures/Rides/`): salía 48 % fuera de la
  cuerda porque el primer km de salida va por otras calles y la cuerda solo podía empezar a 1 km de casa. Ahora puede
  empezar hasta a 3 km de cada extremo: 94,9 km de cuerda y 0 % fuera de ella. Caso a mano equivalente (calles
  paralelas los 2 primeros km). 200 tests en total.
- Ramales en vez de cuerda (2026-10-15): un lazo del autor (Boadilla del Monte, 84 km: vuelta cerca de casa, cuello de
  4,6 km y vuelta de 50 km) salía con 11 % fuera de la cuerda, rechazado. El criterio pasa a ser "ramal o no" (cada
  tramo repetido se juzga por lo que hay entre sus dos pasadas; `DECISIONS.md` 015). Al pasarlo por las estrellas
  generadas salieron dos fallos más, corregidos con tests: un ramal dentro de otro ramal pasaba por vuelta (ahora solo
  cuentan los km hechos una vez) y una cuerda seguida justo de un ramal se mezclaba con él (ahora el tramo se parte
  donde salta su otra pasada). Resultado: salidas del autor (globo, ida y vuelta, lazo, dos vueltas) 0-0,6 % en
  ramales; vueltas generadas buenas 0-1,6 %; estrellas generadas 35-84 %. Casos a mano nuevos: lazo, ramal con una
  vuelta de 2 km al final, cuerda seguida de ramal. 205 tests en total.
- Globo en el centro (2026-10-15): otra salida del autor (Humanes, 21,6 km) con una ida y vuelta al punto más lejano
  colgando de una vuelta pequeña salía con 28 % en ramales. Ahora el ramal que da la vuelta en el punto más lejano de
  casa es el destino. Con un margen del 90 % dejaba pasar estrellas (León este con `trunk`: 35 % → 0 %), así que solo
  vale el punto más lejano exacto. Resultado: seis salidas del autor 0 % en ramales; estrellas generadas 24-60 %.
  Casos a mano nuevos: globo en el centro y estrella de dos brazos (solo se perdona el que llega más lejos).
  208 tests en total.
- Paso 4b (2026-10-15): 24 tests nuevos (234 en total). `Coordinate.BearingTo` con los cuatro puntos cardinales y
  Madrid → León (~327°, NNO); `Coordinate.Destination` a 100 km al norte (100 km de latitud), cruzando el meridiano de
  180° y comprobando distancia y rumbo inicial de vuelta. El abanico: salida al principio y al final, puntos a R km, los
  rumbos esperados (también con rumbo 0°, −90° y 270°), cualquier n desde 3, meridiano de 180°, polígono recto de
  3,85 R medido sobre un abanico pequeño, y que reproduce al décimo de metro los puntos con los que se grabaron las
  fixtures de León este y Tres Cantos.
- Paso 4c1 (2026-10-15): 12 tests nuevos (246 en total) con un motor falso (`FakeRouteEngine`): vuelta válida a la
  primera (1 petición); ruta un tercio larga → radio × 0,75 y válida a la segunda; longitud que salta con el radio →
  el tercer radio sale de interpolar entre el corto y el largo; punto de paso alcanzado por un ramal → se mueve al
  inicio del ramal y la vuelta sale válida; "target island" hacia 0° → gira a 20°; sin vuelta posible en ningún rumbo
  → sin resultado tras 5 peticiones (0°, 20°, 340°, 40°, 320°); tope de peticiones; errores del servicio se propagan.
  Con datos reales: si el motor devuelve la estrella grabada de León este, los puntos de sus ramales se mueven a
  puntos de esa ruta más cerca de la salida. Aún no probado en vivo (4c2).
- Medias vueltas (2026-10-15): 6 tests nuevos y uno ampliado (259 en total). Casos a mano: ramal de 600 m en un lado
  de un cuadrado (se detecta a ~600 m; la repetición casi no lo ve), uno de 150 m (se ignora), ida y vuelta (su media
  vuelta es el destino). Salidas del autor: solo medias vueltas de destino (La Bañeza 16,9 km, Boadilla 690 m en su
  punto más lejano), ninguna en el globo. Generador: punto de paso 500 m dentro de una carretera sin salida → se mueve
  a la entrada y la vuelta sale válida. Sobre las vueltas generadas en vivo (`tools/investigacion/stubs.cs` y Core
  dan lo mismo): León N 3 (610 m, 2,4 km, 340 m), León E 1 (510 m), León O 1 (610 m), León S 0. El perfil lleva las
  dos líneas de `correctMisplacedViaPoints` (test).
- Paso 5, código (2026-10-15): 12 tests nuevos (271 en total): `RideDistance` (2 h a 25 km/h = 50 km; límites),
  `GpxTrack.FromRoute` con una ruta grabada (puntos y altitudes iguales, licencia OSM, válido contra el esquema), el
  generador guarda lo que tenía si el motor responde 403 a mitad, deja de probar rumbos tras un fallo y avisa de cada
  rumbo al terminar y en orden. La consola no tiene tests: probados a mano los errores de argumentos (sin `--km` ni
  `--hours`, los dos, `--km 5`, `--speed` sin `--hours`, `--count 9`, `--lat` sin `--lon`, `--bearing 400`, opción
  desconocida, valor que falta: mensaje y código 2) y la ayuda. **Falta** la ejecución completa contra brouter.de.

### Riesgos y pendientes (fase 2)

- **BRouter lleva cada punto de paso a la vía permitida más cercana, aunque esté lejos:** un punto en el mar a 50 km
  de Galicia dio HTTP 200 y una ruta de 446 km hasta la costa. `RouteAnalyzer` ya mide a qué distancia pasa la ruta
  de cada punto pedido (4a); el generador (4c) tiene que descartar o mover los que se alejen demasiado. La opción de BRouter para devolver los puntos (`exportWaypoints`) no sirve:
  devuelve los pedidos, no los corregidos, y con JSON mal formado.
- **HTTP 500 sin texto es ambiguo:** BRouter lo devuelve por perfil inexistente, por un solo punto y por coordenadas
  inválidas. El cliente valida los puntos antes de llamar y lo trata como perfil perdido (vuelve a subirlo una vez).
- **brouter.de sin política de uso publicada** (`DECISIONS.md` 013): si cae, no hay rutas. **Limita sin avisar:**
  el 2026-10-15, tras unas 75 peticiones en unos 10 minutos (a una por segundo, varias con URL largas), respondió
  HTTP 403 "Please, retry later!". El generador tiene que ir justo de peticiones. **Segundo bloqueo** el mismo día, tras
  unas 330 peticiones en total y solo ~23 de la última tanda (una cada 2 s): parece un cupo acumulado por IP (por hora o
  por día), no un límite de velocidad. Una generación normal (3 candidatas, como mucho 36 peticiones) no debería
  tocarlo, pero las pruebas intensivas sí. Refuerza la idea de la fase 6 de usar BRouter en el móvil (`DECISIONS.md`
  013). Un fallo del servicio a mitad de `GenerateManyAsync` hacía perder las candidatas ya halladas: resuelto en el
  paso 5 (las devuelve y deja de pedir). Para desarrollar y probar, BRouter en local (`DECISIONS.md` 018).
- **Las vueltas en abanico salen como estrellas, no como anillos** (investigación del 2026-10-15, scripts en
  `tools/investigacion/`): con puntos geométricos, BRouter entra y sale de cada punto por el mismo ramal. Con R = 6-8 km:
  León oeste 2 % repetido (bien), León norte 48 %, León este 84 %, Tres Cantos este 77 %, Tres Cantos suroeste 74 %;
  León este y sur con R = 8 y Tres Cantos norte, "target island". Dos salidas reales del autor (Garmin) repiten 0 %.
  - Penalizar lo ya recorrido (zonas `nogos` con peso de BRouter, tramo a tramo): ayuda poco (84 → 60 %, 77 → 61 %)
    y cuesta 5 peticiones por vuelta. Descartado.
  - Mover el punto que provoca un ramal al cruce donde empieza: León norte pasa de 48 % a 0 %, pero la vuelta encoge
    (58 → 27 km). No sirve donde toda la red es un árbol.
  - La prohibición de `trunk` pesa: en España muchas nacionales de un carril por sentido están etiquetadas `trunk`.
    León este (R = 6) con `trunk` permitido: 33 km, 35 % repetido, 3,6 km por `trunk` (frente a 80 km y 84 %).
    Un solo caso antes del bloqueo de brouter.de.
  - Algunos rumbos no tienen vuelta posible (Tres Cantos hacia El Pardo): hay que poder girar el rumbo.

## Fase 1

## Qué funciona

- Solución .NET 10 (`VientoHud.slnx`) con tres proyectos:
  - `src/VientoHud.Core`: tipos `Coordinate`, `WindSample`, `WindForecast`, `LocationFix`; interfaces `IWindForecastProvider`
    e `ILocationProvider`; cliente `OpenMeteoWindForecastProvider`; `Compass` (grados a 16 rumbos); regla `StartDirection`;
    `IWindObservationSource` con cliente `IemWindObservationSource` (METAR); `IPastWindForecastSource` con cliente
    `OpenMeteoPastWindForecastSource`; cálculo del error `WindVerification` y orquestación `ForecastJudge`.
  - `src/VientoHud.Console` (ejecutable `vientohud`): comandos `wind` y `verify`.
  - `tests/VientoHud.Core.Tests` (xUnit, 117 tests).
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
- **Comando `verify`:** `vientohud verify [--lat --lon] [--days 30] [--until aaaa-mm-dd] [--stations 5] [--radius 100]
  [--models a,b,c]`. Busca las estaciones METAR más cercanas, descarga observaciones y previsiones pasadas
  (corto plazo y 24 h) y muestra el error por modelo, en total y con viento, y por estación. Unos segundos.

## Qué falta (fase 1)

- [x] Crear la solución .NET 10: `VientoHud.Core`, `VientoHud.Console`, `VientoHud.Core.Tests`.
- [x] Elegir el proveedor de previsión: **Open-Meteo**, modelo AROME France (ver `DECISIONS.md` 005).
- [x] Interfaz del proveedor de previsión + implementación real + implementación falsa + fixtures grabados.
- [x] Comando de consola `wind [--lat --lon] [--hours 6]` con tabla por horas (velocidad, rachas, dirección).
- [x] Regla "empieza hacia X" (contra el viento medio de las próximas horas).
- [x] Juez: previsión vs observación, al menos 5 días. Tabla de error aquí. Dividido en:
  - [x] 6a. Observaciones: METAR de IEM, estaciones más cercanas a cualquier punto del mundo (`DECISIONS.md` 009).
  - [x] 6b. Previsiones pasadas de Open-Meteo: a 0-3 h (Historical Forecast API) y a 24 h (Previous Runs API),
    para AROME, `best_match` y ECMWF (`DECISIONS.md` 010).
  - [x] 6c. Cálculo del error en Core: emparejar previsión y observación por estación y hora; error medio de
    velocidad (absoluto y con signo), error angular de dirección, % de horas con la dirección dentro de ±45°. Por separado con viento
    observado flojo (< 12 km/h) y con viento (`DECISIONS.md` 011).
  - [x] 6d. Comando `verify`; ejecutado 90 días en 8 zonas y 365 días en Madrid y León. AROME sigue por delante
    de `best_match` (`DECISIONS.md` 012).
  - [ ] Más adelante: AEMET como complemento para España cuando llegue la clave.

## Cómo se ha validado

- `dotnet build`: 0 advertencias, 0 errores.
- `dotnet test`: 117/117 superados.
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
- Paso 6d (2026-10-14): `ForecastJudge` probado con fuentes falsas (orden de veredictos, estaciones sin cobertura,
  sin estaciones, periodo vacío). `verify` probado en vivo: errores de argumentos, 30 días en Madrid (5,7 s),
  90 días en 8 zonas, 365 días en Madrid y León. Los resultados están en la sección "Juez del viento".
  El formateo de `verify` no tiene tests automáticos (la consola no tiene proyecto de tests).

## Riesgos y pendientes detectados

- **Juez con AEMET (complemento):** su observación horaria solo cubre las últimas 12 horas; habrá que ir
  guardándola durante días. Útil sobre todo donde no hay aeródromos (montaña de León).
- **Clave de AEMET:** pedida el 2026-10-14, llegó el 2026-10-16 y caduca a los 3 meses (hacia mediados de enero
  de 2027; norma de AEMET desde julio de 2026). Solo sirve para el complemento de AEMET como herramienta del autor;
  no entra en el producto (`DECISIONS.md` 025). El radar de la fase 4 sale de OPERA, sin clave.
- **Elegir el modelo por zona:** el juez muestra que el mejor modelo cambia según la zona (León, Navarra y
  A Coruña frente al centro y el este). La app podría ejecutar el juez en las estaciones cercanas al usuario
  y elegir el modelo allí. No es de la fase 1; anotado como mejora posible.
- **Pocas estaciones METAR fuera de las ciudades:** en León solo LELN en 100 km. El juez dirá qué modelo acierta
  más en zonas llanas y abiertas; en valles y montaña la comparación será menos representativa.

## Juez del viento (fase 1)

Ejecutado el 2026-10-14 con `verify`, 90 días (16 jul – 13 oct 2026), estaciones METAR más cercanas
(salida completa en `docs/verificacion/`). Métricas definidas en `DECISIONS.md` 011.

**Con viento observado ≥ 12 km/h, previsión a corto plazo.** Error medio de velocidad en km/h (sesgo entre
paréntesis; negativo = el modelo se queda corto) y % de horas con la dirección dentro de ±45°:

| Zona (estaciones) | Horas | AROME | best_match | ECMWF IFS |
|---|---|---|---|---|
| Madrid (LEVS, LEGT, LEMD, LETO, LECV) | 2.599 | **3,9** (−0,9) 95 % | 6,7 (−5,8) 95 % | 5,7 (−5,2) 94 % |
| León (LELN) | 430 | 4,0 (−0,5) 89 % | **3,4** (−0,6) 93 % | 5,0 (−3,9) 86 % |
| Navarra (LEPP, LESO, LELO) | 1.811 | 3,7 (−1,2) 94 % | **3,3** (−1,1) 98 % | 5,3 (−4,1) 88 % |
| Zaragoza (LEZG, LEHC) | 1.894 | **3,6** (0,0) 96 % | 5,2 (−4,7) 96 % | 5,6 (−5,1) 93 % |
| Burgos (LEBG) | 1.216 | **3,6** (−2,1) 97 % | 3,7 (−2,1) 95 % | 3,8 (−1,6) 96 % |
| Sevilla (LEZL, LEMO, LEEC) | 1.537 | no cubre | **4,2** (−3,4) 96 % | 4,5 (−4,0) 95 % |
| Valencia (LEVC, LEBT) | 1.234 | **3,3** (−2,0) 96 % | 4,8 (−4,1) 96 % | 5,3 (−4,8) 93 % |
| A Coruña (LECO, LEST) | 1.108 | 4,2 (−3,4) 96 % | 3,6 (−2,8) 91 % | **3,0** (−1,5) 91 % |

**Con viento, previsión del día anterior (24 h)**, error medio de velocidad: Madrid 4,5 / 6,6 / 5,8;
León 5,4 / 5,3 / 5,4; Navarra 4,5 / 4,8 / 5,5; Zaragoza 4,7 / 5,5 / 5,8; Burgos 4,3 / 4,1 / 3,9;
Sevilla — / 4,5 / 4,8; Valencia 3,8 / 4,9 / 5,5; A Coruña 4,2 / 3,7 / 3,2 (AROME / best_match / ECMWF).
Dirección dentro de ±45°: 81-95 %.

**Todas las horas** (la mayoría con viento flojo): error medio de velocidad 2,5-4,7 km/h y de dirección 22-43°
según zona, modelo y plazo. Con viento flojo la dirección vale poco en cualquier modelo.

**Un año** (14 oct 2025 – 13 oct 2026), con viento, corto plazo: Madrid AROME 4,3 km/h (−1,0) frente a
`best_match` 7,7 (−7,3), 10.600 horas; León AROME 3,9 frente a `best_match` 3,1, 1.467 horas.
Confirma que el resultado no es cosa del verano.

**AROME HD** (1,5 km), 90 días, con viento, corto plazo: Madrid 3,8, León 4,3, Navarra 3,7, A Coruña 3,6 km/h;
igual que AROME salvo en A Coruña.

**Conclusiones** (`DECISIONS.md` 012):
- Con viento de verdad, la dirección prevista es fiable (≈ 95 % dentro de ±45° a corto plazo): la regla
  "empieza hacia X" se apoya en algo sólido cuando sopla.
- Los modelos globales se quedan cortos de velocidad 4-7 km/h en el centro y el este. AROME casi no tiene sesgo.
- Se mantiene AROME → `best_match`.

**Cautelas:** son aeródromos (llano y despejado); el archivo de AROME a 24 h solo cubre ~90 días, por eso tiene
menos horas a ese plazo; León tiene una sola estación.
