# BRouter local (desarrollo)

Servidor de BRouter en el propio ordenador, para probar el generador sin cargar el servidor público brouter.de
(`DECISIONS.md` 018). No es parte de la app: la consola usa brouter.de salvo que se le indique este servidor.

## Requisitos

- Java 17 (probado con OpenJDK 17.0.8 de Microsoft).
- Unos 175 MB en `tools/brouter-local/data/` (fuera de git): BRouter 1.7.10 (6,7 MB, licencia MIT) y dos ficheros de
  datos de 5° × 5° de brouter.de: `W5_N40.rd5` (Madrid, 92 MB) y `W10_N40.rd5` (León y La Bañeza, 77 MB). Son datos
  de OpenStreetMap (ODbL): la atribución que pone la consola sirve igual.

## Uso

```powershell
# Arranca el servidor en http://localhost:17777/ (la primera vez descarga BRouter y los datos).
powershell -ExecutionPolicy Bypass -File tools\brouter-local\start.ps1

# En otra terminal, que la consola lo use (sin esperar entre peticiones):
$env:VIENTOHUD_BROUTER_URL = "http://localhost:17777/"
dotnet run --project src\VientoHud.Console -- route --km 60 --lat 42.3000 --lon -5.8980
```

- `-Update` vuelve a descargar los datos (brouter.de los regenera cada semana).
- `-Tiles W5_N40,W10_N40,…` para otras zonas: cada fichero se llama por su esquina suroeste.
- El perfil estricto se sube igual que a brouter.de y queda en `data/customprofiles/`.

Comprobado el 2026-10-15: la misma vuelta de La Bañeza (60 km, 3 rumbos) da exactamente las mismas rutas que
brouter.de, con las mismas 12 peticiones, en 9 s en lugar de ~1 min.
