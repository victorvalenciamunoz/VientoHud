Respuestas reales de EUMETNET Open Radar Data (`https://s3.waw3-1.cloudferro.com/`, sin clave, CC BY 4.0),
grabadas el 2026-10-16 (fase 4, paso 4b-1).

- `OPERA@20261003T0600@0@DBZH.h5`: reflectividad máxima (CIRRUS), 1 km, toda Europa, del bucket `openradar-archive`.
  El 3 oct. 2026 a las 6:00 UTC Barajas (LEMD) daba chubasco (`SHRA`) en el METAR: llueve en Madrid.
- `OPERA@20261003T0600@0@RATE.h5`: lluvia en mm/h (NIMBUS), 2 km, misma hora.
- `OPERA@20261003T0530@0@RATE.h5`: lluvia en mm/h media hora antes, para el movimiento (paso 4c-1, grabado el
  2026-10-16).
- `list-24h-2026-10-16-after-0907.xml`: listado del bucket `openradar-24h` hecho a las 11:07 UTC,
  `prefix=2026/10/16/OPERA/COMP/&start-after=2026/10/16/OPERA/COMP/OPERA@20261016T0907`. Última DBZH: 11:00.
- `list-24h-2026-10-15-after-2220.xml`: lo mismo para el día anterior desde las 22:20 (última DBZH: 23:55).
- `missing-key.xml`: HTTP 404 (`NoSuchKey`) al pedir un fichero que no existe (`OPERA@20261016T0003@0@DBZH.h5`).
