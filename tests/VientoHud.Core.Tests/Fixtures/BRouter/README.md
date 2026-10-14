Respuestas reales de brouter.de (BRouter 1.7.10), grabadas el 2026-10-14 con el perfil `road-strict.brf`.

- `route-leon-loop.geojson`: HTTP 200. Vuelta de 17,4 km en León (radio 3 km, hacia el norte).
  `lonlats=-5.567100,42.598700|-5.596752,42.614558|-5.578426,42.624359|-5.555774,42.624359|-5.537448,42.614558|-5.567100,42.598700`
- `target-island.txt`: HTTP 400, texto plano. Vuelta de radio 11 km hacia el oeste desde León: un punto cae junto a
  un trozo de vía aislado.
- `profile-upload-ok.json`: HTTP 200 a `POST /brouter/profile` con `road-strict.brf`.
- `profile-upload-error.json`: HTTP 200 a `POST /brouter/profile` con un perfil inválido (trae `error`).

Otras respuestas vistas (sin fichero): perfil inexistente, un solo punto o coordenadas inválidas → HTTP 500 sin cuerpo.
