# PiqueteDefend — Game Specification

## 1. Concepto

Juego de cartas por turnos para 2 jugadores en local. Dos facciones enfrentadas en el contexto político-cómico argentino: **Manifestantes** vs **Policías**. Inspirado en Castle Wars / Arcomage.

---

## 2. Facciones

### Manifestantes
- Representan al pueblo organizado en la calle.
- Estética: bombos, banderas, pañuelos, ollas populares.

### Policías
- Representan la fuerza del orden.
- Estética: escudos, patrulleros, gases lacrimógenos, comisarías.

Cada facción tiene su propio set de cartas temático, pero comparten la misma mecánica de recursos y reglas.

---

## 3. Recursos

Cada jugador maneja 3 recursos independientes. Al inicio de cada turno **antes de jugar una carta**, el jugador recibe su producción automática.

| Recurso | Descripción | Producción inicial | Máximo |
|---------|-------------|-------------------|--------|
| **Dinero** ($) | Plata para movilizar gente y pagar logística | 5 / turno | 99 |
| **Fuerza** (⚡) | Capacidad física, represión o resistencia | 3 / turno | 99 |
| **Social** (📣) | Apoyo popular, organización, narrativa | 2 / turno | 99 |

> La producción de cada recurso puede aumentarse mediante unidades pasivas activas.

---

## 4. Estado del jugador

Cada jugador tiene:

- **HP (Health Points):** 100 puntos al inicio. Llegar a 0 = derrota.
- **Recursos:** Dinero, Fuerza, Social — valor actual + producción base por turno.
- **Mano:** 6 cartas siempre visibles.
- **Zona de unidades:** 3 slots, cada uno con tipo de unidad y contador (x1–x5).
- **activeStatuses:** `List<StatusEffect>` — buffs/debuffs temporizados que afectan a este jugador. Se procesan al inicio de su turno (ver §6 y §7.2).

---

## 5. Condiciones de victoria

Un jugador gana si ocurre **cualquiera** de estas condiciones:

| Condición | Descripción |
|-----------|-------------|
| **KO** | Reducir los HP del oponente a 0 |
| **Hegemonía Social** | Acumular 60 puntos de Social |
| **Poder Económico** | Acumular 100 puntos de Dinero |

No hay empate. Existe un límite de turnos como salvavidas (ver §5.1).

### Orden de prioridad de victoria (en orden):
1. **KO** — si el HP del oponente llega a 0, gana quien causó el daño (prioridad máxima)
2. **Hegemonía Social** — acumular ≥ 60 📣
3. **Poder Económico** — acumular ≥ 100 $

Si KO y una condición de recurso se cumplen simultáneamente, gana por KO.

### Momentos de evaluación
Las condiciones se chequean en **dos momentos**, para que una victoria por recurso alcanzada en el propio turno no pueda ser revertida por el oponente antes de contar:

1. **Al inicio del turno**, tras la fase de producción (cubre KO por unidades atacantes y recursos generados pasivamente).
2. **Inmediatamente después de resolver los efectos de una carta jugada** (cubre boost de recursos y daño directo que cruzan el umbral en el acto).

Si una condición se cumple en cualquiera de los dos momentos, la partida termina de inmediato.

### Recursos negativos
Los recursos nunca bajan de **0**. El exceso de reducción se descarta. Este comportamiento es configurable a futuro por balanceo.

### 5.1 Terminación garantizada (anti-stalemate)

Como la partida no tiene mazo que se agote y los recursos tienen piso 0, dos jugadores pasivos podrían loopear infinito. Hay dos salvavidas, ambos con parámetros configurables:

**Muerte súbita (mecanismo principal):**
- A partir del turno `suddenDeathStart` (default: **40**), al final de cada turno ambos jugadores reciben daño incremental.
- El daño escala: turno 40 → 1, turno 41 → 2, turno 42 → 3, … Garantiza un KO mucho antes del límite duro.
- Este daño ignora la absorción de unidades defensivas (es inevitable).

**Límite de turnos (backstop duro):**
- `maxTurns` (default: **100**). Si se alcanza sin un ganador, la partida termina por desempate determinista:
  1. Mayor HP gana.
  2. Si empatan en HP → mayor suma de recursos (Dinero + Fuerza + Social).
  3. Si aún empatan → gana el jugador que **no** fue primero (compensa la ventaja de iniciativa).

> Con muerte súbita activa el límite de 100 prácticamente nunca se alcanza; queda solo como red de seguridad.

---

## 6. Estructura de un turno

### Inicio de partida (setup)

1. Cada jugador elige su facción (§11.2).
2. Estado inicial de cada `PlayerState`:
   - **HP:** 100
   - **Recursos iniciales:** Dinero 3, Fuerza 2, Social 1 (suficiente para jugar una carta barata en el primer turno, incluso antes de la producción).
   - **Unidades:** 0 slots ocupados.
   - **activeStatuses:** vacío.
   - **Mano:** 6 cartas aleatorias del pool de su facción.
3. **Coinflip** determina qué jugador juega primero (a futuro: animación o pantalla de sorteo).
4. Comienza el turno 1 con el jugador elegido.

> Nota de balance a validar: el jugador que va primero tiene una ventaja de iniciativa. El desempate del límite de turnos la compensa parcialmente (§5.1). Verificar magnitud con la simulación.

### Loop de turno

```
1. EFECTOS      — Procesar activeStatuses del jugador activo:
                    Por cada status:
                      → counter--
                      → si counter == 0: disparar su payload y eliminar el status
                    Los payloads disparados producen modificadores para ESTE turno
                    (ej. skipProduction = true, productionMultiplier = 2).

2. PRODUCCIÓN   — Si skipProduction NO está activo este turno:
                    a) Producción base de recursos + producción de unidades Productoras
                    b) Multiplicar el total por productionMultiplier (default 1; 2 si disparó DoubleProduction)
                    c) Aplicar daño neto de unidades enemigas (ver fórmula abajo)
                  → Evaluar condiciones de victoria (ver §5)

3. ACCIÓN       — El jugador ELIGE una de dos opciones:
                    a) Jugar 1 carta: pagar costo → resolver cada CardEffect en orden
                       → Evaluar condiciones de victoria (ver §5)
                    b) Descartar 1 carta: sin costo, sin efecto
                  → Reponer la mano a 6 con una carta aleatoria del pool

4. FIN DE TURNO — Si turno ≥ suddenDeathStart: aplicar daño incremental de
                  muerte súbita a ambos jugadores (ignora defensas, ver §5.1)
                  → Evaluar condiciones de victoria
                  → Pasa el turno al oponente (o termina si turno == maxTurns)
```

> No hay robo de cartas. La mano es fija y siempre visible.
> El `productionMultiplier` de DoubleProduction afecta **base + unidades** (decisión de diseño; ajustable por balanceo).

### Resolución de daño (paso 2c)

```
daño_total = suma(contador × 1) de todas las unidades Atacantes enemigas
absorción  = suma(contador × 1) de todas las unidades Defensivas propias  ← primero
daño_neto  = max(0, daño_total - absorción)
HP_propio -= daño_neto
```

### Ejemplo de status temporizado (resuelve el timing del contador)

**Corte de Ruta** (SkipProduction sobre el oponente, `counter: 1`):

| Turno | Jugador | Qué pasa |
|-------|---------|----------|
| 5 | Manifestantes | Juega la carta. **Inmediato:** inserta `StatusEffect{SkipProduction, counter: 1}` en `activeStatuses` de Policías |
| 6 | Policías | Fase EFECTOS: `counter` 1→0 → dispara SkipProduction → se elimina. Fase PRODUCCIÓN: **omitida** |
| 7 | Manifestantes | Sin status. Turno normal |

**Asamblea Popular** (DoubleProduction sobre sí mismo, `counter: 1`): se inserta en el `activeStatuses` propio y dispara al inicio del **próximo turno propio** (turno 7 si se jugó en el 5), duplicando esa producción.

> El `counter` se mide en **turnos del jugador afectado** y se decrementa **antes** de disparar. `counter: 1` = "el próximo turno del afectado". No hay off-by-one.

---

## 7. Arquitectura técnica (Unity)

El modelo tiene **dos capas**:
- **CardEffect** — efecto inmediato que se resuelve al jugar la carta.
- **StatusEffect** — buff/debuff temporizado con contador, que un `CardEffect` inserta en un jugador. Su payload se difiere hasta que el contador llega a 0.

Los efectos "diferidos" (saltear producción, doblar producción) no son un tipo especial: son un `CardEffect` inmediato de tipo `ApplyStatus` que coloca un `StatusEffect` con contador en el objetivo.

### 7.1 CardEffect (efecto inmediato de carta)

Toda consecuencia de jugar una carta se modela como uno o más `CardEffect`, resueltos en orden al instante.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effectType` | CardEffectType | Qué hace |
| `target` | TargetType | Sobre quién recae: `Self` / `Opponent` |
| `resourceTarget` | ResourceType | Recurso afectado (solo para `ModifyResource`) |
| `value` | int | Magnitud (con signo: daño/drenaje negativos, cura/boost positivos) |
| `status` | StatusEffect | Plantilla del status a insertar (solo si `effectType == ApplyStatus`) |

**CardEffectType (enum)**

| Valor | Descripción |
|-------|-------------|
| `ModifyHP` | Suma/resta HP al `target` (daño = Opponent con value negativo; cura = Self con value positivo) |
| `ModifyResource` | Suma/resta `resourceTarget` al `target` (boost = Self+; drenaje = Opponent−) |
| `RemoveUnit` | Resta `value` al contador de una unidad del `target` (requiere selección de slot) |
| `ApplyStatus` | Inserta una copia de `status` en el `activeStatuses` del `target` |

**TargetType (enum):** `Self` / `Opponent`
**ResourceType (enum):** `Dinero` / `Fuerza` / `Social`

---

### 7.2 StatusEffect (buff/debuff temporizado)

Vive en el `activeStatuses` de un jugador. Se procesa en la fase EFECTOS al inicio de cada turno del jugador afectado (ver §6).

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `statusType` | StatusType | Qué hace al dispararse |
| `value` | int | Magnitud (ej. multiplicador de producción) |
| `counter` | int | Turnos del jugador afectado hasta disparar. Se decrementa al inicio del turno; dispara y se elimina al llegar a 0 |

**StatusType (enum)**

| Valor | Descripción |
|-------|-------------|
| `SkipProduction` | El jugador no recibe producción en el turno en que dispara |
| `DoubleProduction` | La producción de ese turno se multiplica por `value` (=2) |

> Extender el juego = agregar un valor a `CardEffectType` o `StatusType` + su resolución en `GameManager`. Las cartas siguen siendo solo data.

---

### 7.2.1 Mapeo de cartas a efectos (ejemplos)

| Carta | CardEffect(s) |
|-------|---------------|
| **Paro General** | `{ ModifyHP, Opponent, value: -14 }` |
| **Abrazo Colectivo** | `{ ModifyHP, Self, value: +16 }` |
| **Colecta** | `{ ModifyResource, Self, Dinero, value: +6 }` |
| **Saqueo** | `{ ModifyResource, Opponent, Dinero, value: -3 }` |
| **Infiltrado** | `{ RemoveUnit, Opponent, value: -1 }` |
| **Corte de Ruta** | `{ ApplyStatus, Opponent, status: { SkipProduction, counter: 1 } }` |
| **Asamblea Popular** | `{ ApplyStatus, Self, status: { DoubleProduction, value: 2, counter: 1 } }` |

> Una carta puede llevar varios `CardEffect` (ej. daño + drenaje) sin tocar código.

---

### 7.3 CardData (ScriptableObject)

Cada carta es un asset independiente. Una carta de acción puede aplicar **múltiples efectos**.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | string | Identificador único |
| `cardName` | string | Nombre visible |
| `faction` | Faction | Manifestantes / Policías |
| `cardType` | CardType | Accion / Unidad |
| `actionCategory` | ActionCategory | Ataque / Defensa / Sabotaje / Boost / EfectoEspecial (solo si cardType == Accion). Categoría visual/temática para UI; no afecta la lógica |
| `unitSubtype` | UnitSubtype | Atacante / Defensiva / Productora (solo si cardType == Unidad) |
| `productionResource` | ResourceType | Recurso que produce (solo si unitSubtype == Productora) |
| `costDinero` | int | Costo en $ |
| `costFuerza` | int | Costo en ⚡ |
| `costSocial` | int | Costo en 📣 |
| `effects` | List\<CardEffect\> | Efectos que resuelve la carta al jugarse (solo cartas de Acción) |
| `sprite` | Sprite | Imagen de la carta |
| `descriptionText` | string | Texto visible en la carta |

> Las cartas de **Unidad** dejan `effects` vacío — su efecto es pasivo y lo maneja `GameManager` por el `unitSubtype` y el contador del slot. Las cartas de **Acción** usan `effects`.

**ActionCategory (enum):** `Ataque` / `Defensa` / `Sabotaje` / `Boost` / `EfectoEspecial`. Solo organiza la presentación (color, ícono, agrupación). La lógica real siempre la define la lista `effects`.

---

### 7.4 UnitSlot (clase serializable)

Representa un slot ocupado en la zona de unidades de un jugador.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `unitData` | CardData | Referencia a la carta de unidad |
| `count` | int | Contador actual (1–5) |

---

### 7.5 PlayerState (clase)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `hp` | int | HP actual |
| `dinero` | int | Dinero actual |
| `fuerza` | int | Fuerza actual |
| `social` | int | Social actual |
| `hand` | List\<CardData\> | Cartas en mano (siempre 6) |
| `unitSlots` | List\<UnitSlot\> | Slots de unidades activas (máx. 3) |
| `activeStatuses` | List\<StatusEffect\> | Buffs/debuffs temporizados activos |
| `faction` | Faction | Facción del jugador |

---

### 7.6 GameManager — flujo de resolución

Único responsable de resolver `CardEffect`, procesar `activeStatuses` y manejar las transiciones de turno. Procesa `activeStatuses` antes de la producción, garantizando que cualquier nuevo `CardEffectType` o `StatusType` se integre en un solo lugar.

El pool de cartas de cada facción es una `List<CardData>` filtrada por `faction`. Al jugar o descartar, se reemplaza la carta por una aleatoria del pool.

---

## 8. Sistema de cartas

### 8.1 Pool y mano

- No existe mazo ni descarte.
- Cada jugador tiene **6 cartas** siempre visibles en pantalla.
- Al jugar o descartar una carta, se reemplaza automáticamente por una aleatoria del pool de la facción.
- El pool puede repetir cartas.

### 8.2 Acción (un solo uso)

Se juega, se resuelven todos sus `CardEffect` en orden, y se reemplaza por una carta nueva.

Los subtipos (Ataque, Defensa, Sabotaje, Boost, EfectoEspecial) son solo una categoría visual/temática — la lógica real la define la lista `effects` de la carta.

### 8.3 Unidad (persistente y pasiva)

Se despliega en la zona de unidades. Su efecto es siempre pasivo — el `GameManager` lo resuelve automáticamente cada turno según el `unitSubtype` y el `count` del slot.

- Cada despliegue suma **+1** al contador del slot correspondiente (máximo x5).
- El efecto de sabotaje `RemoveUnit` resta **-1** al contador de un slot elegido por el atacante.
- Si el contador llega a **0**, el slot se libera.

| UnitSubtype | Efecto pasivo por turno |
|-------------|------------------------|
| `Atacante` | +1 daño al oponente por instancia en el contador |
| `Defensiva` | -1 daño entrante por instancia en el contador |
| `Productora` | +1 del recurso definido en `productionResource` por instancia |

> Ejemplo: oponente tiene `Patrullero x4` → 4 daño. Yo tengo `Escudo Humano x2` → absorbe 2. Daño neto = **2 HP**.

**Slots llenos (3/3):** el jugador elige entre reemplazar un slot existente (queda en x1 con la nueva unidad) o cancelar la jugada sin gastar recursos.

---

## 9. Cartas — Manifestantes

### Unidades
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Piquetero** | Atacante | 2 ⚡ + 2 $ | +1 Piquetero. Cada uno suma +1 de daño al oponente por turno |
| **Escudo Humano** | Defensiva | 5 $ + 1 ⚡ | +1 Escudo Humano. Cada uno resta -1 al daño entrante por turno |
| **Olla Popular** | Productora $ | 2 $ + 1 📣 | +1 Olla Popular. Cada una produce +1 $/turno |
| **Fogón** | Productora ⚡ | 4 ⚡ + 1 📣 | +1 Fogón. Cada uno produce +1 ⚡/turno |
| **Megáfono** | Productora 📣 | 1 📣 + 1 $ | +1 Megáfono. Cada uno produce +1 📣/turno |

### Acciones — Boost
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Colecta** | 3 📣 | Gana +6 $ |
| **Adrenalina** | 1 $ | Gana +3 ⚡ |
| **Viral en Redes** | 2 $ | Gana +7 📣 |

### Acciones — Sabotaje
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Saqueo** | 1 ⚡ | Oponente pierde 3 $ |
| **Agotamiento** | 2 $ | Oponente pierde 7 ⚡ |
| **Fake News** | 3 📣 | Oponente pierde 5 📣 |
| **Romper la Marcha** | 1 ⚡ + 3 📣 | -1 a una unidad del oponente (el atacante elige el slot) |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Paro General** | Ataque | 2 $ + 3 ⚡ | Inflige 14 daño directo al oponente |
| **Abrazo Colectivo** | Defensa | 4 $ + 1 📣 | Recupera 16 HP propios |

### Acciones — Efecto especial
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Corte de Ruta** | 1 ⚡ + 2 📣 | El oponente no recibe producción en su próximo turno |
| **Asamblea Popular** | 6 📣 | El jugador recibe el doble de su producción propia en su próximo turno |

---

## 10. Cartas — Policías

### Unidades
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Patrullero** | Atacante | 4 ⚡ + 2 $ | +1 Patrullero. Cada uno suma +1 de daño al oponente por turno |
| **Comisaría** | Defensiva | 2 $ + 1 ⚡ | +1 Comisaría. Cada una resta -1 al daño entrante por turno |
| **Subsidio** | Productora $ | 4 $ + 1 📣 | +1 Subsidio. Cada uno produce +1 $/turno |
| **Entrenamiento** | Productora ⚡ | 1 ⚡ + 2 📣 | +1 Entrenamiento. Cada uno produce +1 ⚡/turno |
| **Conferencia de Prensa** | Productora 📣 | 3 $ + 2 📣 | +1 Conferencia. Cada una produce +1 📣/turno |

### Acciones — Boost
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Partida Presupuestaria** | 1 📣 | Gana +7 $ |
| **Refuerzo** | 3 $ | Gana +8 ⚡ |
| **Cadena Nacional** | 2 ⚡ | Gana +4 📣 |

### Acciones — Sabotaje
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Embargo** | 3 ⚡ | Oponente pierde 7 $ |
| **Detención** | 1 $ | Oponente pierde 3 ⚡ |
| **Censura** | 2 📣 | Oponente pierde 5 📣 |
| **Infiltrado** | 3 $ + 1 📣 | -1 a una unidad del oponente (el atacante elige el slot) |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Operativo Especial** | Ataque | 4 $ + 2 ⚡ | Inflige 18 daño directo al oponente |
| **Escudo Antidisturbios** | Defensa | 2 $ + 3 📣 | Recupera 12 HP propios |

### Acciones — Efecto especial
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Toque de Queda** | 4 $ + 1 ⚡ | El oponente no recibe producción en su próximo turno |
| **Decreto de Emergencia** | 3 $ | El jugador recibe el doble de su producción propia en su próximo turno |

---

## Balance verificable entre facciones

> Valores validados con simulación de 10.000 partidas (AI greedy vs greedy). Win rate resultante: Manifestantes 47.7% — Policías 52.3% (gap ±5pp, rango GOOD).

| Categoría | Manifestantes | Policías |
|-----------|--------------|----------|
| Boost $ | +6 (medio) | +7 (medio) |
| Boost ⚡ | +3 (barato) | +8 (caro) |
| Boost 📣 | +7 (medio) | +4 (medio) |
| **Total boost** | **+16** | **+19** |
| Sabotaje $ | -3 (barato) | -7 (caro) |
| Sabotaje ⚡ | -7 (caro) | -3 (barato) |
| Sabotaje 📣 | -5 (medio) | -5 (medio) |
| **Total sabotaje** | **-15** | **-15** |
| Daño directo | 14 | 18 |
| Recuperación HP | 16 | 12 |
| **Daño + recuperación** | **30** | **30** |

> Asimetría temática: Policías pega más fuerte (18 vs 14), Manifestantes se cura más (16 vs 12). Total (daño + cura) igual en ambas facciones (30). Policías invierte más en Fuerza, Manifestantes más en recursos sociales.

---

## 11. UI / Pantalla

### 11.1 Menú principal

Pantalla de inicio con un botón central **Jugar**. Espacio reservado para futuras opciones (Ajustes, Créditos, etc.) sin implementarlas en v1.

### 11.2 Selección de facción

Tras iniciar, cada jugador elige su facción: **Manifestantes** o **Policías**.

### 11.3 Pantalla de juego

Pantalla única sin divisiones. Jugadores enfrentados horizontalmente. Torres en el centro. Stats y slots siempre visibles para ambos. La mano y botones pertenecen solo al jugador activo — al cambiar el turno, la mano anterior desaparece y aparece la del nuevo jugador activo en su lado.

```
┌────────────────────────────────────────────────────────────────────────┐
│  MANIFESTANTES                [TURNO: MANIFESTANTES]         POLICÍAS  │
│  HP: 85  $: 9  ⚡: 6  📣: 14    [efectos activos]   HP: 100  $:12 ⚡:8 │
│                                                                        │
│  [Slot 1]                                                   [Slot 1]  │
│  [Slot 2]          [TORRE]              [TORRE]             [Slot 2]  │
│  [Slot 3]                                                   [Slot 3]  │
│                                                                        │
│  [c1]  [c2]  [c3]  [c4]  [c5]  [c6]                                  │
│                         [JUGAR]  [DESCARTAR]                          │
└────────────────────────────────────────────────────────────────────────┘
  ◄── jugador activo (mano visible)
```

**Efectos activos:** cada jugador muestra un indicador visual si tiene `activeStatuses` vigentes (ej: ícono de producción bloqueada, ícono de producción doble).

**Indicador de turno:** banner central con el nombre de la facción activa.

### 11.4 Input

| Acción | Mouse | Teclado | Gamepad |
|--------|-------|---------|---------|
| Seleccionar carta | Click | Teclas 1–6 | D-pad |
| Jugar carta | Click "Jugar" | Enter | A / X |
| Descartar carta | Click "Descartar" | Backspace | B / O |
| Seleccionar slot enemigo (sabotaje) | Click slot | Tab + Enter | D-pad + A |

### 11.5 Anatomía de una carta

Cada carta muestra:
- **Imagen** (placeholder en v1)
- **Nombre**
- **Costo** — íconos de los recursos requeridos con su cantidad
- **Efecto** — texto corto con `+` o `-` y el recurso/unidad afectado, o descripción del efecto especial

### 11.6 Pantalla de victoria

Overlay sobre la pantalla de juego con:
- Mensaje de victoria (nombre de facción ganadora + condición)
- Botón **Revancha** — reinicia la partida con las mismas facciones
- Botón **Menú principal** — vuelve al inicio

---

## 12. Parámetros definidos

| Parámetro | Valor |
|-----------|-------|
| Cartas visibles en mano | 6 |
| Slots de unidades por jugador | 3 |
| Unidades apilables | Sí — hasta x5 por slot |
| Slots fijos por tipo | No — cualquier unidad ocupa cualquier slot libre |
| HP inicial | 100 |
| Recursos iniciales | Dinero 3, Fuerza 2, Social 1 |
| Producción base por turno | Dinero 5, Fuerza 3, Social 2 |
| Umbral Hegemonía Social | 60 📣 |
| Umbral Poder Económico | 100 $ |
| Primer jugador | Coinflip aleatorio |
| `suddenDeathStart` | Turno 40 (configurable) |
| `maxTurns` | 100 (configurable) |

---

## 13. Fuera de scope (v1)

- Construcción de mazos personalizada
- Más de 2 jugadores
- Online / multijugador en red
- Animaciones elaboradas
- Progresión / desbloqueo de cartas
