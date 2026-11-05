# VientoHud

Antes de salir en bici, responde: ¿salgo, cuándo y por dónde?
Genera una ruta circular pensada para el viento y la lluvia de ese momento y la exporta en GPX
para cargarla en el GPS de la bici. Hermano de CieloHud y GincanaHud (mismo autor).

Lee `docs/VISION.md` para el porqué y `docs/PLAN.md` para las fases. El estado actual está en `docs/STATUS.md`.

Las normas comunes a los proyectos del autor (idioma, pasos, build y test, STATUS y DECISIONS, ramas y PR) están en
sus instrucciones personales (`~/.claude/CLAUDE.md`), fuera del repo. Aquí va solo lo propio de VientoHud, que es una
app MAUI:

@~/.claude/tipos/maui.md

## Principios (no negociables)

- **Sin teclear datos.** La ubicación sale del dispositivo. El usuario elige tocando: opciones (duración, velocidad)
  y puntos en el mapa (hasta dónde ir, por dónde pasar); nunca escribe datos (`docs/DECISIONS.md` 048).
- **Sin backend propio ni base de datos.** Se pueden consumir APIs y datos públicos (previsión, radar, OpenStreetMap,
  motores de rutas), pero no montamos servidor. Cachés en fichero local si hace falta.
- **El tiempo es el juez.** Toda predicción debe poder contrastarse con lo que pasó de verdad
  (estaciones de AEMET, imágenes de radar posteriores). Si no se puede medir, no está terminado.
- **El GPX es el producto.** Durante la ruta el móvil va guardado: todo lo útil tiene que ir en la ruta y en el
  resumen previo.
- **Solo bici de carretera.** Rutas por vías asfaltadas, sin pistas ni caminos.

## Stack

- .NET 10, C#.
- `VientoHud.Core`: librería sin dependencias de UI.
- `VientoHud.Console`: app de consola para validar cálculos y generar GPX.
- `VientoHud.Core.Tests`: xUnit.
- `VientoHud.App`: app .NET MAUI (solo Android), con la estética de CieloHud (fase 7). `dotnet build src/VientoHud.App`;
  la clave de Thunderforest se lee de la variable de entorno `VIENTOHUD_THUNDERFOREST_KEY` al compilar (sin ella, el
  mapa es OpenStreetMap).

## Convenciones

- Dirección del viento en convención meteorológica: grados DESDE donde sopla (0° = viene del norte).
  Rumbos de la ruta: grados HACIA donde vas. Documentarlo en cada tipo para no mezclarlos.
- Velocidades en km/h, distancias en km, coordenadas WGS84.
- Tipos de valor inmutables (`record` / `record struct`).
- Nada de lógica en la consola: solo formatea lo que devuelve Core.

## Repo

El código está en `victorvalenciamunoz/VientoHud`, **público**, con `main` protegida: cada paso entra por rama y
pull request con el CI (`Tests de Core`) en verde. Las versiones se publican como releases del mismo repo. El
repo anterior, `VientoHud-codigo` (privado), queda archivado con el historial hasta el 2026-11-05.

Por ser público, nada que diga dónde vive o por dónde sale el autor (`docs/DECISIONS.md` 067): ni nombres de sitios
reales (en los textos, "pueblo A", "ciudad 1"…), ni coordenadas, ni capturas con el mapa de sus zonas. Las grabaciones
de sitios reales, las verificaciones y las herramientas de investigación viven en `../VientoHud-privado` (solo en
el disco del autor): los tests que las usan se compilan si esa carpeta existe, y `dotnet test` tiene que pasar con
ella y sin ella (`VIENTOHUD_PRIVATE_DIR` a una carpeta que no exista). `historia.md` (las fases cerradas sin pendientes)
también vive allí, en `../VientoHud-privado/docs/historia.md`.
