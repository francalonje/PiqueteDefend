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
| **Dinero** ($) | Plata para movilizar gente y pagar logística | 0 / turno | 100 |
| **Fuerza** (⚡) | Capacidad física, represión o resistencia | 0 / turno | 100 |
| **Social** (📣) | Apoyo popular, organización, narrativa | 0 / turno | 100 |

> La producción base es 0. Los recursos solo se generan mediante unidades con efecto pasivo de producción o cartas de Acción. Los valores de producción base son configurables desde código en un único lugar para facilitar el balanceo.

**Recursos iniciales al inicio de la partida:** 5 de cada recurso (configurable para balanceo).

**Primer turno:** los jugadores NO reciben producción. La producción comienza a partir del turno 2.

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
- A partir del turno `suddenDeathStart` (default: **30**), al final de cada turno todas las unidades de ambos jugadores reciben **1 punto de daño** (ignora defensas, es inevitable).
- Garantiza que las unidades mueran eventualmente si ningún jugador es eliminado antes.

**Límite de turnos (backstop duro):**
- `maxTurns` (default: **100**). Si se alcanza sin ganador, la partida termina por desempate determinista:
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
1. EFECTOS      — Procesar activeStatuses del jugador activo:
                    Por cada status:
                      → counter--
                      → si counter == 0: activar su efecto (sobre este turno) y eliminar el status

2. PRODUCCIÓN   — Si NO es el turno 1 de la partida Y skipProduction no está activo:
                    a) Producción de unidades con efecto pasivo de producción
                    b) Multiplicar por productionMultiplier (default 1; doble si hay DoubleProduction)
                  → Evaluar condición de victoria

3. ACCIÓN       — El jugador puede hacer ambas, una, o ninguna (en cualquier orden):
                    a) Jugar 1 carta (si tiene suficiente recurso) O descartar 1 carta
                    b) Atacar con 1 unidad propia → afecta slots del oponente según
                       la definición de ataque de esa unidad
                       [FUTURO] buffs/debuffs/pasivas podrían permitir atacar con
                       más de una unidad por turno o que el ataque tenga costo de recurso
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
| **[FUTURO]** | — | No hay cartas de Equipo en el catálogo actual (§8.4). La estructura se define cuando se agregue la primera. |

---

### 7.2 UnitAttack

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `reference` | AttackReference | `Absolute` (slots del oponente 1–6) / `Relative` (offsets desde el slot del atacante; `0` = enfrentado) |
| `pattern` | int[] | Slots/offsets candidatos del ataque |
| `pickCount` | int | `0` = golpea todos los de `pattern`; `N>0` = el atacante elige N de `pattern` |
| `damagePerSlot` | int | Daño aplicado a cada slot golpeado |

**AttackReference** enum: `Absolute`, `Relative`

**[FUTURO]** → `List<UnitAttack>` para múltiples poderes por unidad con distintos patrones.

---

### 7.3 PassiveEffect

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `passiveType` | PassiveType | Qué hace el pasivo |
| `resource` | ResourceType | Recurso afectado (para `ProduceResource`) |
| `value` | int | Magnitud (ej: `+1`/turno) |

**PassiveType** enum: `ProduceResource` (**[FUTURO]** otros tipos: boost a unidades vecinas, etc.)

Los `PassiveEffect` de las unidades en juego se resuelven en la fase **PRODUCCIÓN** (§6).

---

### 7.4 UnitSlot (unidad desplegada en el tablero)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `unitData` | UnitCardData | Referencia (inmutable) al template de la carta |
| `currentHp` | int | HP actual |
| `count` | int | **[FUTURO]** Punto de extensión para apilamiento. Default 1; hoy sin mecánica activa (una unidad por slot). La capa de stats efectivos puede multiplicar por `count` cuando se active. |
| `attachedEquipment` | List\<EquipmentCardData\> | Equipo adjunto mientras la unidad esté viva (**[FUTURO]**, §8.4) |

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
| `targetSlot` | int | `ModifyHP`, `RemoveUnit` | Slot de unidad afectado. `-1` = el jugador elige al momento de jugar |
| `resourceTarget` | ResourceType | `ModifyResource` | Recurso afectado |
| `value` | int | `ModifyHP`, `ModifyResource` | Magnitud (signo: + cura/gana, − daña/quita) |
| `status` | StatusEffect | `ApplyStatus` | Plantilla del status a insertar |

> `ModifyHP` opera sobre la unidad en `targetSlot` del jugador `target` (el jugador no tiene HP propio). Es daño/cura directo: no lo mitigan defensas.

**CardEffectType:** `ModifyHP`, `ModifyResource`, `RemoveUnit`, `ApplyStatus`
**TargetType:** `Self` / `Opponent`
**ResourceType:** `Dinero` / `Fuerza` / `Social`

> Convención de extensión (ver `CLAUDE.md`): agregar un tipo de efecto = un valor nuevo en `CardEffectType` + su resolución en el `GameEngine` (un solo lugar). `RemoveUnit` está disponible como punto de extensión; hoy ninguna carta lo usa.

---

### 7.7 StatusEffect (buff/debuff temporizado)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `statusType` | StatusType | SkipProduction / DoubleProduction |
| `value` | int | Magnitud (reservado; los status actuales no lo usan) |
| `counter` | int | **Turnos hasta activarse.** Se decrementa en la fase EFECTOS al inicio del turno del afectado; cuando llega a 0, el status se activa (modifica la PRODUCCIÓN de ESE turno) y se elimina. Para efectos de "próximo turno", `counter` arranca en 1. |

**StatusType:** `SkipProduction`, `DoubleProduction`

> Los dos status actuales son modificadores de una sola producción ("la próxima"). El campo `counter` está pensado para soportar también efectos retardados de varios turnos sin cambiar el modelo.

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

### 8.2 Acción (un solo uso)

Se juega, se resuelven todos sus `CardEffect` en orden, y se reemplaza por una carta nueva.

### 8.3 Unidad (persistente)

Se despliega en un slot **permitido** (`allowedSlots`; vacío = cualquiera) que esté libre. Si no hay ningún slot permitido libre, el jugador elige reemplazar una unidad existente en un slot permitido (la nueva entra con su HP máximo) o cancelar sin gastar recursos.

No hay apilamiento activo: un slot contiene una unidad. **[FUTURO]** El modelo reserva un punto de extensión para apilamiento (`UnitSlot.count`, §7.4), hoy inactivo.

### 8.4 Equipo

**[FUTURO]** — No hay cartas de Equipo en el catálogo actual. Cuando se agregue la primera, definir: qué atributos modifica, si es permanente o temporal, y qué pasa con el equipo adjunto cuando la unidad muere o es reemplazada.

---

## 9. Cartas — Manifestantes

> **Baseline de validación (placeholder uniforme):** todas las unidades arrancan con **20 HP**, ataque de **1 slot a elección · 5 de daño** (`reference=Absolute`, `pattern=[1..6]`, `pickCount=1`) y despliegue en **cualquier** slot. Es uniforme a propósito, para validar jugabilidad/diversión antes de diferenciar y balancear. Nombres, costos y pasivas son los definitivos; HP/daño/posición se ajustan al balancear.

### Unidades
| Carta | Costo | maxHp | Slots permitidos | Ataque | Pasiva | Descripción |
|-------|-------|-------|------------------|--------|--------|-------------|
| **Piquetero** | 4 ⚡ | 20 | Cualquiera | 1 a elección · 5 | — | *Lleva el bombo, la bandera y las ganas de parar todo.* |
| **Jubilado** | 5 $ | 20 | Cualquiera | 1 a elección · 5 | — | *83 años, bastón y primera fila.* |
| **Olla Popular** | 3 $ | 20 | Cualquiera | 1 a elección · 5 | +1 $/turno | *Arroz, fideos, solidaridad.* |
| **Quilombero** | 5 ⚡ | 20 | Cualquiera | 1 a elección · 5 | +1 ⚡/turno | *No sabe bien por qué pelea pero lo hace con todo.* |
| **Tuitero Militante** | 2 📣 | 20 | Cualquiera | 1 a elección · 5 | +1 📣/turno | *2.300 seguidores. Siente que cambió la historia.* |

### Acciones — Boost
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Colecta** | 3 📣 | Gana +6 $ | *Pasamos el sombrero.* |
| **Fernet con Cola** | 1 $ | Gana +3 ⚡ | *Hidratación táctica.* |
| **Viral en Redes** | 2 $ | Gana +7 📣 | *Un video de 14 segundos. Tres millones de reproducciones.* |

### Acciones — Sabotaje
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Saqueo** | 1 ⚡ | Oponente pierde 3 $ | *No es saqueo. Es redistribución urgente.* |
| **Asamblea de 6 Horas** | 2 $ | Oponente pierde 7 ⚡ | *Todos hablan. Nadie escucha.* |
| **Fake News** | 3 📣 | Oponente pierde 5 📣 | *Una historia bien contada a tiempo.* |
| **Romper la Marcha** | 4 📣 | -1 HP a una unidad del oponente (atacante elige slot) | *Alguien tira una piedra donde no era.* |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto | Descripción |
|-------|---------|-------|--------|-------------|
| **Paro General** | Ataque | 5 ⚡ | Inflige 14 daño directo a una unidad del oponente | *24 horas de nada. El país en pausa.* |
| **Abrazo Colectivo** | Defensa | 5 $ | Recupera 16 HP a una unidad propia | *El abrazo que cura todo.* |

### Acciones — Efecto especial
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Corte de Ruta** | 3 📣 | El oponente no recibe producción en su próximo turno | *Neumáticos quemados, humo negro.* |
| **Asamblea Popular** | 6 📣 | El jugador recibe el doble de su producción en su próximo turno | *Se vota a mano alzada. Esta vez salió bien.* |

---

## 10. Cartas — Policías

> **Baseline de validación (placeholder uniforme):** todas las unidades arrancan con **20 HP**, ataque de **1 slot a elección · 5 de daño** (`reference=Absolute`, `pattern=[1..6]`, `pickCount=1`) y despliegue en **cualquier** slot. Es uniforme a propósito, para validar jugabilidad/diversión antes de diferenciar y balancear. Nombres, costos y pasivas son los definitivos; HP/daño/posición se ajustan al balancear.

### Unidades
| Carta | Costo | maxHp | Slots permitidos | Ataque | Pasiva | Descripción |
|-------|-------|-------|------------------|--------|--------|-------------|
| **Patrullero** | 6 ⚡ | 20 | Cualquiera | 1 a elección · 5 | — | *Sirena, luces y un oficial de 14 horas de turno.* |
| **Comisaría** | 3 $ | 20 | Cualquiera | 1 a elección · 5 | — | *El edificio más antiguo del barrio.* |
| **Subsidio** | 5 $ | 20 | Cualquiera | 1 a elección · 5 | +1 $/turno | *El Estado se financia a sí mismo.* |
| **Gorra de Barrio** | 3 ⚡ | 20 | Cualquiera | 1 a elección · 5 | +1 ⚡/turno | *Lo conoce todo el mundo. Nadie sabe qué hace.* |
| **Conferencia de Prensa** | 5 📣 | 20 | Cualquiera | 1 a elección · 5 | +1 📣/turno | *El ministro sonríe. Los periodistas anotan.* |

### Acciones — Boost
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Partida Presupuestaria** | 1 📣 | Gana +7 $ | *Existe en el papel. Se aprobó a las 3 AM.* |
| **Licitación Express** | 3 $ | Gana +8 ⚡ | *Una empresa, un sobre, 48 horas.* |
| **Cadena Nacional** | 2 ⚡ | Gana +4 📣 | *Interrumpe la novela. El presidente habla 40 minutos.* |

### Acciones — Sabotaje
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Embargo** | 3 ⚡ | Oponente pierde 7 $ | *El juez firmó. La plata se fue.* |
| **Detención** | 1 $ | Oponente pierde 3 ⚡ | *Demorado para averiguación de antecedentes.* |
| **Censura** | 2 📣 | Oponente pierde 5 📣 | *El artículo fue dado de baja. Por razones técnicas.* |
| **Infiltrado** | 4 $ | -1 HP a una unidad del oponente (atacante elige slot) | *Un tipo raro en la marcha. Nadie lo conocía.* |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto | Descripción |
|-------|---------|-------|--------|-------------|
| **Operativo Apretón** | Ataque | 6 $ | Inflige 18 daño directo a una unidad del oponente | *Cuatro camiones, veinte efectivos, un drone.* |
| **Balas de Goma** | Defensa | 5 📣 | Recupera 12 HP a una unidad propia | *No matan, dicen. Técnicamente.* |

### Acciones — Efecto especial
| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Toque de Queda** | 5 $ | El oponente no recibe producción en su próximo turno | *A las 22hs, todos adentro.* |
| **Decreto de Emergencia** | 3 $ | El jugador recibe el doble de su producción en su próximo turno | *El Congreso estaba de feria. Había urgencia.* |

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

**Atacar con una unidad:** **click** sobre una unidad propia → aparece un **popover** sobre ella con su ataque disponible; al clickear el popover, la unidad ataca. Si el ataque es a elección (`pickCount > 0`), a continuación se clickea el/los slot(s) objetivo. **[FUTURO]** si una unidad llega a tener varios ataques (`List<UnitAttack>`), el popover los lista.

**Efectos activos:** indicador visual por jugador si tiene `activeStatuses` vigentes.

**Indicador de turno:** un **chip** (pill) que salta al lado del jugador activo, más la marca **▶** en su panel de stats. El botón **Terminar turno** está centrado en el tope.

### 11.4 Input

| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta | Arrastrar la carta sobre **JUGAR** (drag & drop) | Seleccionar (1–6) + Enter |
| Descartar carta | Arrastrar la carta sobre **DESCARTAR** (drag & drop) | Seleccionar (1–6) + Backspace |
| Atacar con una unidad | Click en la unidad → click en el popover de ataque | — |
| Elegir slot objetivo (ataque a elección / sabotaje / equipo) | Click en el slot | — |

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
| Producción base por turno | 0 (configurable en código en un lugar) |
| Primer turno sin producción | Sí |
| Lados de facción | Fijos (por ahora): Manifestantes izquierda, Policías derecha |
| Primer jugador | Lo elige la selección de facción (la que arranca); coinflip si no se especifica |
| `suddenDeathStart` | Turno 30 (configurable) |
| `maxTurns` | 100 (configurable) |

---

## 13. Pendientes [DEFINIR]

- **Diferenciación de unidades:** hoy todas comparten baseline (20 HP / 1 a elección · 5 / cualquier slot). Diferenciar HP, patrón de ataque (`reference`/`pattern`/`pickCount`/`damagePerSlot`) y `allowedSlots` por carta al balancear.
- **Apilamiento:** punto de extensión reservado (`UnitSlot.count`), inactivo en v1.
- **Unidades iniciales por facción:** cuáles y cuántas despliega cada facción al empezar.
- **Feedback visual por unidad:** si requiere config por carta en Presentation o alcanzan convenciones globales (§7.10).
- **EquipmentCardData:** estructura, cuando se agregue la primera carta de Equipo (§8.4).
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
