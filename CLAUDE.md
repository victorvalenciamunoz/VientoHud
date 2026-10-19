# VientoHud

Antes de salir en bici, responde: ¿salgo, cuándo y por dónde?
Genera una ruta circular pensada para el viento y la lluvia de ese momento y la exporta en GPX
para cargarla en el GPS de la bici. Hermano de CieloHud y GincanaHud (mismo autor).

Lee `docs/VISION.md` para el porqué y `docs/PLAN.md` para las fases. El estado actual está en `docs/STATUS.md`.

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
- **Fase a fase.** Trabaja SOLO en la fase actual de `docs/PLAN.md`. No adelantes trabajo de fases futuras.

## Stack

- .NET 10, C#.
- `VientoHud.Core`: librería sin dependencias de UI (se reutilizará en MAUI).
- `VientoHud.Console`: app de consola para validar cálculos y generar GPX.
- `VientoHud.Core.Tests`: xUnit.
- Fase 6: .NET MAUI (Android-first), estética HUD de la familia.

## Convenciones

- Código, identificadores y comentarios en inglés. Documentación en `docs/` en español.
- Todo el tiempo interno en UTC (`DateTimeOffset`). Solo se convierte a hora local al mostrar.
- Dirección del viento en convención meteorológica: grados DESDE donde sopla (0° = viene del norte).
  Rumbos de la ruta: grados HACIA donde vas. Documentarlo en cada tipo para no mezclarlos.
- Velocidades en km/h, distancias en km, coordenadas WGS84.
- Tipos de valor inmutables (`record` / `record struct`).
- Nada de lógica en la consola: solo formatea lo que devuelve Core.
- Claves de API (p. ej. AEMET) en user-secrets o variables de entorno. Nunca en el repo.
- Las llamadas a servicios externos van detrás de interfaces, con implementaciones falsas para tests
  y respuestas reales grabadas como fixtures.

## Forma de trabajar

- Antes de añadir un paquete NuGet o depender de un servicio externo, explica por qué y qué alternativas has valorado
  (licencia, límites de uso, si requiere clave, si sigue mantenido). Verifica en la fuente; no lo supongas.
- Tras cada cambio: `dotnet build` y `dotnet test`. No des un paso por terminado si algo falla.
- Al terminar un paso, actualiza `docs/STATUS.md` (qué funciona, qué falta, cómo se validó).
- Las decisiones relevantes van a `docs/DECISIONS.md`: contexto, decisión, alternativas.
- Pasos pequeños y verificables. Si una tarea es grande, propón cómo dividirla antes de empezar.
