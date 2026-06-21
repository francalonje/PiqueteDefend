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

Un sistema de *muerte súbita* a partir del turno 30 garantiza que ninguna partida sea eterna.

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

> **Balance:** provisional y sin validar — los valores (HP/daño/costos) se afinarán con un
> simulador en Python (combate posicional, victoria por KO).

## Cómo jugar

1. **Jugar** en el menú → elegís qué facción juega primero (los lados son fijos por ahora: **Manifestantes** a la izquierda, **Policías** a la derecha).
2. En tu turno, las unidades con pasiva generan recursos (la producción base es 0; el turno 1 no produce). Luego podés **jugar o descartar una carta** y **atacar con una unidad** (en cualquier orden).
3. El turno pasa al rival. Repetir hasta que un jugador se quede sin unidades (KO).

Las cartas aplican **efectos** al rival o a uno mismo: sobre recursos, sobre la producción
(bloquearla/duplicarla), o **estados sobre una unidad** (veneno, aturdir, furia, desmoralizar).
También podés **mover** tus unidades o **intercambiar** las del rival para romperle la formación.

**Controles:**
| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta | Arrastrar sobre zona **JUGAR** | Seleccionar (1–6) + Enter |
| Descartar carta | Arrastrar sobre zona **DESCARTAR** | Seleccionar (1–6) + Backspace |
| Equipar a una unidad | Arrastrar el equipo sobre la unidad | — |
| Atacar / curar con unidad | Clic en la unidad → clic en el popover | — |
| Elegir slot objetivo (o mover/intercambiar) | Clic en el/los slot(s) | — |

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
tests EditMode (a retomar al estabilizar el diseño de cartas) cubren producción, timing de
status, condiciones de victoria, muerte súbita y combate.

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
selección de facción → juego hotseat → victoria/revancha, con fondos en las tres
pantallas y audio (SFX de clic + música en menú, selección y partida; hoy las tres
comparten la misma pista placeholder, en slots separados para divergir luego).

**En diseño (próxima iteración):** el catálogo de cartas completo —unidades diferenciadas por
arquetipo, pasivas variadas, estados por unidad, cartas de acción ampliadas y equipo— está
especificado en `docs/game-spec.md` y entrando a implementación. La build jugable v0.1.0
todavía corre la baseline uniforme previa; el balance fino se hará con un simulador en Python.

Pendientes / ideas a futuro: pistas de música propias por pantalla, arte de cartas,
animaciones de combate más ricas, indicadores de efectos/estados por unidad y permitir
elegir facción por jugador (hoy los lados son fijos). Fuera de scope de v1:
deckbuilding, online, +2 jugadores.

---

*Proyecto personal. Humor y nombres de cartas son sátira política, sin afiliación real.*
