# PiqueteDefend

> Juego de cartas por turnos para 2 jugadores en local. **Manifestantes vs Policías**, con humor político argentino. Inspirado en Castle Wars, The King is Watching, Legends of Runeterra y Slay the Spire.

![Unity](https://img.shields.io/badge/Unity-6000.5.0f1-black?logo=unity)
![Pipeline](https://img.shields.io/badge/render-URP-blue)
![UI](https://img.shields.io/badge/UI-UI%20Toolkit-blue)

---

## Concepto

Dos facciones enfrentadas se baten en una pantalla compartida (hotseat). Cada jugador
administra tres recursos, despliega unidades en un tablero de 6 slots, las **equipa** y
juega cartas de acción. Las unidades atacan según **patrones de posición**; el combate, las
pasivas y los efectos de las cartas van bajando el HP de las unidades rivales.

**Cómo se gana:** por **KO** — cuando el rival pierde su última unidad.

Un sistema de *muerte súbita* (backstop a partir del turno 50) garantiza que ninguna partida sea eterna.

## Facciones

- **Manifestantes** — bombos, banderas, pañuelos, ollas populares.
- **Policías** — escudos, patrulleros, gases lacrimógenos, comisarías.

**44 cartas (22 por facción)** en tres tipos:

- **Unidades** (8/facción) — persistentes, ocupan slots y combaten. Arquetipos con stats,
  zona de despliegue y patrón de ataque propios: *escaramuza, muro, cleave, productora,
  healer, sniper* y *emisor*.
- **Acciones** (10/facción) — boost, sabotaje, ataque, defensa y efectos especiales
  (aturdir, envenenar, desmoralizar, mover/intercambiar unidades, doblar o cortar producción…).
- **Equipo** (4/facción) — se adosa a una unidad y la mejora (+HP, +daño) u otorga una
  pasiva (regeneración, espinas, aura) hasta que la unidad muere.

Las **pasivas** van más allá de producir recursos: auras de daño, espinas, regeneración y
pasivas que dañan o envenenan al rival por zona. La **posición importa**: cada unidad tiene
zonas de despliegue (vanguardia/retaguardia) y patrones de ataque (banda, zona fija, relativo).

Cada facción tiene pool propio temático y **unidades iniciales** que se despliegan gratis
al empezar la partida.

> **Balance:** validado por simulación (ver `sim/`). Un simulador en Python re-implementa las
> reglas y barrió los valores hasta dejar win-rate parejo entre facciones (~48/52) y una duración
> media de ~32 medios-turnos resuelta por KO. Los números finales están en `docs/game-spec.md` §9/§10.

## Cómo jugar

1. **Jugar** en el menú → elegís qué facción juega primero (los lados son fijos por ahora: **Manifestantes** a la izquierda, **Policías** a la derecha).
2. En tu turno producís recursos (+1 de cada uno de base, más las unidades con pasiva de producción). Luego podés **jugar o descartar una carta** y **atacar con una unidad** (en cualquier orden). El primer jugador **produce pero no ataca** en su turno 1 (compensa la ventaja de iniciativa).
3. El turno pasa al rival. Repetir hasta que un jugador se quede sin unidades (KO).

Las cartas aplican **efectos** al rival o a uno mismo: sobre recursos, sobre la producción
(bloquearla/duplicarla), o **estados sobre una unidad** (veneno, aturdir, furia, desmoralizar).
También podés **mover** tus unidades o **intercambiar** las del rival para romperle la formación.

Al **arrastrar** una carta, los slots elegibles se iluminan; podés soltarla en **JUGAR** o
directamente **sobre un slot válido**. Soltar en un lugar inválido cancela sin gastar recursos.
Hacé **hover** sobre una unidad o una carta para ver un popover con su detalle.

**Controles:**
| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta | Arrastrar sobre **JUGAR** o sobre un slot válido | Seleccionar (1–6) + Enter |
| Descartar carta | Arrastrar sobre zona **DESCARTAR** | Seleccionar (1–6) + Backspace |
| Desplegar unidad | Arrastrar a un slot propio **libre** (no hay reemplazo) | — |
| Equipar a una unidad | Arrastrar el equipo sobre la unidad | — |
| Atacar / curar con unidad | Clic en la unidad (resaltada) → clic en el popover | — |
| Elegir slot objetivo | Clic en el slot | — |
| Mover / intercambiar unidad | Arrastrar al primer slot → clic en el segundo | — |
| Ver detalle de unidad / carta | Hover | — |

---

## Arquitectura

Separación estricta entre **lógica** y **presentación**, en tres ensamblados (`.asmdef`):

```
Assets/PiqueteDefend/
├── Core/          # Dominio en C# puro — sin Unity en la lógica. Motor de juego
│                  #   (GameEngine), modelo, enums, cartas (ScriptableObject), RNG inyectable.
│                  #   Determinista y testeable sin abrir el editor.
├── Presentation/  # UI Toolkit (UXML/USS), controladores de pantalla, audio, escenas.
├── Editor/        # Generadores: assets de cartas y escenas desde código (reproducibles).
└── Tests/EditMode # Tests unitarios del núcleo (NUnit).
```

La **fuente de verdad de las reglas** es `docs/game-spec.md`. El núcleo C# lo implementa; los
tests EditMode (`Tests/EditMode/GameEngineTests.cs`, 42 tests) cubren ataques posicionales, daño
efectivo (furia/aura/desmoralizar/equipo), espinas, curación, estados por unidad (veneno/aturdir),
equipo, mover/intercambiar, producción, reglas de inicio, despliegue sin reemplazo, victoria/empate
y muerte súbita.

> **¿Vas a tocar el código?** Leé primero **[`docs/dev-guide.md`](docs/dev-guide.md)**: un mapa de
> dónde vive cada cosa y recetas para agregar cartas/efectos/estados, sprites y animaciones de unidad.

El **balance** se valida en `sim/` (Python): re-implementa las reglas, corre miles de partidas con
una política heurística (spec §16) y reporta win-rate y duración. Los valores finales se vuelcan a
`docs/game-spec.md` y `Core/CardLibrary.cs`.

## Estructura del repositorio

```
.
├── docs/game-spec.md          # Especificación completa del juego (fuente de verdad)
├── sim/                       # Simulador de balance en Python (reglas + barrido de knobs)
├── tools/
│   └── gen_click.py           # Generador de SFX sintéticos
├── CLAUDE.md                  # Guía de arquitectura y convenciones
└── PiqueteDefend/             # Proyecto Unity (6000.5.0f1, URP)
```

---

## Empezar

> ⚠️ El repo usa **Git LFS** para imágenes y audio. Instalá LFS antes de clonar:
> ```bash
> git lfs install
> git clone https://github.com/francalonje/PiqueteDefend.git
> ```

1. Abrí la carpeta `PiqueteDefend/` con Unity **6000.5.0f1** (vía Unity Hub).
2. Abrí la escena `Assets/PiqueteDefend/Scenes/Main.unity`.
3. **Play**.

## Desarrollo

**Tests** — `Window → General → Test Runner → EditMode`, o headless:
```bash
Unity.exe -batchmode -projectPath PiqueteDefend -runTests -testPlatform EditMode \
          -testResults results.xml -logFile -
```

**Regenerar cartas y escenas** — desde el menú `PiqueteDefend` del editor
(*Generate Card Library*, *Setup UI Scenes*). Los assets se generan desde código,
así que un cambio de balance se reaplica con un clic.

## Estado

**v0.2.0 — versión jugable con la capa de cartas e interacción completas.** Partida de punta a
punta: menú → selección de facción → juego hotseat → victoria/revancha, con fondos y audio en las
tres pantallas.

**Implementado:**
- Catálogo completo (44 cartas) con unidades por arquetipo, pasivas variadas, estados por unidad,
  acciones y equipo, **en el núcleo C#** (Core), con balance validado por simulación y **42 tests
  EditMode** en verde.
- **UI de juego completa:** drag&drop de cartas a las zonas o **directo al slot** (con *highlight* de
  slots elegibles), *highlight* de las unidades que pueden actuar, targeting de equipar y de
  mover/intercambiar (dos clicks), **badges** de estado/equipo por unidad y **popover informativo**
  en hover (unidades y cartas). Despliegue **sin reemplazo** (no se apila sobre otra unidad).

**Próximas ideas:** arte/sprites y animaciones de unidad (idle/ataque/muerte — ver
[`docs/dev-guide.md`](docs/dev-guide.md)), pistas de música propias por pantalla, y elegir facción
por jugador (hoy los lados son fijos). Fuera de scope de v1: deckbuilding, online, +2 jugadores.

---

*Proyecto personal. Humor y nombres de cartas son sátira política, sin afiliación real.*
