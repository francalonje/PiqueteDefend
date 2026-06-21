# PiqueteDefend — Guía del desarrollador

Manual práctico para laburar el proyecto: **dónde vive cada cosa** y **recetas** para los cambios
más comunes (cartas, efectos, ataques, estados, condiciones de partida, sprites y animaciones).

> **Fuentes de verdad.** Reglas y diseño: [`game-spec.md`](game-spec.md). Arquitectura y
> convenciones: [`../CLAUDE.md`](../CLAUDE.md). Este doc es el "cómo se hace", no reemplaza a esos.
>
> **¿Apurado?** [`dev-cheatsheet.md`](dev-cheatsheet.md) es la versión de una página (tablas
> "quiero X → tocá Y", comandos y reglas clave).

---

## 1. Regla de oro (leer antes de tocar nada)

- **La lógica del juego vive en `Core` (C# puro), nunca en un MonoBehaviour.** El núcleo es
  determinista y testeable sin abrir el editor. La presentación sólo lee estado del motor y le manda
  comandos.
- **Las cartas son data**, no código. Agregar/editar una carta = editar `CardLibrary.cs` y regenerar
  los assets; no se toca el motor.
- **Extender el juego = un valor nuevo en un enum + su resolución en `GameEngine` (un solo lugar).**
- **Aleatoriedad inyectable:** el núcleo recibe un `IRandomProvider`; nunca usa `Random` global.
- **El simulador (`sim/`) es un espejo del motor.** Si cambiás una regla en `GameEngine`, replicala
  en `sim/rules.py` y corré `py sim/parity_check.py`.

---

## 2. Mapa del proyecto

```
PiqueteDefend/                         # raíz del repo
├── docs/
│   ├── game-spec.md                   # ESPEC del juego (fuente de verdad de reglas)
│   └── dev-guide.md                   # este archivo
├── sim/                               # simulador de balance en Python (espejo del motor)
│   ├── rules.py  cards.py  model.py   # motor / catálogo / tipos (espejo de Core)
│   ├── knobs.py                        # GlobalKnobs + SHIPPED (config que ship­ea) + BASELINE
│   ├── policy.py  sweep.py  main.py    # bot heurístico / batch / CLI
│   ├── valuation.py                     # pricing per-card por valor/costo (Fase 1 del balance per-card)
│   └── parity_check.py                 # verifica cards.py × SHIPPED == catálogo del Core
├── CLAUDE.md                          # arquitectura y convenciones
└── PiqueteDefend/                     # proyecto Unity (6000.5.0f1, URP)
    └── Assets/PiqueteDefend/
        ├── Core/                       # === DOMINIO (C# puro) ===
        │   ├── GameEngine.cs           #   motor: turnos, ataques, efectos, estados, victoria
        │   ├── GameConfig.cs           #   parámetros de balance/reglas (recursos, sd, maxTurns…)
        │   ├── CardLibrary.cs          #   FUENTE programática de las 44 cartas
        │   ├── CardCatalog.cs          #   ScriptableObject que consume el motor (lo genera el editor)
        │   ├── Enums.cs                #   TODOS los enums (CardEffectType, StatusType, PassiveType…)
        │   ├── GameOutcome.cs          #   GamePhase, ActionResult, GameOutcome
        │   ├── IRandomProvider.cs / ICardCatalog.cs
        │   └── Model/                  #   CardData, UnitCardData, ActionCardData, EquipmentCardData,
        │                               #   CardEffect, PassiveEffect, StatusEffect, UnitAttack,
        │                               #   StatModifier, ResourceCost, UnitSlot, PlayerState
        ├── Presentation/               # === UI / AUDIO (MonoBehaviours, UI Toolkit) ===
        │   ├── Screens/GameController.cs   # puente motor↔UI: render, drag&drop, popovers, animaciones
        │   ├── Screens/MainMenuController.cs, FactionSelectController.cs
        │   ├── UI/Game.uxml, Game.uss   # layout y estilos de la pantalla de juego
        │   ├── UI/MainMenu.*, FactionSelect.*, Common.uss
        │   ├── App/AudioManager.cs, AudioId.cs   # audio (clips desde Resources/Audio/)
        │   ├── App/SceneBackground.cs, MatchConfig.cs
        │   └── Resources/              # UIPanelSettings, CardCatalog, bg-*, Audio/*  (cargados en runtime)
        ├── Editor/                      # === GENERADORES (no entran al build) ===
        │   ├── CardLibraryGenerator.cs  #   menú "PiqueteDefend/Generate Card Library"
        │   └── SceneSetup.cs            #   menú "PiqueteDefend/Setup UI Scenes" + SetupEverything()
        ├── Data/Cards/                  # assets de cartas generados (ScriptableObjects)
        ├── Scenes/                      # Main, FactionSelect, Game (generadas por SceneSetup)
        └── Tests/EditMode/GameEngineTests.cs   # tests del núcleo (NUnit)
```

---

## 3. Compilar, testear, correr

Unity está en `C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe`.

```bash
# Compilar todo + regenerar cartas y escenas (headless)
Unity.exe -batchmode -projectPath PiqueteDefend \
  -executeMethod PiqueteDefend.EditorTools.SceneSetup.SetupEverything -quit -logFile compile.log

# Correr los tests EditMode (compila TODOS los ensamblados, incluida la Presentación)
Unity.exe -runTests -batchmode -projectPath PiqueteDefend \
  -testPlatform EditMode -testResults results.xml -logFile tests.log
# resultados en PiqueteDefend/results.xml  (total/passed/failed en <test-run>)

# Simulador de balance (Python, usar `py` en esta máquina)
py sim/main.py run            # config que ship­ea (SHIPPED), n=500
py sim/main.py run --n 5000   # batch grande
py sim/main.py run --n 10000 --paired   # balance de facción: 2n partidas (first=0 y first=1
                                        # por seed, mismos draws) + IC95 sobre las decisivas
py sim/main.py game --seed 7  # traza de 1 partida
py sim/parity_check.py        # sim × SHIPPED == catálogo del Core (correr si tocás cards/knobs)
py sim/valuation.py           # ranking valor/costo per-card: detecta las cartas más fuertes a encarecer
```

Desde el editor abierto: `Window → General → Test Runner` (EditMode) y el menú **PiqueteDefend**
(*Generate Card Library*, *Setup UI Scenes*).

> **Borrar un asset con el editor cerrado:** eliminá el archivo **y** su `.meta`. Binarios
> (imágenes/audio) van por **Git LFS** — `git lfs install` antes de clonar.

---

## 4. Recetas — Lógica (Core)

### 4.1 Agregar o editar una carta

Todo se hace en [`Core/CardLibrary.cs`](../PiqueteDefend/Assets/PiqueteDefend/Core/CardLibrary.cs)
(`BuildManifestantes()` / `BuildPolicias()`), usando los helpers `Unit(...)`, `Action(...)`,
`Equipment(...)`, `Atk(...)`, `Heal(...)`, `Produce/Aura/Espinas/Regen/...`, `ModHP/ModRes/ApplyStatus/...`.

1. Agregá/editá la entrada en la lista de la facción.
2. Menú **PiqueteDefend → Generate Card Library** (o `SetupEverything` headless) para regenerar los
   assets de `Data/Cards/` y el `CardCatalog` de `Resources/`.
3. Si la carta es **inicial** de la facción, agregá su `id` en `StartingUnitIds(...)`.
4. **Replicá el valor en `sim/cards.py`** (mismos campos, valores **base** pre-tune; ver §6) y corré
   `py sim/parity_check.py`. Actualizá la tabla del spec §9/§10.

> Los números del Core están **horneados** (ya con el tune de balance). El sim usa valores **base** ×
> knobs `SHIPPED`; `parity_check.py` garantiza que coincidan.

### 4.2 Modificar un ataque (a qué pega y cuánto)

El ataque de una unidad es un `UnitAttack` (`Model/UnitAttack.cs`):

- `reference`: `Absolute` (los `pattern` son slots fijos del rival 0–5) o `Relative` (offsets desde el
  atacante; `0` = enfrentado, `+` = hacia el frente enemigo).
- `pattern` (`int[]`): slots/offsets candidatos.
- `pickCount`: `0` = pega todos los del patrón; `N>0` = el atacante elige N.
- `damagePerSlot`: daño (o curación si `effect = HealAllies`).
- `effect`: `DamageEnemies` (default) o `HealAllies` (cura sobre el tablero propio).

Presets nombrados (duelo, banda, cleave, zona fija, libre elección) en **spec §6**. La resolución de
slots es `GameEngine.ResolveSlots(reference, pattern, origin)`. El daño **efectivo** (base + equipo +
Furia + Aura − Desmoralizar) lo calcula `GameEngine.EffectiveAttackDamage(...)`.

### 4.3 Agregar un tipo de efecto de carta (CardEffectType)

1. Nuevo valor en `enum CardEffectType` (`Enums.cs`).
2. Resolvelo en `GameEngine.ResolveEffect(...)` (un solo `switch`). Si usa un segundo slot, mirá
   `CardEffect.targetSlotB` y `NeedsSecondSlot`.
3. Si apunta a una unidad, revisá `CardEffect.TargetsAUnitSlot` (`Model/CardEffect.cs`) para que la
   UI sepa pedir target.
4. Espejá en `sim/rules.py` (`_resolve_effect`) y `sim/model.py` (enum). Agregá test.

### 4.4 Agregar una pasiva (PassiveType)

1. Nuevo valor en `enum PassiveType` (`Enums.cs`).
2. Resolución según su *timing*:
   - inicio de turno (producción/regeneración/daño/estado por turno) → `GameEngine.BeginTurn` /
     `ResolveTurnStartPassives`.
   - continuo al atacar (aura) → `GameEngine.AuraBonusFor` / `EffectiveAttackDamage`.
   - reactivo al ser golpeada (espinas) → en `AttackWithUnit` (loop de `Retaliate`).
3. Las pasivas dirigidas usan el **mismo targeting que un ataque** (`target` + `reference` + `pattern`)
   vía `PassiveTargets(...)`. **Ojo:** hoy **no honran `pickCount`** (afectan todo el patrón); usar
   `pick 0` (ver spec §7.3). Las pasivas de **equipo** también cuentan: `UnitSlot.AllPassives()`.
4. Espejá en `sim/rules.py` + `sim/policy.py` (`_passive_value` para que el bot la valore). Test.

### 4.5 Agregar un estado / debuff (StatusType)

1. Nuevo valor en `enum StatusType` (`Enums.cs`).
2. ¿Vive en el **jugador** (producción) o en la **unidad**? Declaralo en
   `StatusEffect.IsPlayerStatus(...)` (`Model/StatusEffect.cs`).
3. **Timing** (clave, validado en el sim — ver spec §6/§7.7):
   - estados por unidad = *active-while-present*: el `counter` se decrementa **al fin del turno del
     dueño** (`GameEngine.TickUnitStatuses`); Poison **daña en EFECTOS** (inicio).
   - estados de jugador = *fire-on-expiry*: decrementan en EFECTOS y disparan al llegar a 0.
4. Consumir el estado donde corresponda: daño (Poison) en `BeginTurn`; modificar daño (Furia/
   Desmoralizar) en `EffectiveAttackDamage`; bloquear acción (Stun) en `AttackWithUnit`
   (`UnitSlot.IsStunned`).
5. Para aplicarlo desde una carta: `CardEffectType.ApplyStatus` ya rutea jugador-vs-unidad solo.
6. Espejá en `sim/` + UI (badge + texto): ver §5.3. Test.

### 4.6 Condiciones de partida / balance (GameConfig)

Todos los parámetros viven en [`Core/GameConfig.cs`](../PiqueteDefend/Assets/PiqueteDefend/Core/GameConfig.cs):
recursos iniciales, producción base por recurso, `maxResource`, reglas de iniciativa
(`firstProducesTurn1`, `firstNoAttackTurn1`), `suddenDeathStart`/`suddenDeathDamage`, `maxTurns`,
`handSize`, `maxSlots`, e **inflación** (`inflationStartTurn`, `inflationPercentPerTurn`, §3). La
condición de victoria (KO / empate / timeout) y el desempate están en `GameEngine.CheckVictory` /
`TimeoutTiebreak`. Espejo de los knobs en `sim/knobs.py` (`SHIPPED`).

> **Unidades iniciales (3/facción):** `CardLibrary.StartingUnitIds` + `cards.py STARTING_IDS` —
> Escaramuza + Productora + Muro (el Muro despliega al frente → presencia en vanguardia desde el
> turno 1). **`drawWeight`** se deriva por tipo en los builders (no carta por carta): productoras y
> boosts de producción 2, resto (incl. unidades comunes) 1 (spec §8.1; subir unidades tapa la mano
> con 3 iniciales). El sim reporta **presencia** (unidades vivas/lado, vanguardia, unidades en mano)
> en `run` para medir "tablero lleno" vs "mano tapada".

> **Costo de cartas — dos multiplicadores (spec §3):** (1) **factor global ×1.2**, horneado al
> generar los assets (`CardLibrary.CostScale`, espejo de `knobs.cost_mult`); los `amount` de los
> builders son el costo **base**. (2) **Inflación** por turno: `GameEngine.InflationPercent` →
> `PlayerState.InflatedAmount(amount, pct)` (ceil) en `CanAfford`/`Pay`; espejo en
> `sim/rules.py` (`inflated_amount`, `GameEngine.inflation_percent`) y el bot lo respeta
> (`policy.take_turn` pasa `engine.inflation_percent`). La UI muestra costo inflado + medidor
> (`GameController.RenderInflationMeter`).

### 4.7 Tests

`Tests/EditMode/GameEngineTests.cs`. Patrón: armás el tablero **antes** de `BeginTurn`, usás el RNG
determinista (`ZeroRng`/`FixedRng`) y el `TestCatalog` en memoria. Hay helpers `U(...)`, `Duel(...)`,
`Aura/Retaliate/...`. Agregá un test por cada regla/interacción nueva y mantené la **paridad con el
sim**.

---

## 5. Recetas — Presentación (UI / arte / animación)

> La pantalla de juego es **UI Toolkit** (UXML/USS), no GameObjects. Las unidades en el tablero son
> `VisualElement`s construidos en `GameController.RenderSlotColumn(...)`. Las cartas, en
> `BuildCardVisual(...)`.

### 5.1 Agregar el sprite de un personaje a una unidad

1. Importá la imagen a `Assets/` (queda versionada por **Git LFS**). En `CardData` ya existe el campo
   **`sprite`** (`Model/CardData.cs`) — asignalo al asset de la carta (o seteá `card.sprite` en
   `CardLibrary` si querés que lo haga el generador; hoy no lo hace).
2. Mostralo en el **tablero**: en `GameController.RenderSlotColumn(...)`, donde hoy se agregan
   `slot__name` + HP, poné el sprite como fondo del elemento del slot:
   ```csharp
   if (slot.unit.sprite != null)
       el.style.backgroundImage = new StyleBackground(slot.unit.sprite);
   ```
   (o agregá un hijo `VisualElement` con `background-image` para no pisar el color del slot).
3. Mostralo en la **carta**: en `BuildCardVisual(...)` agregá un `VisualElement` con
   `style.backgroundImage = new StyleBackground(card.sprite)` y su clase en `Game.uss`
   (la spec §11.5 ya pide "Imagen" en la carta).
4. Para el **estilo** (tamaño, recorte) agregá clases en `Game.uss`. Usá `background-size` (cover /
   `100% 100%`); **no** uses `-unity-background-scale-mode` (Unity 6 lo ignora).

### 5.2 Animaciones de unidad (idle / ataque / golpe / muerte)

Hoy las animaciones de combate están en `GameController` y son **UI Toolkit puro**:
- **Flash** de color: clases USS `slot--flash-attack` / `slot--flash-hit` / `slot--flash-dead`
  (transición CSS) que se agregan y se quitan con `FlashElement(...)`.
- **Shake**: `ShakeElement(...)` mueve `transform.position` con `schedule.Every(...)`.
- El flujo: `PrepareAttackAnimation` toma snapshot de HP **antes** del ataque; `ApplyPendingAnimations`
  compara contra el estado posterior y anima los slots afectados.

Para **agregar/ampliar** animaciones dentro de este modelo:
- **Idle**: una clase USS con `transition`/`scale` aplicada al slot ocupado (ej. un leve "respirar"),
  o swap de frames de un sprite-sheet vía `schedule.Execute(...).Every(ms)` cambiando `background-image`.
- **Ataque / golpe / muerte**: seguí el patrón de `FlashElement`/`ShakeElement` — agregá una clase USS
  con la transición y toggléala, o programá cambios de `style`/`transform` con `schedule`.
- **Hooks ya disponibles** para no hardcodear por carta:
  - `CardData.animationHook` (string, **[FUTURO]**) — nombre de animación a disparar al jugar.
  - `UnitAttack.hitSoundId` y `CardData.playSoundId` — ids de sonido (ver §5.4).
- **[DEFINIR] (spec §7.10):** si el feedback por unidad necesita config por carta, crear un SO
  `CardPresentation` en *Presentation* indexado por `id` (sprites idle/death, hooks, etc.). Hoy se
  resuelve con convenciones globales.

> **Si querés animación con `Animator`/`SpriteRenderer`** (esqueletal, spritesheets complejos): eso
> implica meter **GameObjects** en la escena de juego, que hoy es UI-Toolkit-only. Es un cambio de
> arquitectura de la *presentación* (no del Core) — mantené la lógica en el motor y que la capa de
> GameObjects reaccione a eventos/estado del `GameEngine`, igual que hoy lo hace la UI.

### 5.3 Indicadores de estado/equipo (badges) y popover de info

- **Badges** por unidad: `GameController.AddStatusBadges(...)` + clases `badge--poison/stun/furia/
  desmor/equip` en `Game.uss`. El texto/tooltip por estado: `StatusBadge(...)`; por equipo:
  `EquipBadgeText(...)` / `EquipmentText(...)`. Al agregar un `StatusType` nuevo, sumá su caso acá.
- **Popover informativo** (hover): `ShowInfoPopover` (unidad) y `ShowCardInfo` (carta), que comparten
  `BeginInfoPanel`. Textos legibles: `ReachText`, `PassiveText`, `EffectPart`/`ApplyStatusText`,
  `AttackInfoText`, `DeployZoneText`. Estilo: clases `info-popover*` en `Game.uss`.
- **Highlight "puede actuar"**: clase `slot--can-act`, condición en `WireSlot` (`Mode.Acting`).
- **Highlight de drop** al arrastrar: clase `slot--drop-ok`, en `HighlightDropTargets(...)`.

### 5.4 Audio

Clips en `Presentation/Resources/Audio/` (música `music-*.mp3`, SFX `*.wav`). `AudioId.cs` tiene los
ids; `AudioManager` los carga por nombre desde `Resources/Audio/`. Para un sonido propio de carta o
ataque, seteá `CardData.playSoundId` / `UnitAttack.hitSoundId`; si están vacíos se usa el default
global (`Sfx(specific, fallback)` en `GameController`).

### 5.5 Gotchas de UI Toolkit (ya pisados, no repetir)

- **PanelSettings no persiste** al generar escenas por script: se carga en runtime desde
  `Resources/UIPanelSettings` en `OnEnable` de cada controller.
- **Las stylesheets del `<Style>` del UXML están en el elemento raíz `.screen--game`, no en
  `rootVisualElement`.** Los elementos que se agregan a `rootVisualElement` (popovers, ghost de drag)
  **no heredan** Game.uss → se ven sin estilo. Solución: `GameController.Stylize(el)` copia las
  stylesheets de la pantalla al elemento.
- Escalado: `PanelSettings = ScaleWithScreenSize`, ref 1200×800, **match por ancho** → el canvas
  lógico siempre mide 1200px de ancho (FHD y QHD renderizan igual; lo que varía es el aspect ratio).

---

## 6. El simulador es un espejo — mantenelo en sync

`sim/` re-implementa las reglas en Python para balancear sin abrir Unity. **Toda regla que cambies en
`GameEngine` debe replicarse en `sim/rules.py`** (y el catálogo en `sim/cards.py`, los enums en
`sim/model.py`, los knobs en `sim/knobs.py`).

- Los valores de `cards.py` son **base** (pre-tune); `knobs.SHIPPED` (hp 0.85 / dmg 1.5 + reglas de
  iniciativa) los escala a los números **horneados** del Core. `run`/`game`/`dump` usan `SHIPPED` por
  default; `--baseline` da la config cruda.
- **`py sim/parity_check.py`** falla si `cards.py × SHIPPED` deja de coincidir con el catálogo del
  Core — corrélo siempre que toques cartas, knobs o reglas de despliegue/combate.
- La política del bot (`policy.py`) implementa spec §16; si agregás una mecánica que el bot debería
  usar/valorar, actualizala ahí o el balance que reporta el sim no la tendrá en cuenta.

---

## 7. Checklist para un cambio típico

- [ ] ¿Es **data** (carta/valor)? → `CardLibrary.cs` + regenerar + `cards.py` + `parity_check.py` + spec §9/§10.
- [ ] ¿Es **regla/mecánica**? → enum en `Enums.cs` + resolución en `GameEngine` (un lugar) + espejo en `sim/rules.py`.
- [ ] ¿Toca **balance/condiciones**? → `GameConfig.cs` + `sim/knobs.py`.
- [ ] **Tests EditMode** nuevos en verde.
- [ ] **UI** si hace falta (badges/popover/targeting en `GameController` + `Game.uss`).
- [ ] **Spec** (`game-spec.md`) actualizado — es la fuente de verdad.
- [ ] Compila headless sin errores y los tests pasan.
