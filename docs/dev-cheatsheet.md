# PiqueteDefend — Cheatsheet del dev

Referencia rápida. El detalle está en [`dev-guide.md`](dev-guide.md); las reglas, en
[`game-spec.md`](game-spec.md). Rutas relativas a `PiqueteDefend/Assets/PiqueteDefend/`.

## Comandos

```bash
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Unity.exe"

# compilar + regenerar cartas y escenas
"$UNITY" -batchmode -projectPath PiqueteDefend -quit -logFile compile.log \
  -executeMethod PiqueteDefend.EditorTools.SceneSetup.SetupEverything

# tests EditMode (compila TODO; resultados en PiqueteDefend/results.xml)
"$UNITY" -runTests -batchmode -projectPath PiqueteDefend -testPlatform EditMode \
  -testResults results.xml -logFile tests.log     # ⚠ cerrá el editor antes

py sim/main.py run            # balance, config que ship­ea (SHIPPED)
py sim/parity_check.py        # sim × SHIPPED == catálogo del Core
```

## Quiero… → tocá esto

| Quiero… | Archivo(s) | Después |
|---|---|---|
| Agregar/editar una **carta** | `Core/CardLibrary.cs` | Regenerar (menú *Generate Card Library*) + espejar en `sim/cards.py` + `parity_check.py` + spec §9/§10 |
| Cambiar **stats/costo/ataque** de una unidad | `Core/CardLibrary.cs` (`Unit/Atk/Heal`) | idem arriba |
| Marcar **unidad inicial** de facción | `Core/CardLibrary.cs` → `StartingUnitIds()` | — |
| Nuevo **efecto de carta** | `Enums.cs` (`CardEffectType`) + `GameEngine.ResolveEffect` | espejo `sim/rules.py` `_resolve_effect` + test |
| Nueva **pasiva** | `Enums.cs` (`PassiveType`) + `GameEngine` (según timing) | espejo `sim/rules.py` + `policy._passive_value` + test |
| Nuevo **estado/debuff** | `Enums.cs` (`StatusType`) + `StatusEffect.IsPlayerStatus` + `GameEngine` | espejo `sim/` + badge UI (§abajo) + test |
| Cambiar **recursos/producción/sd/maxTurns/reglas inicio** | `Core/GameConfig.cs` | espejo `sim/knobs.py` (`SHIPPED`) |
| Cambiar **victoria/empate/desempate** | `GameEngine.CheckVictory` / `TimeoutTiebreak` | espejo `sim/rules.py` + test |
| **Sprite** de una unidad/carta | `Model/CardData.cs` (`sprite`) → `GameController.RenderSlotColumn` / `BuildCardVisual` | clase en `Game.uss` (`background-image`/`background-size`) |
| **Animación** (idle/ataque/golpe/muerte) | `GameController` (`FlashElement`/`ShakeElement`/`ApplyPendingAnimations`) + `Game.uss` (`slot--flash-*`) | hooks: `CardData.animationHook`, `UnitAttack.hitSoundId` |
| **Sonido** propio de carta/ataque | `CardData.playSoundId` / `UnitAttack.hitSoundId`; clips en `Presentation/Resources/Audio/` | ids en `App/AudioId.cs` |
| **Badge** de un estado nuevo | `GameController.StatusBadge` + clase `badge--*` en `Game.uss` | — |
| Texto del **popover** (alcance/pasiva/efecto) | `GameController`: `ReachText`/`PassiveText`/`EffectPart`/`ApplyStatusText` | — |
| Layout/estilos de la **pantalla** | `Presentation/UI/Game.uxml` + `Game.uss` | — |

## Enums → dónde se resuelven (Core)

| Enum (`Enums.cs`) | Resolución |
|---|---|
| `CardEffectType` | `GameEngine.ResolveEffect` |
| `PassiveType` | `BeginTurn`/`ResolveTurnStartPassives` (turno), `AuraBonusFor`/`EffectiveAttackDamage` (aura), `AttackWithUnit` (Retaliate) |
| `StatusType` | Poison: `BeginTurn`; Furia/Desmoralizar: `EffectiveAttackDamage`; Stun: `AttackWithUnit` (`UnitSlot.IsStunned`); counter: `TickUnitStatuses` |
| `AttackReference`/`AttackEffect` | `GameEngine.ResolveSlots` / `AttackWithUnit` |
| `StatType` | `UnitSlot.MaxHp` / `EquipmentDamage` |

## Reglas clave (no olvidar)

- **Lógica → Core (C# puro). UI sólo lee y manda comandos.** Cartas = data.
- **Extender = enum + resolución en `GameEngine` (un solo lugar).**
- **Estados por unidad:** counter baja **al fin del turno del dueño**; Poison **daña en EFECTOS** (inicio).
  Estados de jugador (producción): *fire-on-expiry* (bajan en EFECTOS, disparan al llegar a 0).
- **Daño efectivo** = base + equipo + Furia + Aura − Desmoralizar (`EffectiveAttackDamage`).
- **Despliegue sin reemplazo:** slot debe estar **libre** y permitido (spec §8.3).
- **Pasivas no honran `pickCount`** → usar `pick 0` (afectan todo el patrón).
- **RNG inyectable** (`IRandomProvider`); nada de `Random` global.
- **Sim espeja al motor:** cambiás regla en Core → replicá en `sim/rules.py` y corré `parity_check.py`.

## Gotchas UI Toolkit

- `PanelSettings` no persiste por escena → se carga en runtime desde `Resources/UIPanelSettings`.
- **Stylesheets del `<Style>` viven en `.screen--game`, no en `rootVisualElement`.** Elementos
  agregados al root (popovers, ghost) **no heredan** `Game.uss` → usar `GameController.Stylize(el)`.
- Escalado `ScaleWithScreenSize`, ref 1200×800, **match ancho** → canvas lógico siempre 1200px.
- `background-size: cover`/`100% 100%` (NO `-unity-background-scale-mode`, Unity 6 lo ignora).

## Borrar/regenerar

- Borrar asset con editor cerrado: eliminá el archivo **y** su `.meta`.
- Binarios (imágenes/audio) van por **Git LFS**.
- Cartas/escenas se regeneran desde código (menú **PiqueteDefend**) — un cambio de balance se reaplica con un clic.
