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

**[FUTURO]** El sistema debe soportar agregar nuevas facciones sin modificar código — cada facción es data (pool de cartas + assets).

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

- **HP (punto de extensión):** campo reservado en `PlayerState` para posible reintroducción de HP de jugador. No se usa en la mecánica actual.
- **Recursos:** Dinero, Fuerza, Social — valor actual.
- **Mano:** 6 cartas al inicio. **[DEFINIR]** tamaño de mano y reglas de robo cuando se termine de definir el turno completo.
- **Zona de unidades:** 6 slots numerados (1–6). La posición importa para el combate. Cada slot puede contener una unidad apilada (x1–x5).
- **activeStatuses:** `List<StatusEffect>` — buffs/debuffs temporizados. Se procesan al inicio del turno del jugador afectado.

> El jugador **no tiene HP propio**. Pierde cuando su última unidad muere.

---

## 5. Condiciones de victoria

Un jugador gana cuando **el último HP de la última unidad del oponente llega a 0** (KO).

No hay empate. Existe un límite de turnos como salvavidas (ver §5.1).

### Momentos de evaluación
La condición de KO se evalúa:
1. Al final de cada fase de combate (ataque de unidad).
2. Inmediatamente después de resolver los efectos de una carta jugada.
3. Al aplicar el daño de muerte súbita.

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

1. Cada jugador elige su facción (§11.2).
2. Estado inicial de cada `PlayerState`:
   - **Recursos iniciales:** 5 de cada recurso (configurable).
   - **Unidades:** 0 slots ocupados.
   - **activeStatuses:** vacío.
   - **Mano:** 6 cartas aleatorias del pool de su facción.
3. **Coinflip** determina qué jugador juega primero.
4. Comienza el turno 1 con el jugador elegido.

### Loop de turno

```
1. EFECTOS      — Procesar activeStatuses del jugador activo:
                    Por cada status:
                      → counter--
                      → si counter == 0: disparar su payload y eliminar el status

2. PRODUCCIÓN   — Si NO es el turno 1 de la partida Y skipProduction no está activo:
                    a) Producción de unidades con efecto pasivo Productora
                    b) Multiplicar por productionMultiplier (default 1)
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

El jugador selecciona una unidad propia. El ataque de esa unidad define:
- Qué slots del oponente afecta (ej: slots 1 y 2)
- Cuánto daño hace a cada slot afectado

El daño se aplica al HP de la unidad en cada slot afectado. Si el HP llega a 0, la unidad muere y el slot queda libre.

**[FUTURO]** Cada unidad podría tener múltiples poderes con distintos patrones de slots y daño, o habilidades que recuperen HP.

---

## 7. Arquitectura técnica (Unity)

### 7.1 Jerarquía de CardData

Toda carta es un ScriptableObject. La jerarquía de herencia permite que cada tipo tenga sus propios campos sin contaminar la base.

**CardData (base)**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | string | Identificador único |
| `cardName` | string | Nombre visible |
| `faction` | Faction | Manifestantes / Policías |
| `cardType` | CardType | Unidad / Accion / Equipo |
| `costs` | List\<ResourceCost\> | Costos (hoy 1 entrada; preparado para multi-recurso) |
| `sprite` | Sprite | Imagen de la carta |
| `descriptionText` | string | Texto visible en la carta |
| `playSound` | AudioClip | Sonido al jugar la carta (opcional) |
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
| `attack` | UnitAttack | Patrón de ataque (slots afectados + daño) |
| `passiveEffects` | List\<PassiveEffect\> | Efectos pasivos (producción, boost, etc.) |
| `unitSubtype` | UnitSubtype | Atacante / Defensiva / Productora — punto de extensión, no usado activamente |
| `hitEffect` | UnitHitEffect | Efecto visual al recibir daño |
| `deathEffect` | UnitHitEffect | Efecto visual al morir |
| `attackEffect` | UnitHitEffect | Efecto visual al atacar |

**ActionCardData : CardData**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effects` | List\<CardEffect\> | Efectos que resuelve al jugarse, en orden |

**EquipmentCardData : CardData**

| Campo | Tipo | Descripción |
|-------|------|-------------|
| **[DEFINIR]** | — | Atributos que modifica en la unidad objetivo |

---

### 7.2 UnitAttack

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `targetSlots` | int[] | Slots del oponente que afecta (ej: [1, 2]) |
| `damagePerSlot` | int | Daño aplicado a cada slot afectado |

**[FUTURO]** → `List<UnitAttack>` para múltiples poderes por unidad con distintos patrones.

---

### 7.3 UnitHitEffect

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effectType` | UnitEffectType | Shake / Flash / Animation |
| `animationClip` | AnimationClip | Solo si effectType == Animation |

`UnitEffectType` enum: `Shake`, `Flash`, `Animation` (**[FUTURO]**)

---

### 7.4 PassiveEffect

**[DEFINIR]** estructura completa. A priori: tipo de efecto pasivo + valor (ej: `Productora` de Dinero con +1/turno).

---

### 7.5 UnitSlot (unidad desplegada en el tablero)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `unitData` | UnitCardData | Referencia al template de la carta |
| `currentHp` | int | HP actual |
| `count` | int | Apilamiento (x1–x5) |
| `attachedEquipment` | List\<EquipmentCardData\> | Equipo adjunto mientras la unidad esté viva |
| `slotIndex` | int | Posición en el tablero (1–6) |

**[DEFINIR]** interacción entre apilamiento (`count`) y HP: ¿apilar suma maxHp, multiplica daño, o ambos?

---

### 7.6 PlayerState

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `hp` | int | **Punto de extensión** — no se usa activamente |
| `dinero` | int | Dinero actual |
| `fuerza` | int | Fuerza actual |
| `social` | int | Social actual |
| `hand` | List\<CardData\> | Cartas en mano |
| `unitSlots` | List\<UnitSlot\> | Slots de unidades activas (máx. 6) |
| `activeStatuses` | List\<StatusEffect\> | Buffs/debuffs temporizados activos |
| `faction` | Faction | Facción del jugador |

---

### 7.7 CardEffect (efecto inmediato de carta de Acción)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effectType` | CardEffectType | Qué hace |
| `target` | TargetType | Self / Opponent — sobre qué jugador recae |
| `targetSlot` | int | Slot de unidad afectado. `-1` = el jugador elige al momento de jugar |
| `resourceTarget` | ResourceType | Recurso afectado (solo para `ModifyResource`) |
| `value` | int | Magnitud |
| `status` | StatusEffect | Plantilla del status a insertar (solo si `effectType == ApplyStatus`) |

> `ModifyHP` ahora opera sobre la unidad en `targetSlot` del jugador `target`, no sobre el HP del jugador. El jugador no tiene HP propio.

**CardEffectType:** `ModifyHP`, `ModifyResource`, `RemoveUnit`, `ApplyStatus`
**TargetType:** `Self` / `Opponent`
**ResourceType:** `Dinero` / `Fuerza` / `Social`

---

### 7.8 StatusEffect (buff/debuff temporizado)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `statusType` | StatusType | SkipProduction / DoubleProduction |
| `value` | int | Magnitud |
| `counter` | int | Turnos hasta disparar (se decrementa al inicio del turno del afectado) |

**StatusType:** `SkipProduction`, `DoubleProduction`

---

### 7.9 GameEngine — responsabilidades

Único responsable de resolver `CardEffect`, procesar `activeStatuses`, resolver ataques de unidades y manejar transiciones de turno.

**[FUTURO]** Preparar punto de extensión `IPlayerController` para soportar IA y multijugador online sin modificar `GameEngine`.

**[FUTURO]** Preparar punto de extensión para deckbuilding (pool de cartas configurable por jugador).

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

Se despliega en un slot libre (1–6). Si todos los slots están ocupados, el jugador elige reemplazar un slot existente (queda en x1 con la nueva unidad) o cancelar sin gastar recursos.

Cada despliegue adicional de la misma unidad en el mismo slot suma +1 al contador (máximo x5).

**[DEFINIR]** interacción entre `count` y HP al apilar.

### 8.4 Equipo

Se arrastra sobre un slot con unidad activa. Queda adjunto a esa unidad mientras esté viva.

**[DEFINIR]** qué atributos puede modificar y si tiene duración o es permanente.

---

## 9. Cartas — Manifestantes

> Nota: costos actualizados a recurso único. Valores de HP y ataque de unidades **[DEFINIR]** al balancear.

### Unidades
| Carta | Costo | maxHp | Ataque (slots / daño) | Pasiva | Descripción |
|-------|-------|-------|-----------------------|--------|-------------|
| **Piquetero** | 4 ⚡ | [DEFINIR] | [DEFINIR] | — | *Lleva el bombo, la bandera y las ganas de parar todo.* |
| **Jubilado** | 5 $ | [DEFINIR] | [DEFINIR] | — | *83 años, bastón y primera fila.* |
| **Olla Popular** | 3 $ | [DEFINIR] | [DEFINIR] | +1 $/turno | *Arroz, fideos, solidaridad.* |
| **Quilombero** | 5 ⚡ | [DEFINIR] | [DEFINIR] | +1 ⚡/turno | *No sabe bien por qué pelea pero lo hace con todo.* |
| **Tuitero Militante** | 2 📣 | [DEFINIR] | [DEFINIR] | +1 📣/turno | *2.300 seguidores. Siente que cambió la historia.* |

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

> Nota: costos actualizados a recurso único. Valores de HP y ataque de unidades **[DEFINIR]** al balancear.

### Unidades
| Carta | Costo | maxHp | Ataque (slots / daño) | Pasiva | Descripción |
|-------|-------|-------|-----------------------|--------|-------------|
| **Patrullero** | 6 ⚡ | [DEFINIR] | [DEFINIR] | — | *Sirena, luces y un oficial de 14 horas de turno.* |
| **Comisaría** | 3 $ | [DEFINIR] | [DEFINIR] | — | *El edificio más antiguo del barrio.* |
| **Subsidio** | 5 $ | [DEFINIR] | [DEFINIR] | +1 $/turno | *El Estado se financia a sí mismo.* |
| **Gorra de Barrio** | 3 ⚡ | [DEFINIR] | [DEFINIR] | +1 ⚡/turno | *Lo conoce todo el mundo. Nadie sabe qué hace.* |
| **Conferencia de Prensa** | 5 📣 | [DEFINIR] | [DEFINIR] | +1 📣/turno | *El ministro sonríe. Los periodistas anotan.* |

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

Tras iniciar, cada jugador elige su facción: **Manifestantes** o **Policías**.

### 11.3 Pantalla de juego

Pantalla única. Jugadores enfrentados horizontalmente. 6 slots de unidades por jugador siempre visibles. La mano y botones pertenecen solo al jugador activo.

```
┌────────────────────────────────────────────────────────────────────────┐
│  MANIFESTANTES                [TURNO: MANIFESTANTES]         POLICÍAS  │
│  $: 9  ⚡: 6  📣: 14          [efectos activos]    $:12  ⚡:8  📣:5    │
│                                                                        │
│  [S1][S2][S3][S4][S5][S6]                    [S1][S2][S3][S4][S5][S6] │
│                                                                        │
│  [c1]  [c2]  [c3]  [c4]  [c5]  [c6]                                  │
│              [JUGAR]  [DESCARTAR]  [ATACAR CON UNIDAD]                │
└────────────────────────────────────────────────────────────────────────┘
```

**Efectos activos:** indicador visual por jugador si tiene `activeStatuses` vigentes.

**Indicador de turno:** banner central con la facción activa.

### 11.4 Input

| Acción | Mouse | Teclado |
|--------|-------|---------|
| Seleccionar carta | Click | Teclas 1–6 |
| Jugar carta | Click "Jugar" | Enter |
| Descartar carta | Click "Descartar" | Backspace |
| Seleccionar unidad propia para atacar | Click unidad | — |
| Seleccionar slot enemigo (sabotaje/equipo) | Click slot | — |

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
| Unidades apilables | Sí — hasta x5 por slot |
| HP inicial de jugador | — (no usado activamente; punto de extensión) |
| Recursos iniciales | 5 de cada uno (configurable) |
| Producción base por turno | 0 (configurable en código en un lugar) |
| Primer turno sin producción | Sí |
| Primer jugador | Coinflip aleatorio |
| `suddenDeathStart` | Turno 30 (configurable) |
| `maxTurns` | 100 (configurable) |

---

## 13. Pendientes [DEFINIR]

- **Atributos de unidades:** HP y patrón de ataque de cada carta de unidad (se define al balancear).
- **Apilamiento + HP:** ¿apilar una unidad suma maxHp al HP actual, multiplica daño, o ambos?
- **PassiveEffect:** estructura del objeto (tipo + valor + recurso afectado).
- **EquipmentCardData:** qué atributos modifica, si tiene duración o es permanente.
- **Mano:** tamaño definitivo y reglas de robo (pendiente de definir turno completo).
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
