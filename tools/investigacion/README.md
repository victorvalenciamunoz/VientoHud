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
| `stem.cs` | Dónde se repite un track: la cuerda (salir y volver por la misma carretera) frente a lo repetido fuera de ella (estrellas), con una tira de texto a lo largo de la ruta. |
| `shape.cs` | Forma de un track (GPX o GeoJSON de BRouter): km, % repetido, punto más lejano, superficie encerrada. |
| `loops.cs` | Fase 2: compara formas de evitar la repetición (abanico actual, penalizar lo recorrido, mover puntos de ramales). Con `ALLOW_TRUNK=1`, el perfil estricto con `trunk` permitido. Escribe un GPX por resultado. |
| `togpx.cs` | Convierte una respuesta grabada de BRouter en GPX para mirarla en gpx.studio. |
