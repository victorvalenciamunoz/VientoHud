Respuestas reales de brouter.de (BRouter 1.7.10), grabadas el 2026-10-14 con el perfil `road-strict.brf`.

- `route-leon-loop.geojson`: HTTP 200. Vuelta de 17,4 km en León (radio 3 km, hacia el norte).
  `lonlats=-5.567100,42.598700|-5.596752,42.614558|-5.578426,42.624359|-5.555774,42.624359|-5.537448,42.614558|-5.567100,42.598700`
- `target-island.txt`: HTTP 400, texto plano. Vuelta de radio 11 km hacia el oeste desde León: un punto cae junto a
  un trozo de vía aislado.
- `profile-upload-ok.json`: HTTP 200 a `POST /brouter/profile` con `road-strict.brf`.
- `profile-upload-error.json`: HTTP 200 a `POST /brouter/profile` con un perfil inválido (trae `error`).

Otras respuestas vistas (sin fichero): perfil inexistente, un solo punto o coordenadas inválidas → HTTP 500 sin cuerpo.

Grabadas el 2026-10-15 para el análisis de rutas (fase 2, paso 4a). Puntos en abanico: la salida, 4 puntos a R km en los
rumbos `rumbo - (90 - 180·i/5)` (i = 1..4) y vuelta a la salida.

- `route-leon-east-repeats.geojson`: perfil estricto. León hacia el este, R = 6 km: 79,7 km (13 veces R) y mucho
  recorrido repetido.
  `lonlats=-5.5671,42.5987|-5.523983,42.642346|-5.497366,42.615353|-5.497403,42.582005|-5.524044,42.555038|-5.5671,42.5987`
- `route-leon-south-fastbike.geojson`: perfil de serie `fastbike` (no el estricto). León hacia el sur, R = 3 km:
  21,3 km con pistas (`track`) y sendas (`path`), algunas de tierra.
  `lonlats=-5.5671,42.5987|-5.537456,42.582838|-5.555779,42.57304|-5.578421,42.57304|-5.596744,42.582838|-5.5671,42.5987`
- `route-trescantos-southwest-offset.geojson`: perfil estricto. Tres Cantos hacia el suroeste, R = 5 km: 48,1 km. El
  segundo punto cae dentro del Monte de El Pardo (sin carreteras abiertas) y la ruta pasa a unos 2,7 km de él.
  `lonlats=-3.7083,40.6006|-3.714486,40.55588|-3.747908,40.567177|-3.766221,40.591237|-3.762418,40.618877|-3.7083,40.6006`
