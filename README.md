# PiqueteDefend

> Juego de cartas por turnos para 2 jugadores en local. **Manifestantes vs Policías**, con humor político argentino. Inspirado en Castle Wars, The King is Watching, Legends of Runeterra y Slay the Spire.

![Unity](https://img.shields.io/badge/Unity-6000.5.0f1-black?logo=unity)
![Pipeline](https://img.shields.io/badge/render-URP-blue)
![UI](https://img.shields.io/badge/UI-UI%20Toolkit-blue)

---

## Concepto

Dos facciones enfrentadas se baten en una pantalla compartida (hotseat). Cada jugador
administra tres recursos, despliega unidades en un tablero de 6 slots y juega cartas de
acción. Las unidades atacan según patrones de posición; el combate y los efectos de las
cartas van bajando el HP de las unidades rivales.

**Cómo se gana:** por **KO** — cuando el rival pierde su última unidad.

Un sistema de *muerte súbita* a partir del turno 30 garantiza que ninguna partida sea eterna.

## Facciones

- **Manifestantes** — bombos, banderas, pañuelos, ollas populares.
- **Policías** — escudos, patrulleros, gases lacrimógenos, comisarías.

32 cartas (16 por facción) entre **unidades** (persistentes; atacan y producen recursos)
y **acciones** (boost, sabotaje, ataque, defensa, efecto especial).

Cada facción tiene pool propio temático y una o más **unidades iniciales** que se despliegan
gratis al empezar la partida.

> **Balance:** todavía sin validar. Las unidades usan una baseline uniforme de prueba
> (20 HP / 5 daño) para iterar jugabilidad antes de diferenciar y balancear.

## Cómo jugar

1. **Jugar** en el menú → cada jugador elige su facción.
2. En tu turno, las unidades con pasiva generan recursos (la producción base es 0; el turno 1 no produce). Luego podés **jugar o descartar una carta** y **atacar con una unidad** (en cualquier orden).
3. El turno pasa al rival. Repetir hasta que un jugador se quede sin unidades (KO).

Algunas cartas aplican **efectos de status** al rival o a uno mismo (bloquear producción, duplicarla) que se activan al inicio del turno siguiente.

**Controles:**
| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta | Arrastrar sobre zona **JUGAR** | Seleccionar (1–6) + Enter |
| Descartar carta | Arrastrar sobre zona **DESCARTAR** | Seleccionar (1–6) + Backspace |
| Atacar con unidad | Clic en la unidad → clic en el popover | — |
| Elegir slot objetivo | Clic en el slot | — |

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

La **fuente de verdad de las reglas** es `docs/game-spec.md`. El núcleo C# lo implementa y
los tests EditMode cubren producción, timing de status, condiciones de victoria, muerte
súbita y unidades.

## Estructura del repositorio

```
.
├── docs/game-spec.md          # Especificación completa del juego
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

**v0.1.0 — primera versión jugable.** Partida completa de punta a punta: menú →
selección de facción → juego hotseat → victoria/revancha, con fondos y audio
(SFX de clic + música in-game).

Pendientes / ideas a futuro: música de lobby, arte de cartas, animaciones, indicadores
de efectos más visuales. Fuera de scope de v1: deckbuilding, online, +2 jugadores.

---

*Proyecto personal. Humor y nombres de cartas son sátira política, sin afiliación real.*
