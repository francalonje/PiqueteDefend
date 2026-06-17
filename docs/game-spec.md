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
- **activeEffects:** `List<PlayerEffect>` — efectos activos sobre este jugador (buffs y debuffs temporales). Procesados al inicio de cada turno antes de la producción.

---

## 5. Condiciones de victoria

Un jugador gana si ocurre **cualquiera** de estas condiciones:

| Condición | Descripción |
|-----------|-------------|
| **KO** | Reducir los HP del oponente a 0 |
| **Hegemonía Social** | Acumular 50 puntos de Social |
| **Poder Económico** | Acumular 75 puntos de Dinero |

No hay empate ni límite de turnos.

### Orden de prioridad de victoria (evaluado al inicio de cada turno, en orden):
1. **KO** — si el HP del oponente llega a 0, gana quien causó el daño (prioridad máxima)
2. **Hegemonía Social** — acumular ≥ 50 📣
3. **Poder Económico** — acumular ≥ 75 $

Si KO y una condición de recurso se cumplen simultáneamente, gana por KO.

### Recursos negativos
Los recursos nunca bajan de **0**. El exceso de reducción se descarta. Este comportamiento es configurable a futuro por balanceo.

---

## 6. Estructura de un turno

```
1. EFECTOS      — Procesar activeEffects del jugador activo:
                    → Aplicar cada efecto con turnsRemaining > 0
                    → Decrementar turnsRemaining de cada efecto
                    → Eliminar efectos con turnsRemaining == 0

2. PRODUCCIÓN   — Si no hay efecto SkipProduction activo:
                    a) Producción base de recursos (× 2 si hay DoubleProduction activo)
                    b) Producción de unidades Productoras activas
                    c) Daño neto de unidades enemigas (ver fórmula abajo)
                  → Evaluar condiciones de victoria (ver §5)

3. ACCIÓN       — El jugador ELIGE una de dos opciones:
                    a) Jugar 1 carta: pagar costo → aplicar cada PlayerEffect de la carta
                    b) Descartar 1 carta: sin costo, sin efecto

4. FIN DE TURNO — Pasa el turno al oponente
```

> No hay robo de cartas. La mano es fija y siempre visible.

### Resolución de daño (paso 2c)

```
daño_total = suma(contador × 1) de todas las unidades Atacantes enemigas
absorción  = suma(contador × 1) de todas las unidades Defensivas propias  ← primero
daño_neto  = max(0, daño_total - absorción)
HP_propio -= daño_neto
```

---

## 7. Arquitectura técnica (Unity)

### 7.1 PlayerEffect (clase serializable)

Unidad mínima de efecto. Toda acción del juego que modifica el estado de un jugador se modela como uno o más `PlayerEffect`.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `effectType` | EffectType | Qué hace el efecto |
| `resourceTarget` | ResourceType | Recurso afectado (solo para efectos de recurso) |
| `value` | int | Magnitud del efecto |
| `turnsRemaining` | int | Turnos que dura. `1` = se aplica este turno y expira. `0` = ya expiró |

**EffectType (enum)**

| Valor | Descripción |
|-------|-------------|
| `DamageOpponent` | Inflige daño directo al HP del oponente |
| `HealSelf` | Recupera HP propio |
| `GainResource` | Suma recursos al jugador que lo tiene |
| `DrainOpponentResource` | Resta recursos al oponente |
| `RemoveOpponentUnit` | -1 al contador de una unidad enemiga (requiere selección de slot) |
| `SkipOpponentProduction` | El oponente no recibe producción en su próximo turno |
| `DoubleOwnProduction` | El jugador recibe producción x2 en su próximo turno |

> Agregar un nuevo tipo de efecto al juego = agregar un valor al enum + su lógica de resolución en `GameManager`. Las cartas no necesitan cambios.

**ResourceType (enum):** `Dinero` / `Fuerza` / `Social`

---

### 7.2 CardData (ScriptableObject)

Cada carta es un asset independiente. Una carta puede aplicar **múltiples efectos**.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | string | Identificador único |
| `cardName` | string | Nombre visible |
| `faction` | Faction | Manifestantes / Policías |
| `cardType` | CardType | Accion / Unidad |
| `unitSubtype` | UnitSubtype | Atacante / Defensiva / Productora (solo si cardType == Unidad) |
| `productionResource` | ResourceType | Recurso que produce (solo si unitSubtype == Productora) |
| `costDinero` | int | Costo en $ |
| `costFuerza` | int | Costo en ⚡ |
| `costSocial` | int | Costo en 📣 |
| `effects` | List\<PlayerEffect\> | Efectos que aplica la carta al jugarse |
| `sprite` | Sprite | Imagen de la carta |
| `descriptionText` | string | Texto visible en la carta |

> Las cartas de **Unidad** no usan `effects` — su efecto es pasivo y lo maneja `GameManager` por el tipo y contador del slot. Las cartas de **Acción** usan `effects` para aplicar sus consecuencias.

---

### 7.3 UnitSlot (clase serializable)

Representa un slot ocupado en la zona de unidades de un jugador.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `unitData` | CardData | Referencia a la carta de unidad |
| `count` | int | Contador actual (1–5) |

---

### 7.4 PlayerState (clase)

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `hp` | int | HP actual |
| `dinero` | int | Dinero actual |
| `fuerza` | int | Fuerza actual |
| `social` | int | Social actual |
| `hand` | List\<CardData\> | Cartas en mano (siempre 6) |
| `unitSlots` | List\<UnitSlot\> | Slots de unidades activas (máx. 3) |
| `activeEffects` | List\<PlayerEffect\> | Efectos temporales activos |
| `faction` | Faction | Facción del jugador |

---

### 7.5 GameManager — flujo de resolución

Único responsable de aplicar efectos y transiciones de turno. Procesa `activeEffects` antes de la producción, garantizando que cualquier nuevo `EffectType` se integre en un solo lugar.

El pool de cartas de cada facción es una `List<CardData>` filtrada por `faction`. Al jugar o descartar, se reemplaza la carta por una aleatoria del pool.

---

## 8. Sistema de cartas

### 8.1 Pool y mano

- No existe mazo ni descarte.
- Cada jugador tiene **6 cartas** siempre visibles en pantalla.
- Al jugar o descartar una carta, se reemplaza automáticamente por una aleatoria del pool de la facción.
- El pool puede repetir cartas.

### 8.2 Acción (un solo uso)

Se juega, se aplican todos sus `PlayerEffect` en orden, y se reemplaza por una carta nueva.

Los subtipos (Ataque, Defensa, Sabotaje, Boost, EfectoEspecial) son solo una categoría visual/temática — la lógica real la define la lista `effects` de la carta.

### 8.3 Unidad (persistente y pasiva)

Se despliega en la zona de unidades. Su efecto es siempre pasivo — el `GameManager` lo resuelve automáticamente cada turno según el `unitSubtype` y el `count` del slot.

- Cada despliegue suma **+1** al contador del slot correspondiente (máximo x5).
- El efecto de sabotaje `RemoveOpponentUnit` resta **-1** al contador de un slot elegido por el atacante.
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
| **Colecta** | 3 📣 | Gana +8 $ |
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
| **Paro General** | Ataque | 2 $ + 3 ⚡ | Inflige 9 daño directo al oponente |
| **Abrazo Colectivo** | Defensa | 4 $ + 1 📣 | Recupera 13 HP propios |

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
| **Partida Presupuestaria** | 1 📣 | Gana +3 $ |
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
| **Operativo Especial** | Ataque | 4 $ + 2 ⚡ | Inflige 13 daño directo al oponente |
| **Escudo Antidisturbios** | Defensa | 2 $ + 3 📣 | Recupera 9 HP propios |

### Acciones — Efecto especial
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Toque de Queda** | 4 $ + 1 ⚡ | El oponente no recibe producción en su próximo turno |
| **Decreto de Emergencia** | 3 $ | El jugador recibe el doble de su producción propia en su próximo turno |

---

## Balance verificable entre facciones

| Categoría | Manifestantes | Policías |
|-----------|--------------|----------|
| Boost $ | +8 (caro) | +3 (barato) |
| Boost ⚡ | +3 (barato) | +8 (caro) |
| Boost 📣 | +7 (medio) | +4 (medio) |
| **Total boost** | **+18** | **+15** |
| Sabotaje $ | -3 (barato) | -7 (caro) |
| Sabotaje ⚡ | -7 (caro) | -3 (barato) |
| Sabotaje 📣 | -5 (medio) | -5 (medio) |
| **Total sabotaje** | **-15** | **-15** |
| Daño directo | 9 | 13 |
| Recuperación HP | 13 | 9 |
| **Daño + recuperación** | **22** | **22** |

> El boost total difiere en 3 puntos. Esto se compensa con que Policías tiene el daño directo más alto (+4) y Manifestantes la recuperación más alta (+4). Balance neto equivalente.

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

**Efectos activos:** cada jugador muestra un indicador visual si tiene `activeEffects` vigentes (ej: ícono de producción bloqueada, ícono de producción doble).

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

---

## 13. Fuera de scope (v1)

- Construcción de mazos personalizada
- Más de 2 jugadores
- Online / multijugador en red
- Animaciones elaboradas
- Progresión / desbloqueo de cartas
