# Scripts de investigación

Scripts sueltos de C# (`dotnet run fichero.cs`) usados para investigar. No forman parte de la solución ni tienen tests.
Usan `VientoHud.Core` con `#:project`. Ejecutarlos desde la raíz del repositorio, para que `global.json` fije el SDK 10:

```
dotnet run tools/investigacion/loops.cs -- <carpeta-salida> 3 leon-N:42.5987:-5.5671:0:8
```

Los que llaman a brouter.de esperan algo más de 1 s entre peticiones. Aun así, brouter.de respondió
HTTP 403 "Please, retry later!" tras unas 75 peticiones en unos 10 minutos (2026-10-15). Hay que hacer pocas pruebas y espaciarlas.

| Script | Qué hace |
|---|---|
| `record.cs` | Pide vueltas en abanico (salida + 4 puntos a R km) a brouter.de y guarda la respuesta tal cual (fixtures). |
| `analyze.cs` | Pasa `RouteAnalyzer` sobre respuestas grabadas de BRouter, con varias opciones de repetición. |
| `repeats.cs` | Tramos repetidos de un GPX según `RouteAnalyzer`: dónde, dónde está su otra pasada y si son ramales (estrellas). |
| `stem.cs` | Primera versión del criterio de forma (cuerda junto a casa frente a lo repetido fuera de ella), con una tira de texto a lo largo de la ruta. Sustituido por los ramales de `RouteAnalyzer` (`DECISIONS.md` 015). |
| `shape.cs` | Forma de un track (GPX o GeoJSON de BRouter): km, % repetido, punto más lejano, superficie encerrada. |
| `loops.cs` | Fase 2: compara formas de evitar la repetición (abanico actual, penalizar lo recorrido, mover puntos de ramales). Con `ALLOW_TRUNK=1`, el perfil estricto con `trunk` permitido. Escribe un GPX por resultado. |
| `livetest.cs` | Prueba en vivo del paso 4: vueltas con hasta 12 peticiones (abanico, mover puntos de ramal, ajustar R, girar el rumbo). Guarda cada respuesta en caché y se para ante un 403. Argumento `strict` o `trunk`. |
| `matchrides.cs` | Empareja salidas grabadas (GPX) con BRouter, con un punto de paso por km y `trunk` permitido, y cuenta los km por tipo de vía; dice dónde están los tramos `trunk`. |
| `generate.cs` | Pasada en vivo del generador de Core (`LoopGenerator`) con el cliente real de BRouter: un rumbo por caso, o `*` para tres candidatas cada 120°. Escribe el GPX de la mejor de cada rumbo. |
| `stubs.cs` | Medias vueltas cortas de un GPX (entrar y salir por el mismo sitio): dónde y cuánto entra. Lo usé para medir las de las vueltas de León antes de llevarlo a `RouteAnalyzer`. |
| `judge.cs` | Juez de la fase 2: genera vueltas en varias salidas y distancias contra un BRouter (pensado para el local) y saca una tabla con validez, error de distancia, km por tipo de vía y sin dato de firme, repetición, medias vueltas y validez del GPX contra el esquema. |
| `togpx.cs` | Convierte una respuesta grabada de BRouter en GPX para mirarla en gpx.studio. |
| `legs.cs` | Fase 3: tramos de ~1 km de un GPX (`RouteLegs`) con su rumbo, rectitud y hora de paso. |
| `routewind.cs` | Fase 3: viento previsto (Open-Meteo, un punto cada 5 km, una llamada) en cada tramo de un GPX: de cara y de lado. Puede guardar la respuesta (fixtures). |
