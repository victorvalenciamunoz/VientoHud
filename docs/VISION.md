# Visión

## La idea

Antes de salir en bici abres la app y te dice:

> **Sal ahora. Ruta de 42 km, empieza hacia el oeste.**
> Viento del oeste a 18 km/h: de cara a la ida, a favor a la vuelta.
> Lluvia entrando por el suroeste hacia las 19:40. Con esta ruta llegas a casa sobre las 19:05.
> [Descargar GPX]

O bien:

> **Mejor espera a las 17:30.** Ahora hay un chubasco a 15 km que te pillaría en 20 minutos;
> detrás viene un hueco de unas 2 horas.

Cargas el GPX en el GPS de la bici, guardas el móvil y te vas. Ni pantalla ni auriculares durante la ruta.

## Por qué

- Es cálculo sobre el mundo real, no una app de meter y sacar datos.
- No pide nada al usuario salvo elegir cuánto quiere rodar.
- No exige atención durante la actividad: todo lo útil va en el GPX y en el resumen previo.
- Tiene un juez objetivo: el viento y la lluvia que hubo de verdad.

## Las tres piezas

1. **Viento:** previsión por horas y, sobre todo, por tramo de la ruta (a qué hora pasarás por cada sitio).
2. **Lluvia a corto plazo (nowcasting):** predicción propia a 30-90 min moviendo las imágenes del radar hacia delante.
3. **Ruta circular:** generada desde tu ubicación, puntuada según viento y lluvia, exportada a GPX.

## Qué NO es

- No es para MTB ni gravel: solo bici de carretera.
- No es un Strava ni un registro de actividad: no guarda tus salidas.
- No es un navegador giro a giro: de eso se encarga el GPS de la bici con el GPX.
