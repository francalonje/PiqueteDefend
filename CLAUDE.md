# PiqueteDefend

Juego de cartas por turnos, 2 jugadores en local (hotseat). Manifestantes vs Policías,
humor político argentino. Inspirado en Castle Wars / Arcomage. Unity 6 (6000.5.0f1) + URP.

## Fuente de verdad

**`docs/game-spec.md`** — la especificación completa del juego y **única fuente de verdad**.
Reglas, cartas, UI, parámetros y modelo de datos. Ante cualquier duda de comportamiento
(timing de status, orden de fases, fórmula de daño, desempates, empates), manda el spec.

> El balance de cartas todavía **no está validado**: las unidades usan una baseline uniforme
> de prueba (20 HP / 5 daño). La validación por simulación se hará con una herramienta a
> construir para el modelo actual (combate posicional por slots, victoria por KO).

## Arquitectura

El proyecto Unity vive en `PiqueteDefend/`. Separación estricta en tres ensamblados (asmdef):

```
Assets/PiqueteDefend/
  Core/         PiqueteDefend.Core         — C# de dominio. Enums, modelo (CardEffect,
                                              StatusEffect, UnitSlot, PlayerState, CardData),
                                              y el motor de juego (GameEngine). SIN MonoBehaviours,
                                              SIN GameObjects, SIN dependencias de escena.
                                              Puede usar UnityEngine sólo para ScriptableObject/Sprite.
  Presentation/ PiqueteDefend.Presentation — MonoBehaviours, UI, controladores de escena.
                                              Depende de Core. Toda la capa visual va acá.
  Tests/EditMode/ PiqueteDefend.Tests.EditMode — Tests unitarios del núcleo (NUnit).
```

**Regla de oro:** la lógica del juego nunca vive en un MonoBehaviour. El núcleo es C# puro y
determinista, testeable sin abrir el editor. La presentación sólo lee estado del núcleo y le
manda comandos (jugar carta, descartar). Esto permite testear en CI.

**Aleatoriedad inyectable:** el núcleo no usa `UnityEngine.Random` ni `System.Random` directo.
Recibe una abstracción de RNG, para que los tests sean deterministas y reproducibles.

## Convenciones

- Namespaces: `PiqueteDefend.Core`, `PiqueteDefend.Presentation`, `PiqueteDefend.Tests`.
- Las cartas son **data** (`CardData` ScriptableObject). Agregar una carta = crear un asset,
  no tocar código. Extender el juego = agregar un valor a `CardEffectType`/`StatusType` + su
  resolución en el motor (un solo lugar).
- Borrar assets de Unity con el editor cerrado: eliminar el archivo **y** su `.meta`.
- Idioma del dominio (cartas, facciones) en español; código en inglés salvo nombres del dominio.

## Flujo de trabajo

- Foundation-first: núcleo + tests antes que UI.
- Después de crear/borrar archivos `.cs` o `.asmdef` con el editor cerrado, abrir Unity una vez
  para que regenere los `.meta` y compile, antes de commitear.
- Commits: pedir al usuario antes de commitear/pushear.
