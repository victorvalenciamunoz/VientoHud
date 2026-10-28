<p align="center">
  <img src="images/icono.svg" width="112" alt="Icono de VientoHud: tres ráfagas de viento en cian" />
</p>

<h1 align="center">VientoHud</h1>

<p align="center">
  <b>Antes de salir en bici: ¿salgo, cuándo y por dónde?</b><br/>
  Mira el viento y la lluvia de ese momento, te dice hacia dónde conviene ir, trazas la ruta tocando el mapa y te la lleva al GPS de la bici en GPX.
</p>

---

Aquí están las versiones para instalar en Android. El código no es público.

## Qué es

Solo bici de carretera, y nada de teclear: la ubicación sale del móvil y lo demás se elige tocando.

1. **Hacia dónde.** Con el viento previsto para las horas de la salida, la app dice hacia dónde conviene empezar (ida de cara, vuelta a favor) y lo pinta como un abanico en el mapa.
2. **Por dónde.** Mantienes pulsado un punto del mapa (ida y vuelta) o dos o más (vuelta por ellos). En segundos, por carreteras asfaltadas: el sentido mejor para el viento, el viento tramo a tramo a la hora a la que pasarás (flechas de colores), la lluvia prevista en la ruta (radar a corto plazo y modelo después), los km de ciudad y la hora de llegada.
3. **Cuándo.** "Sal ahora" o "mejor espera hasta las…" si así te mojas menos o evitas rachas de lado peligrosas. También puedes elegir la hora de salida de hoy.
4. **El GPX es el producto.** La ruta, con el resumen y avisos en los puntos que importan, va al GPS de la bici (Hammerhead, Garmin o cualquiera). El móvil se queda guardado.

Hermano de [CieloHud](https://github.com/victorvalenciamunoz/CieloHud): misma estética, mismos principios.

## Instalar en tu móvil

Descarga el APK de la [última versión](https://github.com/victorvalenciamunoz/VientoHud/releases) desde el propio móvil
(Android 8.0 o posterior) y ábrelo. Android pedirá permiso para instalar apps de origen desconocido, y Play Protect
puede avisar de que no la conoce: es normal en una app que no está en Google Play.

Al abrirla pedirá la **ubicación** (es el punto de salida; no sale del móvil salvo para pedir el tiempo y las rutas).
Necesita conexión: la previsión, el radar, las rutas y el mapa vienen de servicios públicos. Las versiones 0.x son
*pre-release*: conviene contrastar lo que dice con lo que luego pasa en la carretera.

## Llevar la ruta al GPS de la bici

Con la ruta trazada y juzgada, el botón **GPX** abre el panel "GPX al aparato". El fichero lleva el consejo entero en
la descripción y los avisos de rachas y lluvia como puntos de paso.

- **Hammerhead Karoo:** **COMPARTIR** y, en la hoja de Android, **Karoo Companion**. Tras "Carga completa", la ruta
  está en tu lista de rutas de Hammerhead y el Karoo la descarga por wifi o por la app. Vale igual para cualquier otra
  app que acepte un GPX compartido (Strava, komoot, Drive, el correo…).
- **Garmin (Fenix, Edge…):** **ABRIR CON…** (Garmin Connect no sale en la hoja de compartir). Garmin Connect pide el
  tipo de trayecto, **Ciclismo en ruta**, y con **Guardar** queda en tus trayectos, para mandarla al reloj o al
  ciclocomputador.

## Datos y servicios

Sin servidor propio: todo son servicios y datos públicos, con sus licencias en
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

| Pieza | Fuente |
|---|---|
| Viento y lluvia por horas | [Open-Meteo](https://open-meteo.com) |
| Lluvia a corto plazo | Radar de [EUMETNET OPERA](https://www.eumetnet.eu/activities/observations-programme/current-activities/opera/) (Open Radar Data), movido hacia delante |
| Rutas | [BRouter](https://github.com/abrensch/brouter) con un perfil propio de bici de carretera sobre datos de OpenStreetMap |
| Mapa de fondo | OpenCycleMap de [Thunderforest](https://www.thunderforest.com) |
