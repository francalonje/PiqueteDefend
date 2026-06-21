# PiqueteDefend — Game Specification

## 1. Concepto

Juego de cartas por turnos para 2 jugadores en local (hotseat). Dos facciones enfrentadas en el contexto político-cómico argentino: **Manifestantes** vs **Policías**. Inspirado en Castle Wars, The King is Watching, Legends of Runeterra y Slay the Spire.

**[FUTURO]** Soporte para Jugador vs IA y Jugador vs Jugador online.

---

## 2. Facciones

### Manifestantes
- Representan al pueblo organizado en la calle.
- Estética: bombos, banderas, pañuelos, ollas populares.

### Policías
- Representan la fuerza del orden.
- Estética: escudos, patrulleros, gases lacrimógenos, comisarías.

Cada facción tiene su propio pool de cartas temático, pero comparten la misma mecánica de recursos y reglas.

Una facción es **data**, no código. Se define por:
- Su **pool de cartas** (unidades, acciones, equipo).
- Sus **unidades iniciales** — la(s) unidad(es) que despliega gratis al empezar la partida (§6).
- Sus **assets** (sprites, sonidos).

**[FUTURO]** El sistema debe soportar agregar nuevas facciones sin modificar código.

---

## 3. Recursos

Cada jugador maneja 3 recursos independientes.

| Recurso | Descripción | Producción base | Máximo |
|---------|-------------|-----------------|--------|
| **Dinero** ($) | Plata para movilizar gente y pagar logística | +1 / turno | 100 |
| **Fuerza** (⚡) | Capacidad física, represión o resistencia | +1 / turno | 100 |
| **Social** (📣) | Apoyo popular, organización, narrativa | +1 / turno | 100 |

> La producción base es **+1 de cada recurso por turno**. Encima de eso, las unidades con pasiva de producción y las cartas de Acción suman extra. Los valores de producción base son configurables desde código en un único lugar (`GameConfig`) para facilitar el balanceo.

**Recursos iniciales al inicio de la partida:** 5 de cada recurso (configurable para balanceo).

**Primer turno:** el primer jugador NO recibe producción en su turno 1 (ni base ni de unidades). La producción comienza a partir del turno 2.

**Recursos negativos:** los recursos nunca bajan de 0. El exceso de reducción se descarta.

---

## 4. Estado del jugador

Cada jugador tiene:

- **Recursos:** Dinero, Fuerza, Social — valor actual.
- **Mano:** 6 cartas. Al jugar o descartar una carta se repone con una aleatoria del pool de la facción.
- **Zona de unidades:** 6 slots numerados (1–6). La posición importa para el combate (§6). Cada slot contiene **a lo sumo una unidad** (no hay apilamiento).
- **activeStatuses:** `List<StatusEffect>` — buffs/debuffs temporizados. Se procesan al inicio del turno del jugador afectado.

> El jugador **no tiene HP propio**. Pierde cuando su última unidad muere (§5).

---

## 5. Condiciones de victoria

Un jugador gana cuando **la última unidad del oponente llega a 0 HP** (KO).

Como cada jugador arranca con al menos una unidad inicial (§6), nunca empieza la partida sin unidades: la derrota siempre es consecuencia de perder en combate la última unidad propia.

Puede haber **empate** (ver Empate, abajo). Existe un límite de turnos como salvavidas (ver §5.1).

### Momentos de evaluación
La condición de KO se evalúa:
1. Al final de cada resolución de ataque de unidad.
2. Inmediatamente después de resolver los efectos de una carta jugada (incluido cuando un efecto propio mata a una unidad propia).
3. Al aplicar el daño de muerte súbita.

### Empate (muerte simultánea)
Si ambos jugadores pierden su última unidad en el mismo instante (típicamente por el daño de muerte súbita, que golpea a todos a la vez), la partida termina en **empate**.

**[FUTURO]** Al implementar efectos de cartas habrá más formas de provocar muerte simultánea / empate.

### Recursos negativos
Los recursos nunca bajan de 0. El exceso de reducción se descarta.

### 5.1 Terminación garantizada (anti-stalemate)

**Muerte súbita (mecanismo principal):**
- A partir del turno `suddenDeathStart` (default: **50**), al final de cada turno todas las unidades de ambos jugadores reciben **1 punto de daño** (ignora defensas, es inevitable).
- Es un **backstop**, no la forma normal de ganar: se fija **bien por encima de la duración ideal** (§12) para que casi ninguna partida llegue. La duración objetivo se ajusta con HP/daño/recursos, no con este número.
- Garantiza que las unidades mueran eventualmente si ningún jugador es eliminado antes.

**Límite de turnos (backstop duro):**
- `maxTurns` (default: **120**). Si se alcanza sin ganador, la partida termina por desempate determinista:
  1. Jugador con más unidades vivas gana.
  2. Si empatan → mayor suma de HP total de unidades.
  3. Si aún empatan → gana el jugador que **no** fue primero.

---

## 6. Estructura de un turno

### Inicio de partida (setup)

1. Los lados son **fijos** (por ahora): **Manifestantes** a la izquierda, **Policías** a la derecha. La pantalla de selección (§11.2) solo decide qué facción juega primero.
2. Estado inicial de cada `PlayerState`:
   - **Recursos iniciales:** 5 de cada recurso (configurable).
   - **Unidades:** se despliegan las **unidades iniciales predefinidas** de la facción en sus slots (§2). El resto de los slots quedan libres.
   - **activeStatuses:** vacío.
   - **Mano:** 6 cartas aleatorias del pool de su facción.
3. Juega primero la facción elegida en §11.2 (`GameEngine.StartGame(..., firstIndex)`; si no se especifica, el motor cae a un **coinflip**).
4. Comienza el turno 1 con ese jugador.

### Loop de turno

```
1. EFECTOS      — Al inicio del turno del jugador activo, en orden:
                    a) Estados del JUGADOR (producción): counter--; al llegar a 0 se activa
                       (SkipProduction/DoubleProduction) y se elimina.
                    b) Estados por UNIDAD (§7.7): Poison hace su daño; Furia/Desmoralizar/Stun
                       siguen activos mientras counter>0; counter-- y se eliminan al llegar a 0.
                    c) Pasivas de inicio de turno (§7.3): Regeneration (cura), TurnDamage y
                       TurnStatus (daño/estado a los slots objetivo según su targeting).
                  → Evaluar condición de victoria (Poison / TurnDamage pueden matar)

2. PRODUCCIÓN   — Si NO es el turno 1 de la partida Y skipProduction no está activo:
                    a) Producción base: +1 de cada recurso (GameConfig)
                    b) Producción de unidades con efecto pasivo de producción
                    c) Multiplicar por productionMultiplier (default 1; doble si hay DoubleProduction)
                  → Evaluar condición de victoria

3. ACCIÓN       — El jugador puede hacer ambas, una, o ninguna (en cualquier orden):
                    a) Jugar 1 carta (si tiene suficiente recurso) O descartar 1 carta
                    b) Atacar con 1 unidad propia que NO esté aturdida (Stun) → afecta slots
                       según su ataque. Daño efectivo = base + Furia + AuraDamage − Desmoralizar;
                       los defensores con Retaliate devuelven daño al atacante.
                       [FUTURO] atacar con más de una unidad por turno o ataques con costo
                  → Evaluar condición de victoria tras cada acción

4. REPONER MANO — La carta jugada o descartada se reemplaza por una aleatoria del pool
                  [FUTURO] todas las cartas podrían cambiar entre turnos

5. FIN DE TURNO — Si turno ≥ suddenDeathStart: todas las unidades de ambos jugadores
                  reciben 1 de daño (ignora defensas)
                  → Evaluar condición de victoria
                  → Pasa el turno al oponente (o termina si turno == maxTurns)
```

### Resolución de ataque de unidad

El jugador selecciona una unidad propia que tenga un ataque disponible. El ataque (`UnitAttack`, §7.2) define **a qué slots del oponente pega** y **cuánto daño** a cada uno.

El conjunto de slots objetivo se resuelve según el ataque:

- **Marco de referencia (`reference`):**
  - `Absolute` — el `pattern` son números de slot del tablero del oponente (1–6). La posición de la unidad atacante no influye; solo importa dónde está parado el defensor.
  - `Relative` — el `pattern` son **offsets** respecto del slot de la unidad atacante. `0` = el slot enfrentado (mismo número en el tablero rival), `-1`/`+1` = los adyacentes, etc. Los offsets que caen fuera de 1–6 se descartan.

- **Selección (`pickCount`):**
  - `0` → el ataque golpea **todos** los slots de `pattern` (patrón obligatorio).
  - `N > 0` → el atacante **elige N** slots de entre los de `pattern` al momento de atacar. Para un ataque "a libre elección", el `pattern` incluye los 6 slots y el jugador elige cuáles.

El daño (`damagePerSlot`) se aplica al HP de la unidad que ocupe cada slot objetivo. **Si un slot objetivo está vacío, ese golpe se desperdicia (whiff)** — no se redirige. Si el HP de una unidad llega a 0, muere y el slot queda libre.

> **La posición es decisión defensiva:** colocar (o estar obligado a colocar, §8.3) unidades fuera de los patrones de ataque enemigos las protege; los ataques `Relative` y los obligatorios crean el juego de posicionamiento.

**[FUTURO]** Cada unidad podría tener múltiples poderes (`List<UnitAttack>`) con distintos patrones, o habilidades que recuperen HP.

### Geometría de combate (frente / retaguardia)

El tablero de cada jugador es **una línea de 6 slots**.

> **Numeración:** los slots se cuentan **1–6 cara al usuario** y **0–5 en código** (slot `k` del spec = índice `k-1`). Los **offsets `Relative` son idénticos** en ambas bases (un offset `+1` es `+1`); sólo los slots `Absolute` y los `allowedSlots` cambian de base.

- **Retaguardia = slots 1–3** (borde externo, lejos del rival; se dibujan al fondo). Las **unidades iniciales arrancan acá** (slots 1 y 2, §11.3).
- **Frente = slots 4–6** (cerca del rival).
- Los slots **se enfrentan por número**: mi slot `N` mira el slot `N` del rival (offset `Relative` `0`). `+` = hacia el frente enemigo (índice mayor); `−` = hacia su retaguardia.

La posición pesa por **dos palancas**:

1. **`allowedSlots`** encierra a cada unidad en una zona de deploy. Una productora frágil obligada a la retaguardia es difícil de alcanzar; un muro obligado al frente tapa la línea.
2. **El patrón del ataque** decide a qué slots enemigos llega:
   - `Absolute` con patrón fijo → pega siempre los mismos slots (ej. `{1,2,3}` = bombardeo de retaguardia); la posición del atacante no influye.
   - `Relative` → el índice donde parás al atacante decide su cobertura; adelantarlo o atrasarlo cambia a qué slots enemigos amenaza.
   - `Absolute {1..6}` `pick 1` (**libre elección**) → snipea cualquier slot. **Reservado a cartas de acción** (y, a lo sumo, alguna unidad premium), para no anular la posición.

### Catálogo de zonas y patrones (presets)

`allowedSlots` y `UnitAttack.pattern` son **data libre** (`int[]`): cualquier combinación es expresable sin tocar el motor. Estos son los **presets nombrados** recomendados — el vocabulario disponible; no todas las unidades del catálogo usan todos.

**Zonas de deploy (`allowedSlots`)**

| Preset | Slots |
|--------|-------|
| Cualquiera | `{}` (vacío) |
| Retaguardia | {1,2,3} |
| Frente | {4,5,6} |
| Pares | {1,2} · {3,4} · {5,6} |
| Mitades | {1,2,3} · {4,5,6} |

**Patrones de ataque (`UnitAttack`)**

| Preset | reference | pattern | pickCount | Efecto |
|--------|-----------|---------|-----------|--------|
| Duelo | Relative | `[0]` | 0 | pega el slot enfrentado |
| Banda (elige) | Relative | `[-1,0,+1]` | 1 | elige 1 de los 3 alrededor del enfrentado |
| Banda (cleave) | Relative | `[-1,0,+1]` | 0 | pega los 3 |
| X adelante | Relative | `[+1,+2]` | 0 | pega 1–2 slots hacia el frente enemigo |
| Zona fija | Absolute | `{a..b}` | 0 | pega una zona fija (ej. retaguardia {1,2,3}) |
| Libre elección | Absolute | `{1..6}` | 1 | snipea cualquiera (reservado a acciones) |

> **Heurística de balance** (se valida por simulación, Fase 5): `daño_total = damagePerSlot × slots_que_pega`. A más slots golpeados o más HP, menos daño por golpe; a menos HP, más daño. Guía: `valor ≈ HP/4 + daño_total/2 + flexibilidad_de_deploy + valor_pasiva`; el costo de la carta sigue a ese valor.
>
> **Principio vainilla:** una **pasiva** (o una acción de utilidad como **curar**) suma al `valor`. Una unidad **vainilla** —sin pasiva y con ataque de daño normal— recupera ese presupuesto como **+HP o +daño**: no debería existir una unidad que no aporte nada extra y además pegue/aguante como una que sí.

---

## 7. Arquitectura técnica (Unity)

### Separación de capas

El **dominio** (`PiqueteDefend.Core`) contiene solo reglas: es C# determinista, testeable sin abrir el editor, y solo usa `UnityEngine` para `ScriptableObject` y `Sprite`. **Todo lo visual y de audio vive en `PiqueteDefend.Presentation`**, no en el dominio. En particular, ni `AudioClip` ni `AnimationClip` aparecen en Core (ver §7.3 y §7.10).

### 7.1 Jerarquía de CardData

Toda carta es un ScriptableObject. La jerarquía de herencia permite que cada tipo tenga sus propios campos sin contaminar la base.

**CardData (base)** — dominio puro.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | string | Identificador único |
| `cardName` | string | Nombre visible |
| `faction` | Faction | Manifestantes / Policías |
| `cardType` | CardType | **Propiedad derivada** (abstracta) del subtipo concreto — NO un campo serializado, para que no pueda desincronizarse de la clase real |
| `costs` | List\<ResourceCost\> | Costos (hoy 1 entrada; preparado para multi-recurso) |
| `sprite` | Sprite | Imagen de la carta (`Sprite` permitido en Core) |
| `descriptionText` | string | Texto visible en la carta |
| `playSoundId` | string | **Id** del sonido a reproducir al jugar (resuelto por `AudioManager` desde `Resources`, no un `AudioClip` en Core). Opcional. |
| `animationHook` | string | **[FUTURO]** nombre de animación a disparar al jugar |

**ResourceCost**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `resource` | ResourceType | Recurso requerido |
| `amount` | int | Cantidad requerida |

**UnitCardData : CardData**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `maxHp` | int | HP máximo de la unidad |
| `allowedSlots` | int[] | Slots donde puede desplegarse (1–6). **Vacío = cualquiera.** Permite unidades obligadas a ir a slots concretos (ej: `[6]` o `[1,2]`) |
| `attack` | UnitAttack | Patrón de ataque (§7.2) |
| `passiveEffects` | List\<PassiveEffect\> | Efectos pasivos (producción, etc.) (§7.4) |
| `unitSubtype` | UnitSubtype | Atacante / Defensiva / Productora — punto de extensión, no usado activamente |

> Los efectos visuales de impacto/muerte/ataque **no** viven en `UnitCardData`: son presentación (§7.10).

**ActionCardData : CardData**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effects` | List\<CardEffect\> | Efectos que resuelve al jugarse, en orden |

**EquipmentCardData : CardData**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `statModifiers` | List\<StatModifier\> | Modificadores al stat efectivo de la unidad portadora (+maxHp, +daño) |
| `grantedPassives` | List\<PassiveEffect\> | Pasivas que otorga mientras esté equipado |

**StatModifier**: `{ stat: StatType, value: int }`. **StatType** enum: `MaxHp`, `Damage` *(extensible)*. Se juega sobre una unidad propia y dura hasta que muere (§8.4).

---

### 7.2 UnitAttack

Modela la **acción** de una unidad. Por defecto pega daño a los enemigos; con `effect = HealAllies` la misma maquinaria de targeting cura a unidades aliadas (un **healer**, §9/§10). En ambos casos usa la acción de ataque del turno (no es gratis ni adicional).

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `reference` | AttackReference | `Absolute` (slots fijos 1–6 del tablero objetivo) / `Relative` (offsets desde el slot del atacante; `0` = enfrentado, o sí misma si el objetivo es aliado) |
| `pattern` | int[] | Slots/offsets candidatos |
| `pickCount` | int | `0` = afecta todos los de `pattern`; `N>0` = el atacante elige N de `pattern` |
| `damagePerSlot` | int | Magnitud por slot: **daño** si `effect=DamageEnemies`, **curación** si `effect=HealAllies` (tope = maxHp) |
| `effect` | AttackEffect | `DamageEnemies` (default) = afecta el tablero rival / `HealAllies` = afecta el tablero propio curando |

**AttackReference** enum: `Absolute`, `Relative`
**AttackEffect** enum: `DamageEnemies`, `HealAllies` *(extensible: p. ej. `BuffAllies` a futuro)*

El tablero objetivo lo decide `effect` (rival para daño, propio para cura). El **catálogo de zonas/patrones del §6 aplica igual a curaciones** (vanguardia, medio, mitades, etc.). Un slot objetivo vacío = whiff; curar a una unidad ya en maxHp se desperdicia.

**[FUTURO]** → `List<UnitAttack>` para múltiples poderes por unidad con distintos patrones.

---

### 7.3 PassiveEffect

Un pasivo puede **producir recursos, curar/buffear aliadas o dañar/debuffear enemigas**. Las pasivas que afectan a un conjunto de slots usan **el mismo targeting que un ataque** (§6): `reference` + `pattern` + `pickCount` sobre el tablero indicado por `target` (vanguardia, mitades, etc.).

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `passiveType` | PassiveType | Qué hace + cuándo (ver tabla) |
| `value` | int | Magnitud (daño, cura, recurso, +daño de aura) |
| `resource` | ResourceType | Recurso afectado (sólo `ProduceResource`) |
| `status` | StatusEffect | Plantilla a aplicar (sólo `TurnStatus`) |
| `target` | PassiveTarget | `Self` / `Allies` / `Enemies` — sobre qué tablero recae |
| `reference` | AttackReference | Targeting igual que `UnitAttack` (`Self` lo ignora) |
| `pattern` | int[] | idem |
| `pickCount` | int | idem (`0` = todos los del patrón) |

**PassiveType** enum:

| Tipo | Timing | Efecto |
|------|--------|--------|
| `ProduceResource` | Inicio de turno (PRODUCCIÓN) | +`value` de `resource` al dueño |
| `Regeneration` | Inicio de turno del dueño | Cura `value` HP (target `Self` por default; puede curar aliadas con patrón) |
| `AuraDamage` | Continuo (al resolver el ataque de la aliada) | +`value` daño a las aliadas del `target`/patrón (adyacentes = `Relative [-1,+1]`) |
| `Retaliate` | Reactivo, al ser golpeada por un **ataque de unidad** | El atacante recibe `value` de daño |
| `TurnDamage` | Inicio de turno del dueño | `value` daño a los slots objetivo (típico `Enemies`, patrón vanguardia/mitad) |
| `TurnStatus` | Inicio de turno del dueño | Aplica `status` a los slots objetivo (`Enemies` o `Allies`) |

**PassiveTarget** enum: `Self`, `Allies`, `Enemies`. *(Reemplaza al `PassiveScope` de la Fase 2: la adyacencia se expresa con `Relative [-1,+1]` sobre `Allies`.)*

Reglas: las auras **se suman** y no se aplican a sí mismas. `Retaliate` sólo responde a ataques de unidad (no a daño directo de cartas, pasivas ni muerte súbita) y **dispara aunque la unidad muera** (re-evaluar KO). `TurnDamage`/`TurnStatus` se resuelven al inicio del turno del dueño y respetan whiff en slots vacíos. El daño/estado de pasivas **no** dispara `Retaliate` (no es ataque de unidad).

---

### 7.4 UnitSlot (unidad desplegada en el tablero)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `unitData` | UnitCardData | Referencia (inmutable) al template de la carta |
| `currentHp` | int | HP actual |
| `count` | int | **[FUTURO]** Punto de extensión para apilamiento. Default 1; hoy sin mecánica activa (una unidad por slot). La capa de stats efectivos puede multiplicar por `count` cuando se active. |
| `attachedEquipment` | List\<EquipmentCardData\> | Equipo adjunto mientras la unidad esté viva (§8.4). Suma a los stats efectivos y otorga sus `grantedPassives` |

> La **posición** del slot es implícita: es el índice en `PlayerState.unitSlots` (no se almacena por separado).
>
> Los **stats efectivos** (maxHp, daño) se calculan a partir de `unitData` (base, inmutable) + `attachedEquipment`. Solo `currentHp` es estado mutable almacenado; el resto se deriva para que no haya valores duplicados que puedan desincronizarse.

---

### 7.5 PlayerState

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `dinero` | int | Dinero actual |
| `fuerza` | int | Fuerza actual |
| `social` | int | Social actual |
| `hand` | List\<CardData\> | Cartas en mano |
| `unitSlots` | UnitSlot[6] | Slots de unidades, indexados por posición. `null` = slot vacío |
| `activeStatuses` | List\<StatusEffect\> | Buffs/debuffs temporizados activos |
| `faction` | Faction | Facción del jugador |

---

### 7.6 CardEffect (efecto inmediato de carta de Acción)

| Campo | Tipo | Aplica cuando `effectType` es… | Descripción |
|-------|------|--------------------------------|-------------|
| `effectType` | CardEffectType | (siempre) | Qué hace |
| `target` | TargetType | (siempre) | Self / Opponent — sobre qué jugador recae |
| `targetSlot` | int | `ModifyHP`, `RemoveUnit`, `ApplyStatus` (a unidad), `MoveUnit`, `SwapUnits` | Slot afectado / origen. `-1` = el jugador elige al jugar |
| `targetSlotB` | int | `MoveUnit` (destino), `SwapUnits` (segundo slot) | Slot secundario. `-1` = el jugador elige |
| `resourceTarget` | ResourceType | `ModifyResource` | Recurso afectado |
| `value` | int | `ModifyHP`, `ModifyResource` | Magnitud (signo: + cura/gana, − daña/quita) |
| `status` | StatusEffect | `ApplyStatus` | Plantilla del status (a jugador o a unidad según el `statusType`) |

> `ModifyHP` opera sobre la unidad en `targetSlot` del jugador `target`. Es daño/cura **directo**: no lo mitiga nada y **no** dispara `Retaliate` (no es ataque de unidad). `ApplyStatus` aplica el status a un jugador (estados de producción) o a una unidad en `targetSlot` (estados por unidad, §7.7) según su tipo. `MoveUnit` mueve la unidad de `targetSlot` al slot libre `targetSlotB` (respetando `allowedSlots`); `SwapUnits` intercambia las de `targetSlot` y `targetSlotB`.

**CardEffectType:** `ModifyHP`, `ModifyResource`, `RemoveUnit`, `ApplyStatus`, `MoveUnit`, `SwapUnits`
**TargetType:** `Self` / `Opponent`
**ResourceType:** `Dinero` / `Fuerza` / `Social`

> Convención de extensión (ver `CLAUDE.md`): agregar un tipo de efecto = un valor nuevo en `CardEffectType` + su resolución en el `GameEngine` (un solo lugar). `RemoveUnit` (destrucción directa) sigue como punto de extensión sin uso: las cartas de quitar HP usan `ModifyHP`.

---

### 7.7 StatusEffect (buff/debuff temporizado)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `statusType` | StatusType | Qué hace (ver tabla) |
| `value` | int | Magnitud (daño de veneno, ± daño de furia/desmoralización, multiplicador de producción) |
| `counter` | int | **Turnos de duración.** Se decrementa en EFECTOS al inicio del turno del afectado; al llegar a 0 el status se elimina |

Vive en `activeStatuses` de un **jugador** (estados de producción) o de una **unidad** (`UnitSlot.activeStatuses`, estados por unidad). El alcance lo implica el `statusType`.

**StatusType** enum:

| Tipo | Alcance | Efecto |
|------|---------|--------|
| `SkipProduction` | Jugador | No produce su próxima producción |
| `DoubleProduction` | Jugador | Dobla su próxima producción |
| `Poison` (Veneno) | Unidad | `value` daño al inicio del turno del dueño, mientras dure |
| `Stun` (Aturdir) | Unidad | La unidad no puede usar su acción mientras dure |
| `Furia` | Unidad | +`value` daño mientras dure |
| `Desmoralizar` | Unidad | −`value` daño mientras dure |

> Estados de **producción** (`SkipProduction`/`DoubleProduction`): modifican una sola producción, `counter` arranca en 1 (la "próxima"). Estados **por unidad**: `Poison` daña cada turno; `Furia`/`Desmoralizar`/`Stun` están activos mientras `counter > 0` (se consultan al calcular el daño efectivo / al intentar atacar). El daño de `Poison` y de muerte súbita es directo (no dispara `Retaliate`).

---

### 7.8 GameEngine — responsabilidades

Único responsable de resolver `CardEffect`, procesar `activeStatuses`, resolver ataques de unidades, aplicar muerte súbita y manejar transiciones de turno. No usa RNG global: recibe una abstracción de RNG inyectable (coinflip, robo) para que los tests sean deterministas.

**[FUTURO]** Punto de extensión `IPlayerController` para soportar IA y multijugador online sin modificar `GameEngine`.

**[FUTURO]** Punto de extensión para deckbuilding (pool de cartas configurable por jugador).

---

### 7.9 Aleatoriedad

El dominio no usa `UnityEngine.Random` ni `System.Random` directo. Recibe una abstracción de RNG (`IRng` o equivalente) para que coinflip y robo sean deterministas y reproducibles en tests.

---

### 7.10 Presentación (fuera de Core)

Todo lo visual/audio vive en `PiqueteDefend.Presentation`:

- **Feedback de combate:** el shake/flash de una unidad al recibir daño, morir o atacar es responsabilidad de la capa visual (reacciona a eventos del `GameEngine`). El dominio no conoce `Shake`/`Flash`/`AnimationClip`.
- **Audio:** `AudioManager` resuelve `playSoundId` (y la música) cargando clips desde `Resources/Audio/` por nombre.

**[DEFINIR]** si el feedback visual por unidad necesita configuración por carta (p. ej. un `CardPresentation` SO en Presentation, indexado por `id`) o alcanza con convenciones globales (shake al recibir daño, etc.).

---

## 8. Sistema de cartas

### 8.1 Pool y mano

- No existe mazo ni descarte persistente.
- Cada jugador tiene un pool de cartas de su facción (Unidades, Acciones, Equipo).
- Al inicio: 6 cartas aleatorias en mano.
- Al jugar o descartar una carta, se reemplaza por una aleatoria del pool.
- **[FUTURO]** todas las cartas podrían cambiar entre turnos.
- El pool puede repetir cartas.
- **Frecuencia de robo:** cada carta tiene un `drawWeight` (int, default 1) en `CardData`; el robo es proporcional al peso (equivale a tener N copias, pero se tunea con un solo número y es trivial de simular). **Hoy todas las cartas tienen peso 1 (robo uniforme)**; los pesos por rareza se ajustarán con el simulador (Fase 5).

### 8.2 Acción (un solo uso)

Se juega, se resuelven todos sus `CardEffect` en orden, y se reemplaza por una carta nueva.

### 8.3 Unidad (persistente)

Se despliega en un slot **permitido** (`allowedSlots`; vacío = cualquiera) que esté libre. Si no hay ningún slot permitido libre, el jugador elige reemplazar una unidad existente en un slot permitido (la nueva entra con su HP máximo) o cancelar sin gastar recursos.

No hay apilamiento activo: un slot contiene una unidad. **[FUTURO]** El modelo reserva un punto de extensión para apilamiento (`UnitSlot.count`, §7.4), hoy inactivo.

### 8.4 Equipo

Una carta de Equipo (`EquipmentCardData`, §7.1) se juega **sobre una unidad propia** (se arrastra sobre el slot, §11.4): le suma `statModifiers` (+maxHp, +daño) y/o `grantedPassives`. El efecto dura **hasta que la unidad muere** (o es reemplazada): ahí el equipo se destruye con ella (no vuelve a la mano ni al pool). Una unidad puede acumular varios equipos (`UnitSlot.attachedEquipment`); los modificadores se suman en la **capa de stats efectivos** (§7.4). **[DEFINIR]** tope de equipos por unidad (hoy sin límite). Catálogo concreto en §9/§10.

---

## 9. Cartas — Manifestantes

> **Unidades diferenciadas por arquetipo** (geometría de combate y zonas de deploy en §6). Los **costos y pasivas son definitivos**; **HP, daño y posición son provisionales y sin validar** — se balancean por simulación (herramienta de Python, Fase 5). Notación: `Rel` = ataque `Relative` (offsets desde el slot del atacante; `+` = hacia el frente enemigo); `Abs` = `Absolute` (slots fijos del oponente, 1–6); `pick 0` = afecta todos los del patrón, `pick N` = el atacante elige N; `cura X` = la acción cura X HP a aliadas (`effect=HealAllies`, mismo catálogo de patrones del §6) en vez de dañar. Pasivas: §7.3.

### Unidades
| Carta | Costo | Arquetipo | maxHp | Deploy | Ataque · daño | Pasiva | Descripción |
|-------|-------|-----------|-------|--------|---------------|--------|-------------|
| **Piquetero** | 4 ⚡ | Escaramuza | 24 | Cualquiera | `Rel [-1,0,+1]` pick 1 · 9 | Aura +1 daño (adyac.) | *Bombo, bandera y aguante para parar todo. El GPS del camionero lo putea de memoria.* |
| **Jubilado** | 5 $ | Muro | 38 | Frente {4,5,6} | `Abs {4,5,6}` pick 0 · 3 | Espinas 2 | *83 pirulos, bastón y primera fila. La cana le tiene cagazo a lo que largue en la tele.* |
| **Gordo Sindical** | 3 $ | Productora | 14 | Retaguardia {1,2,3} | `Rel [0]` pick 0 · 2 | +1 $/turno | *El que arregla la paritaria y maneja la caja. Aparece en el palco, jamás en la primera fila.* |
| **Fisura** | 5 ⚡ | Cleave | 22 | {2,3,4,5} | `Rel [-1,0,+1]` pick 0 · 3 | +1 ⚡/turno | *Arranca la baldosa de la plaza con las manos y la parte en cuatro. Cada cascote tiene destinatario.* |
| **Tuitero Militante** | 2 📣 | Productora | 12 | Retaguardia {1,2,3} | `Rel [0]` pick 0 · 1 | +1 📣/turno | *2.300 seguidores y la certeza de que cambió la historia con un hilo.* |
| **Choripanero** | 4 📣 | Healer | 18 | {2,3,4,5} | `Abs {4,5,6}` pick 1 · cura 6 | — | *Pan, chori y chimi para aguantar la jornada. El que morfa, vuelve a la marcha.* |
| **Mortero Casero** | 5 ⚡ | Sniper | 14 | {2,3,4} | `Abs {1,2,3}` pick 1 · 9 | — | *Un caño, pólvora trucha y puntería de chiripa. Igual le encaja justo en la oficina del fondo.* |
| **Quema de Cubiertas** | 5 📣 | Emisor | 16 | {2,3,4,5} | `Rel [0]` pick 0 · 1 | Humo: 1 daño/turno a vanguardia enemiga | *Diez gomas viejas y el viento a favor. El humo negro no le hace asco a nadie.* |

### Acciones
| Carta | Categoría | Costo | Efecto | Descripción |
|-------|-----------|-------|--------|-------------|
| **Colecta** | Boost | 3 📣 | +6 $ propio | *Pasamos la gorra. La de los compañeros, no la de la cana.* |
| **Fernet con Cola** | Boost | 1 $ | +3 ⚡ propio | *Hidratación táctica. No es doping si lo toma toda la marcha.* |
| **Viral en Redes** | Boost | 2 $ | +7 📣 propio | *Un video de 14 segundos, tres palos de reproducciones. El ministerio ya está llamando.* |
| **Saqueo** | Sabotaje | 1 ⚡ | Oponente −3 $ | *No es afano. Es redistribución urgente de mercadería.* |
| **Paro General** | Ataque | 5 ⚡ | 14 daño directo a una unidad enemiga | *24 horas de nada. No hay bondi, no hay banco, no hay delivery. El país clavado.* |
| **Abrazo Colectivo** | Defensa | 5 $ | +16 HP a una unidad propia | *El abrazo que cura todo. Menos la deuda en pesos.* |
| **Asamblea Popular** | Especial | 6 📣 | Doble producción propia el próximo turno | *Se vota a mano alzada. Cuatro horas de bardo, pero esta vez salió.* |
| **Escrache** | Sabotaje | 4 📣 | Aturde 1 turno a una unidad enemiga | *Le golpean la puerta a las 7 de la mañana con bombos. No se asoma en todo el día.* |
| **El Aguante** | Boost | 2 ⚡ | Furia (+3 daño, 2 turnos) a una unidad propia | *Cantito, bombo y se renueva el aguante. Treinta cuadras más, fácil.* |
| **Cambio de Consigna** | Especial | 1 📣 | Mueve una unidad propia a un slot libre permitido | *La columna pega la vuelta en U. Nadie cazó la orden, pero todos giraron.* |

### Equipamiento
> Se juega sobre una unidad propia; dura hasta que la unidad muere (§8.4).

| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Pechera de Cartón** | 2 $ | +12 maxHp | *Cartón, cinta de embalar y fe. Aguanta más de lo que el sentido común permite.* |
| **Cascote** | 2 ⚡ | +3 daño | *El fierro más democrático: gratis, abundante y siempre a mano.* |
| **Parrilla Portátil** | 3 $ | Otorga Regeneración (+3 HP/turno) | *Media parrilla, una bolsa de carbón y olor a asado. Cura lo que ninguna obra social.* |
| **Miguelitos** | 2 ⚡ | Otorga Espinas 3 (Retaliate) | *Tres clavos soldados con saña. El patrullero los encuentra tarde, siempre.* |

---

## 10. Cartas — Policías

> **Unidades diferenciadas por arquetipo** (geometría de combate y zonas de deploy en §6). Los **costos y pasivas son definitivos**; **HP, daño y posición son provisionales y sin validar** — se balancean por simulación (herramienta de Python, Fase 5). Notación: `Rel` = ataque `Relative` (offsets desde el slot del atacante; `+` = hacia el frente enemigo); `Abs` = `Absolute` (slots fijos del oponente, 1–6); `pick 0` = afecta todos los del patrón, `pick N` = el atacante elige N; `cura X` = la acción cura X HP a aliadas (`effect=HealAllies`, mismo catálogo de patrones del §6) en vez de dañar. Pasivas: §7.3.

### Unidades
| Carta | Costo | Arquetipo | maxHp | Deploy | Ataque · daño | Pasiva | Descripción |
|-------|-------|-----------|-------|--------|---------------|--------|-------------|
| **Infante** | 6 ⚡ | Escaramuza | 26 | Cualquiera | `Rel [-1,0,+1]` pick 1 · 10 | Aura +1 daño (adyac.) | *Escudo, casco y 14 horas de turno. Va al frente porque le pagan para eso.* |
| **Gendarme** | 3 $ | Muro | 46 | Frente {4,5,6} | `Abs {4,5,6}` pick 0 · 2 | Espinas 2 | *Lo trajeron de la frontera a cuidar una esquina. No se mueve, no se cansa, no entiende el reclamo.* |
| **Puntero** | 5 $ | Productora | 14 | Retaguardia {1,2,3} | `Rel [0]` pick 0 · 2 | +1 $/turno | *Reparte bolsones y promesas. La guita sale de algún lado, siempre.* |
| **Itakero** | 3 ⚡ | Cleave | 22 | {2,3,4,5} | `Rel [-1,0,+1]` pick 0 · 3 | +1 ⚡/turno | *Escopeta Itaka y postas de goma. Apunta al montón, total alguno cae.* |
| **Trol Oficial** | 5 📣 | Productora | 16 | Retaguardia {1,2,3} | `Rel [0]` pick 0 · 1 | +1 📣/turno | *Diez cuentas, un solo sueldo del Estado. Inventa la tendencia antes del mediodía.* |
| **Médico del SAME** | 4 $ | Healer | 18 | {2,3,4,5} | `Rel [-1,0,+1]` pick 0 · cura 3 | — | *Llega en ambulancia y atiende a todos. Después hace tres guardias para llegar a fin de mes.* |
| **Halcón** | 6 ⚡ | Sniper | 14 | {2,3,4} | `Abs {1,2,3}` pick 1 · 10 | — | *Grupo especial, mira telescópica y paciencia de cazador. Desde la terraza ve toda la plaza.* |
| **Gasero** | 5 📣 | Emisor | 18 | {2,3,4,5} | `Rel [0]` pick 0 · 1 | Gas: Veneno (2) a 1 de vanguardia enemiga/turno | *Granada en mano, pañuelo en la cara. "Es para dispersar", dice, mientras llora hasta él.* |

### Acciones
| Carta | Categoría | Costo | Efecto | Descripción |
|-------|-----------|-------|--------|-------------|
| **Partida Presupuestaria** | Boost | 1 📣 | +7 $ propio | *Existe en el papel. Se aprobó a las 3 de la mañana y nadie sabe para qué.* |
| **Licitación Express** | Boost | 3 $ | +8 ⚡ propio | *Una empresa, un sobre y 48 horas. El pliego lo hicieron el lunes a la tarde.* |
| **Cadena Nacional** | Boost | 2 $ | +4 📣 propio | *Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que arranque.* |
| **Embargo** | Sabotaje | 3 ⚡ | Oponente −7 $ | *El juez firmó, la guita voló. El otro ya lo veía venir.* |
| **Operativo Apretón** | Ataque | 6 $ | 18 daño directo a una unidad enemiga | *Cuatro camiones, veinte efectivos y un drone. Todo para un jubilado con un cartel.* |
| **Refuerzos** | Defensa | 5 📣 | +12 HP a una unidad propia | *Llegan dos camiones más. La línea se rearma como si nada.* |
| **Toque de Queda** | Especial | 5 $ | El oponente no produce el próximo turno | *A las 22 todos adentro. El que se manda afuera, va en cana.* |
| **Causa Judicial** | Sabotaje | 4 $ | Veneno (2 daño/turno, 2 turnos) a una unidad enemiga | *Te arman un expediente. Te va comiendo de a poco, durante años.* |
| **Apriete** | Sabotaje | 2 ⚡ | Desmoraliza (−3 daño, 2 turnos) a una unidad enemiga | *Una charla en voz baja contra la pared. Se te van las ganas solas.* |
| **Reubicación Forzosa** | Especial | 2 $ | Intercambia dos unidades enemigas de slot | *Los suben a un patrullero, los bajan en la otra punta. Protocolo, dicen.* |

### Equipamiento
> Se juega sobre una unidad propia; dura hasta que la unidad muere (§8.4).

| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Chaleco Antibalas** | 2 $ | +14 maxHp | *Importado. Al menos figura en el inventario, que ya es algo.* |
| **Tonfa** | 2 ⚡ | +3 daño | *Reglamentaria. El uso, a criterio del que la empuña.* |
| **Obra Social** | 3 $ | Otorga Regeneración (+3 HP/turno) | *Cobertura del 100%. Después de tres formularios y una mañana de cola.* |
| **Reflectores** | 2 📣 | Otorga Aura +1 daño a aliadas adyacentes | *Iluminan todo de golpe. De repente la patota se coordina sola.* |

---

## 11. UI / Pantalla

### 11.1 Menú principal

Pantalla de inicio con un botón central **Jugar**. Espacio reservado para futuras opciones (Ajustes, Créditos, etc.).

### 11.2 Selección de facción

Por ahora los **lados son fijos**: **Manifestantes** siempre a la izquierda, **Policías** siempre a la derecha. La pantalla es una **sola elección** que define **qué facción juega primero**: la facción elegida arranca y la otra es el rival.

> [FUTURO] Permitir que cada jugador elija su facción (incluido mirror, ambos lados con la misma facción). Hoy el modelo asume un Manifestantes vs un Policías con lados fijos.

### 11.3 Pantalla de juego

Pantalla única, **lados fijos**: Manifestantes a la izquierda, Policías a la derecha. 6 slots de unidades por jugador siempre visibles, pegados al borde externo de cada lado. La mano y las zonas de acción pertenecen solo al jugador activo.

Los slots van del **1 al 6**; las **unidades iniciales ("las del fondo") ocupan el 1 y el 2**, en los lugares **más externos** de cada jugador: el de la izquierda, los dos de más a la izquierda; el de la derecha, los dos de más a la derecha (la fila derecha se dibuja invertida).

```
┌────────────────────────────────────────────────────────────────────────────┐
│ MANIFESTANTES            [ Terminar turno ]  (▶ chip)            POLICÍAS      │
│ $:9 ⚡:6 📣:14                                            $:12 ⚡:8 📣:5         │
│                                                                                │
│                       (fondo / escena de la marcha)                            │
│                    ┌─────────────┐                                             │
│                    │ ⚔ Atacar·5  │ ← popover sobre la unidad clickeada         │
│                    └──────┬──────┘                                             │
│                     ╔═════════╗  ╔════════════╗                                │
│ [1][2][3][4][5][6]  ║  JUGAR  ║  ║ DESCARTAR  ║           [6][5][4][3][2][1]   │
│  slots Manif.       ╚═════════╝  ╚════════════╝            slots Policías      │
│  (1·2 al borde izq) [c1][c2][c3][c4][c5][c6]             (1·2 al borde der)    │
│                      ▲ mano del jugador activo                                 │
└────────────────────────────────────────────────────────────────────────────┘
```

**Jugar / descartar carta (drag & drop):** se arrastra la carta de la mano y se suelta sobre la zona **JUGAR** o **DESCARTAR**. No hay botones de clic para esto; las dos zonas son *drop targets*. Mientras se arrastra, una **copia de la carta ("ghost")** acompaña el puntero.

**Atacar con una unidad:** **click** sobre una unidad propia → aparece un **popover** sobre ella con su acción disponible; al clickearlo, actúa. Si es a elección (`pickCount > 0`), a continuación se clickea el/los slot(s) objetivo (en el tablero rival si daña, **en el propio si es un healer** que cura aliadas, §7.2). **[FUTURO]** si una unidad llega a tener varios ataques (`List<UnitAttack>`), el popover los lista.

**Equipar:** se arrastra la carta de Equipo sobre una **unidad propia** (el slot es el *drop target*, §8.4).

**Efectos activos:** indicador por jugador para sus `activeStatuses` (producción) **y por unidad** para sus estados (Veneno/Aturdir/Furia/Desmoralizar) y el equipo adjunto. **[DEFINIR]** iconografía concreta por estado/equipo.

**Indicador de turno:** un **chip** (pill) que salta al lado del jugador activo, más la marca **▶** en su panel de stats. El botón **Terminar turno** está centrado en el tope.

### 11.4 Input

| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta | Arrastrar la carta sobre **JUGAR** (drag & drop) | Seleccionar (1–6) + Enter |
| Descartar carta | Arrastrar la carta sobre **DESCARTAR** (drag & drop) | Seleccionar (1–6) + Backspace |
| Atacar / curar con una unidad | Click en la unidad → click en el popover de acción | — |
| Elegir slot objetivo (ataque/cura a elección, sabotaje, equipo) | Click en el slot | — |
| Mover / intercambiar unidad (MoveUnit / SwapUnits) | Click en el slot origen → click en el destino | — |

### 11.5 Anatomía de una carta

Cada carta muestra:
- **Imagen**
- **Nombre**
- **Costo** — ícono del recurso + cantidad
- **Efecto** — texto corto

### 11.6 Pantalla de victoria

Overlay con:
- Mensaje de victoria (facción ganadora + condición)
- Botón **Revancha** — reinicia con las mismas facciones
- Botón **Menú principal**

---

## 12. Parámetros configurables

| Parámetro | Valor |
|-----------|-------|
| Cartas visibles en mano | 6 |
| Slots de unidades por jugador | 6 |
| Apilamiento de unidades | No activo (punto de extensión [FUTURO], `UnitSlot.count`) |
| Unidades iniciales por facción | Predefinidas (data por facción) |
| Recursos iniciales | 5 de cada uno (configurable) |
| Producción base por turno | +1 de cada recurso ($/⚡/📣); en `GameConfig` (configurable) |
| Primer turno sin producción | Sí |
| Lados de facción | Fijos (por ahora): Manifestantes izquierda, Policías derecha |
| Primer jugador | Lo elige la selección de facción (la que arranca); coinflip si no se especifica |
| `suddenDeathStart` | Turno 50 (backstop, configurable; bien por encima de la duración ideal) |
| `maxTurns` | 120 (backstop duro, configurable) |
| Duración ideal de partida | ~30–40 medios-turnos (≈15–20 por jugador); a afinar por simulación |
| Cartas por facción | 22 (8 unidades + 10 acciones + 4 equipo) |
| Peso de robo (`drawWeight`) | 1 para todas (robo uniforme; rareza a futuro) |

---

## 13. Pendientes [DEFINIR]

- **Balance de unidades:** unidades ya diferenciadas por arquetipo (§9/§10); los valores de HP/daño son **provisionales y sin validar**, a balancear por simulación (Fase 5).
- **Apilamiento:** punto de extensión reservado (`UnitSlot.count`), inactivo en v1.
- **Unidades iniciales por facción:** definidas — Manifestantes = Piquetero + Gordo Sindical; Policías = Infante + Puntero (1 peleador + 1 productora), en slots 1–2. El peleador inicial usa deploy **Cualquiera** para poder ocupar la retaguardia inicial.
- **Feedback visual por unidad:** si requiere config por carta en Presentation o alcanzan convenciones globales (§7.10). Incluye **iconografía de estados por unidad** (Veneno/Aturdir/Furia/Desmoralizar) y de **equipo adjunto** (§11.3/§11.4).
- **Tope de equipos por unidad:** hoy sin límite (§8.4); definir si conviene un máximo.
- **EquipmentCardData:** diseñado e incluido en el catálogo (§7.1 / §8.4 / §9/§10, 4 cartas/facción); falta implementar (capa de stats efectivos, §15 Fase 4).
- **IPlayerController:** diseño del punto de extensión para IA / online [FUTURO].
- **Deckbuilding:** diseño del punto de extensión [FUTURO].

---

## 14. Fuera de scope (v1)

- Construcción de mazos personalizada
- Más de 2 jugadores
- Online / multijugador en red
- Animaciones elaboradas (hoy: shake y flash)
- Progresión / desbloqueo de cartas
- Más de 2 facciones

---

## 15. Extensiones de modelo [IMPLEMENTAR]

Checklist para la sesión de implementación. **El spec es la fuente de verdad: `CardLibrary.cs` y los assets deben quedar alineados con él.**

### Fase 1 — Diferenciación de unidades y posición
**Sin cambios de modelo.** `allowedSlots`, `UnitAttack` (`Absolute`/`Relative`, `pattern`, `pickCount`, `damagePerSlot`) y `maxHp` ya soportan todas las zonas y patrones del §6.
- [ ] Re-statear `CardLibrary.cs`: reemplazar la baseline uniforme por los valores de §9/§10 (maxHp, `allowedSlots`, `attack` por unidad). Quitar `BaselineHp`, `BaselineDamage` y `BaselineAttack()`.
- [ ] Regenerar los assets de unidad (ScriptableObjects) desde la librería.
- [ ] Verificar la base de índices: `allowedSlots` y los `Absolute pattern` en base 0 (slot spec `k` → índice `k-1`); los offsets `Relative` no cambian.
- [ ] (Opcional) helpers/constantes de presets de zonas y patrones (§6).
- [ ] `GameConfig`: producción base = **1 de cada recurso** (`baseProdDinero`/`baseProdFuerza`/`baseProdSocial` = 1).

### Fase 2 — Pasivas variadas y curación
- [ ] `PassiveType` += `Regeneration`, `AuraDamage`, `Retaliate` (§7.3).
- [ ] `PassiveEffect.scope` nuevo + enum `PassiveScope { Self, Adjacent, AllAllies }` (default `Self`).
- [ ] `UnitAttack.effect` nuevo + enum `AttackEffect { DamageEnemies, HealAllies }` (default `DamageEnemies`). El targeting (`reference`/`pattern`/`pickCount`) resuelve sobre el tablero **propio** cuando cura; `damagePerSlot` = magnitud (renombrable a `amountPerSlot`).
- [ ] Hooks en `GameEngine`:
  - `Regeneration`: curar al inicio del turno del dueño (tope maxHp).
  - `AuraDamage`: sumar al **daño efectivo** del aliado en `scope` al resolver su ataque (capa de stats efectivos; se comparte con el equipo de Fase 4).
  - `Retaliate`: en la resolución de ataque, cada defensora golpeada devuelve `value` al atacante; re-evaluar KO. Dispara aún si la defensora muere.
  - `HealAllies`: resolver como un ataque pero **sumando** HP a aliadas (cap maxHp); whiff en slot vacío o unidad llena.
- [ ] Cartas nuevas en `CardLibrary` + assets: **Choripanero** (Manif), **Médico del SAME** (Pol). Pasivas nuevas en Escaramuza (Aura) y Muro (Retaliate).

### Fase 3 — Acciones, estados por unidad y pasivas dirigidas
- [ ] `UnitSlot.activeStatuses` (lista de `StatusEffect` por unidad).
- [ ] `StatusType` += `Poison`, `Stun`, `Furia`, `Desmoralizar` (§7.7). Poison = daño/turno; Furia/Desmoralizar/Stun = activos mientras `counter>0`.
- [ ] `CardEffectType` += `MoveUnit`, `SwapUnits`; `CardEffect.targetSlotB` nuevo; `ApplyStatus` extendido a unidades (vía `targetSlot`).
- [ ] `PassiveType` += `TurnDamage`, `TurnStatus`; `PassiveEffect` gana targeting (`target` PassiveTarget + `reference`/`pattern`/`pickCount`); **reemplazar `PassiveScope` por `PassiveTarget { Self, Allies, Enemies }`**.
- [ ] Hooks de motor: estados por unidad en EFECTOS (decrementar counter; Poison daña); consultar Furia/Desmoralizar en el daño efectivo y Stun al intentar atacar; resolver TurnDamage/TurnStatus al inicio del turno; MoveUnit/SwapUnits.
- [ ] `CardData.drawWeight` (int, default 1) + robo proporcional (hoy uniforme).
- [ ] Rework de acciones en `CardLibrary` (10/facción: 3 boost + 7 variadas, §9/§10) + 2 unidades nuevas/facción (Sniper, Emisor).

### Fase 4 — Equipamiento
- [ ] `CardType` += `Equipo`; `EquipmentCardData { statModifiers, grantedPassives }` (§7.1); enum `StatType { MaxHp, Damage }` + `StatModifier`.
- [ ] **Capa de stats efectivos** en `UnitSlot`: maxHp y daño efectivos = base + Σ equipo; fusionar `grantedPassives` con las propias. Se comparte con `AuraDamage`/`Furia`.
- [ ] Equipo se juega sobre unidad propia (§11.4, targeting de slot); se destruye al morir/reemplazar la unidad.
- [ ] 4 cartas de equipo/facción en `CardLibrary` + assets (§9/§10).
