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
| `effectValue` | int | Valor del efecto para acciones (daño directo, HP ganado, recursos ganados/robados). Para unidades siempre es 1 (ver §9) |
| `effectResource` | enum | Recurso afectado: Dinero / Fuerza / Social / HP / Unidad (para Productora / Boost / Sabotaje) |
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

- Cada tipo de unidad ocupa un slot con un contador (ej: `Piquetero x3`).
- Cada carta de despliegue suma **+1** al contador de esa unidad (máximo x5).
- Cartas de sabotaje enemigo restan **-1** al contador de una unidad elegida por el atacante.
- Si el contador llega a **0**, la unidad desaparece y libera el slot.

**El efecto de cada unidad es siempre de valor 1 por instancia en el contador:**

| Subtipo | Efecto pasivo por turno |
|---------|------------------------|
| **Defensiva** | Resta **-1 de daño entrante** por instancia. Con x3 → absorbe 3 de daño total antes de que llegue al HP |
| **Atacante** | Suma **+1 de daño saliente** por instancia. Con x3 → inflige 3 de daño extra al oponente por turno |
| **Productora** | Genera **+1 del recurso definido** por instancia. Con x3 → produce +3 de ese recurso por turno |

> Ejemplo de resolución: oponente tiene `Patrullero x4` (4 daño). Yo tengo `Escudo Humano x2` (absorbe 2). Daño neto = 4 - 2 = **2 de daño a mi HP**.

**Slots llenos (3/3 ocupados):** si el jugador quiere desplegar una unidad nueva, debe elegir entre:
- **Reemplazar** un slot existente (el slot se vacía y se ocupa con la nueva unidad en x1)
- **Cancelar** la jugada (la carta permanece en mano, no se gastan recursos)

---

## 9. Cartas — Manifestantes

Cobertura completa: todas las categorías disponibles para ambas facciones.

### Unidades
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Piquetero** | Atacante | 3 ⚡ + 2 $ | +1 Piquetero. Cada uno suma +1 de daño al oponente por turno |
| **Escudo Humano** | Defensiva | 2 $ + 3 ⚡ | +1 Escudo Humano. Cada uno resta -1 al daño entrante por turno |
| **Olla Popular** | Productora $ | 2 $ + 2 📣 | +1 Olla Popular. Cada una produce +1 $/turno |
| **Fogón** | Productora ⚡ | 2 ⚡ + 2 📣 | +1 Fogón. Cada uno produce +1 ⚡/turno |
| **Megáfono** | Productora 📣 | 3 📣 | +1 Megáfono. Cada uno produce +1 📣/turno |

### Acciones — Boost (ganar recursos propios)
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Colecta** | 2 📣 | Gana +5 $ inmediatamente |
| **Adrenalina** | 2 $ | Gana +5 ⚡ inmediatamente |
| **Viral en Redes** | 3 $ | Gana +5 📣 inmediatamente |

### Acciones — Sabotaje (reducir recursos del oponente)
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Saqueo** | 3 ⚡ | El oponente pierde 5 $ |
| **Agotamiento** | 3 $ | El oponente pierde 5 ⚡ |
| **Fake News** | 3 📣 | El oponente pierde 5 📣 |
| **Romper la Marcha** | 2 ⚡ + 2 📣 | -1 a una unidad del oponente (el atacante elige el slot) |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Paro General** | Ataque | 4 $ + 3 ⚡ | Inflige 10 daño directo al oponente |
| **Abrazo Colectivo** | Defensa | 3 $ + 2 📣 | Recupera 10 HP propios |

---

## 10. Cartas — Policías

### Unidades
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Patrullero** | Atacante | 3 ⚡ + 2 $ | +1 Patrullero. Cada uno suma +1 de daño al oponente por turno |
| **Comisaría** | Defensiva | 2 $ + 3 ⚡ | +1 Comisaría. Cada una resta -1 al daño entrante por turno |
| **Subsidio** | Productora $ | 2 $ + 2 📣 | +1 Subsidio. Cada uno produce +1 $/turno |
| **Entrenamiento** | Productora ⚡ | 2 ⚡ + 2 📣 | +1 Entrenamiento. Cada uno produce +1 ⚡/turno |
| **Conferencia de Prensa** | Productora 📣 | 3 📣 | +1 Conferencia. Cada una produce +1 📣/turno |

### Acciones — Boost (ganar recursos propios)
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Partida Presupuestaria** | 2 📣 | Gana +5 $ inmediatamente |
| **Refuerzo** | 2 $ | Gana +5 ⚡ inmediatamente |
| **Cadena Nacional** | 3 $ | Gana +5 📣 inmediatamente |

### Acciones — Sabotaje (reducir recursos del oponente)
| Carta | Costo | Efecto |
|-------|-------|--------|
| **Embargo** | 3 ⚡ | El oponente pierde 5 $ |
| **Detención** | 3 $ | El oponente pierde 5 ⚡ |
| **Censura** | 3 📣 | El oponente pierde 5 📣 |
| **Infiltrado** | 2 ⚡ + 2 📣 | -1 a una unidad del oponente (el atacante elige el slot) |

### Acciones — Ataque / Defensa
| Carta | Subtipo | Costo | Efecto |
|-------|---------|-------|--------|
| **Operativo Especial** | Ataque | 4 $ + 3 ⚡ | Inflige 10 daño directo al oponente |
| **Escudo Antidisturbios** | Defensa | 3 $ + 2 📣 | Recupera 10 HP propios |

---

## 11. UI / Pantalla

### 11.1 Menú principal

Pantalla de inicio con un botón central **Jugar**. Espacio reservado para futuras opciones (Ajustes, Créditos, etc.) sin implementarlas en v1.

### 11.2 Selección de facción

Tras iniciar, cada jugador elige su facción: **Manifestantes** o **Policías**.

### 11.3 Pantalla de juego

Pantalla única sin divisiones. Ambos jugadores están enfrentados horizontalmente: uno a la izquierda y otro a la derecha. El campo de batalla es el espacio central compartido.

```
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│  HP: 100                    [TURNO: MANIFESTANTES]              HP: 100│
│  $: 5  ⚡: 3  📣: 2                                    $: 5  ⚡: 3  📣: 2│
│                                                                        │
│  [Slot 1]                                                   [Slot 1]  │
│  [Slot 2]          [TORRE]              [TORRE]             [Slot 2]  │
│  [Slot 3]                                                   [Slot 3]  │
│                                                                        │
│  [c1][c2][c3]                                          [c1][c2][c3]   │
│  [c4][c5][c6]  [JUGAR] [DESCARTAR]  [JUGAR] [DESCARTAR][c4][c5][c6]  │
└────────────────────────────────────────────────────────────────────────┘
  ◄── MANIFESTANTES                                      POLICÍAS ──►
```

**Privacidad de mano:** información abierta — ambos jugadores ven todas las cartas en todo momento.

**Indicador de turno:** banner central que indica qué facción está jugando. Los botones de acción del jugador inactivo se deshabilitan durante el turno del oponente.

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
