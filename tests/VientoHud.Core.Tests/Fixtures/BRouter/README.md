Respuestas de BRouter (`brouter.de`) para los tests del cliente (`BRouterRouteEngine`).

- `profile-upload-ok.json` y `profile-upload-error.json`: respuestas reales a la subida de un perfil, aceptado y
  rechazado.
- `target-island.txt`: la respuesta real cuando un punto queda en una isla de la red de carreteras (HTTP 400).
- `route-synthetic-loop.geojson`: una ruta **inventada** con la forma de las respuestas reales (puntos con altura,
  `track-length`, `filtered ascend` y la tabla `messages` con las etiquetas de cada tramo): un cuadrado de 10 km al
  noreste del origen de los tests (`TestPlaces.Origin`).

Las rutas grabadas de verdad salen de sitios reales y viven fuera del repo (ver el proyecto de tests).
