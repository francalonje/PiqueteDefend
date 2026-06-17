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
- **Dinero, Fuerza, Social:** recursos actuales + producción por turno.
- **Mano:** X cartas visibles en pantalla en todo momento (cantidad fija, a definir).
- **Zona de unidades:** slots para unidades pasivas activas, cada una con un contador de cantidad.

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
1. PRODUCCIÓN   — El jugador activo recibe:
                    a) Producción base de sus recursos
                    b) Producción de unidades pasivas activas
                    c) Daño de unidades atacantes enemigas (tras absorción de escudos)
                  → Se evalúan condiciones de victoria aquí (ver §5)

2. ACCIÓN       — El jugador ELIGE una de dos opciones:
                    a) Jugar 1 carta (pagar su costo y aplicar su efecto)
                    b) Descartar 1 carta (sin costo, sin efecto)

3. FIN DE TURNO — Pasa el turno al oponente
```

> No hay robo de cartas. Las cartas son **siempre visibles** en pantalla. La mano es fija.

### Resolución de daño en producción

El daño de unidades atacantes enemigas se resuelve así cada turno:

```
daño_total = suma(unidades_atacantes_enemigas × valor_por_unidad)
absorción   = suma(unidades_defensivas_propias × valor_por_unidad)   ← se aplica primero
daño_neto   = max(0, daño_total - absorción)
HP_propio  -= daño_neto
```

---

## 7. Implementación técnica de cartas (Unity)

Las cartas se implementan como **ScriptableObjects** (`CardData`). Cada carta es un asset independiente con los siguientes campos:

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `id` | string | Identificador único |
| `cardName` | string | Nombre visible |
| `faction` | enum | Manifestantes / Policías |
| `cardType` | enum | Accion / Unidad |
| `subtype` | enum | Ataque / Defensa / Sabotaje / Boost / Productora / Atacante / Defensiva |
| `costDinero` | int | Costo en $ |
| `costFuerza` | int | Costo en ⚡ |
| `costSocial` | int | Costo en 📣 |
| `effectValue` | int | Valor principal del efecto (daño, absorción, producción, etc.) |
| `effectResource` | enum | Recurso afectado (para Productora/Boost/Sabotaje) |
| `sprite` | Sprite | Imagen de la carta |
| `descriptionText` | string | Texto de efecto visible en la carta |

El pool de cada facción es una lista de referencias a estos assets. El sistema de reemplazo toma una carta aleatoria del pool al jugar o descartar.

---

## 8. Sistema de cartas (sin mazo)

- No existe un mazo ni descarte.
- Cada jugador tiene **X cartas** permanentemente visibles en pantalla (cantidad a definir, ej: 6).
- Al jugar o descartar una carta, esa carta se **reemplaza automáticamente** por una nueva del pool disponible de la facción.
- El pool de cartas es el set completo de la facción — puede repetirse.

---

## 8. Tipos de carta

### 8.1 Acción (un solo uso)
Se juega, produce su efecto inmediatamente y se reemplaza por una carta nueva.

| Subtipo | Efecto |
|---------|--------|
| **Ataque** | Daña los HP del oponente |
| **Defensa** | Suma HP propios |
| **Sabotaje** | Reduce recursos o unidades del oponente |
| **Boost** | Aumenta recursos propios este turno |

### 8.2 Unidad (persistente y pasiva)
Se despliega en la zona de unidades. Es **siempre pasiva** — actúa automáticamente cada turno sin intervención del jugador.

**Las unidades funcionan con contadores de cantidad, no con HP individuales:**

- Cada tipo de unidad tiene un slot con un número (ej: `Piquetero x3`).
- Cartas positivas propias: **+1** a esa unidad (máximo x5).
- Cartas de sabotaje enemigas: **-1** a una unidad elegida por el jugador atacante.
- Si el contador llega a **0**, la unidad desaparece y libera el slot.

**Slots llenos (3/3 ocupados):** si el jugador quiere desplegar una unidad nueva, debe elegir entre:
- **Reemplazar** un slot existente (el slot se vacía y se ocupa con la nueva unidad en x1)
- **Cancelar** la jugada (la carta permanece en la mano sin gastar recursos)

| Subtipo | Efecto pasivo (por unidad en el contador) |
|---------|------------------------------------------|
| **Productora** | Genera +X de un recurso por turno |
| **Atacante** | Inflige X daño al oponente por turno |
| **Defensiva** | Absorbe X daño automáticamente antes de que llegue al HP del jugador |

---

## 9. Ejemplo de cartas (Manifestantes)

| Carta | Tipo | Costo | Efecto |
|-------|------|-------|--------|
| **Paro General** | Acción - Ataque | 4 $ + 2 ⚡ | Inflige 15 daño al oponente |
| **Olla Popular** | Unidad - Productora | 3 $ + 2 📣 | +1 Olla Popular. Cada una genera +2 $/turno y +1 📣/turno |
| **Corte de Ruta** | Acción - Sabotaje | 2 ⚡ + 3 📣 | El oponente no recibe producción en su próximo turno |
| **Escudo Humano** | Unidad - Defensiva | 2 $ + 3 ⚡ | +1 Escudo Humano. Cada uno absorbe 5 daño/turno automáticamente |
| **Viral en Redes** | Acción - Boost | 4 📣 | Gana +10 📣 inmediatamente |
| **Piquetero Duro** | Unidad - Atacante | 3 ⚡ + 2 $ | +1 Piquetero Duro. Cada uno inflige 4 daño/turno |
| **Romper la Marcha** | Acción - Sabotaje | 3 ⚡ | -1 a una unidad atacante del oponente |

---

## 10. Ejemplo de cartas (Policías)

| Carta | Tipo | Costo | Efecto |
|-------|------|-------|--------|
| **Gas Lacrimógeno** | Acción - Ataque | 3 ⚡ + 2 $ | Inflige 12 daño + reduce 2 📣 del oponente |
| **Comisaría** | Unidad - Defensiva | 4 $ + 2 ⚡ | +1 Comisaría. Cada una absorbe 6 daño/turno automáticamente |
| **Operativo Especial** | Acción - Ataque | 5 ⚡ | Inflige 20 daño al oponente |
| **Patrullero** | Unidad - Atacante | 4 $ + 1 ⚡ | +1 Patrullero. Cada uno inflige 3 daño/turno |
| **Subsidio Político** | Acción - Boost | 2 📣 | Gana +8 $ inmediatamente |
| **Infiltrado** | Acción - Sabotaje | 3 $ + 2 📣 | -1 a una unidad productora del oponente |
| **Refuerzo Policial** | Unidad - Productora | 3 $ + 1 ⚡ | +1 Refuerzo. Cada uno genera +3 ⚡/turno |

---

## 11. UI / Pantalla

### 11.1 Menú principal

Pantalla de inicio con un botón central **Jugar**. Espacio reservado para futuras opciones (Ajustes, Créditos, etc.) sin implementarlas en v1.

### 11.2 Selección de facción

Tras iniciar, cada jugador elige su facción: **Manifestantes** o **Policías**.

### 11.3 Pantalla de juego

Pantalla única compartida dividida horizontalmente en dos mitades enfrentadas. El jugador de abajo ve su mitad derecha hacia arriba; el jugador de arriba ve su mitad rotada 180°.

```
┌─────────────────────────────────────────────────────────────────┐  ▲
│  Mano: [c1][c2][c3][c4][c5][c6]              [DESCARTAR][JUGAR]│  │
│  Unidades: [Slot A] [Slot B] [Slot C]                          │  JUGADOR
│  [POLICÍAS]  HP: 100  $: 12  ⚡: 8  📣: 5                     │  ARRIBA
│═════════════════════════════════════════════════════════════════│  (rotado 180°)
│  [MANIFESTANTES]  HP: 85   $: 9   ⚡: 6  📣: 14               │
│  Unidades: [Slot A] [Slot B] [Slot C]                          │  JUGADOR
│  Mano: [c1][c2][c3][c4][c5][c6]              [DESCARTAR][JUGAR]│  ABAJO
└─────────────────────────────────────────────────────────────────┘  ▼
```

**Privacidad de mano:** las cartas propias son siempre visibles. Las cartas del oponente también son visibles (información abierta).

**Indicador de turno:** banner simple que indica de quién es el turno activo.

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
