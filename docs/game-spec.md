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

Cada facción tiene su propio mazo de cartas temático, pero comparten la misma mecánica de recursos y reglas.

---

## 3. Recursos

Cada jugador maneja 3 recursos independientes. Al inicio de cada turno **antes de jugar una carta**, el jugador recibe su producción automática.

| Recurso | Descripción | Producción inicial | Máximo |
|---------|-------------|-------------------|--------|
| **Dinero** ($) | Plata para movilizar gente y pagar logística | 5 / turno | 99 |
| **Fuerza** (⚡) | Capacidad física, represión o resistencia | 3 / turno | 99 |
| **Social** (📣) | Apoyo popular, organización, narrativa | 2 / turno | 99 |

> La producción de cada recurso puede aumentarse durante la partida mediante cartas o unidades pasivas.

---

## 4. Estado del jugador

Cada jugador tiene:

- **HP (Health Points):** 100 puntos al inicio. Llegar a 0 = derrota.
- **Dinero, Fuerza, Social:** recursos actuales + producción por turno.
- **Mano:** 5 cartas (se roba 1 al inicio de cada turno).
- **Zona de unidades:** hasta 3 unidades pasivas activas simultáneamente.

---

## 5. Condiciones de victoria

Un jugador gana si ocurre **cualquiera** de estas condiciones:

| Condición | Descripción |
|-----------|-------------|
| **KO** | Reducir los HP del oponente a 0 |
| **Hegemonía Social** | Acumular 50 puntos de Social |
| **Poder Económico** | Acumular 75 puntos de Dinero |

> Las condiciones de victoria alternativas representan "ganar la narrativa" o "comprar el conflicto".

---

## 6. Estructura de un turno

```
1. PRODUCCIÓN   — El jugador activo recibe recursos de producción + de unidades activas
2. ROBO         — Roba 1 carta (mano máxima: 7)
3. ACCIÓN       — Juega 1 carta (o pasa)
4. FIN DE TURNO — Pasa el turno al oponente
```

> Solo se puede jugar **1 carta por turno**. Posible extensión futura: cartas gratuitas (costo 0).

---

## 7. Tipos de carta

### 7.1 Acción (un solo uso)
Se juega, produce su efecto inmediatamente y se descarta.

| Subtipo | Efecto |
|---------|--------|
| **Ataque** | Daña los HP del oponente |
| **Defensa** | Suma HP propios o bloquea daño |
| **Sabotaje** | Reduce recursos del oponente |
| **Boost** | Aumenta recursos propios este turno |

### 7.2 Unidad (persistente)
Se despliega en la zona de unidades. Permanece en juego hasta que sea destruida o el jugador la retire.

| Subtipo | Efecto pasivo |
|---------|---------------|
| **Productora** | Genera +X de un recurso por turno |
| **Atacante** | Inflige X daño al oponente por turno |
| **Defensiva** | Absorbe X daño antes de que llegue al HP del jugador |

> Una unidad tiene sus propios HP. Cuando llegan a 0, la unidad se destruye.

---

## 8. Ejemplo de cartas (Manifestantes)

| Carta | Tipo | Costo | Efecto |
|-------|------|-------|--------|
| **Paro General** | Acción - Ataque | 4 $ + 2 ⚡ | Inflige 15 daño al oponente |
| **Olla Popular** | Unidad - Productora | 3 $ + 2 📣 | Genera +2 $ y +1 📣 por turno. HP: 10 |
| **Corte de Ruta** | Acción - Sabotaje | 2 ⚡ + 3 📣 | El oponente no recibe producción en su próximo turno |
| **Escudo Humano** | Unidad - Defensiva | 2 $ + 3 ⚡ | Absorbe 5 daño por turno. HP: 15 |
| **Viral en Redes** | Acción - Boost | 4 📣 | Gana +10 📣 inmediatamente |
| **Piquetero Duro** | Unidad - Atacante | 3 ⚡ + 2 $ | Inflige 4 daño por turno. HP: 12 |

---

## 9. Ejemplo de cartas (Policías)

| Carta | Tipo | Costo | Efecto |
|-------|------|-------|--------|
| **Gas Lacrimógeno** | Acción - Ataque | 3 ⚡ + 2 $ | Inflige 12 daño + reduce 2 📣 del oponente |
| **Comisaría** | Unidad - Defensiva | 4 $ + 2 ⚡ | Absorbe 6 daño por turno. HP: 18 |
| **Operativo Especial** | Acción - Ataque | 5 ⚡ | Inflige 20 daño al oponente |
| **Patrullero** | Unidad - Atacante | 4 $ + 1 ⚡ | Inflige 3 daño por turno. HP: 10 |
| **Subsidio Político** | Acción - Boost | 2 📣 | Gana +8 $ inmediatamente |
| **Infiltrado** | Acción - Sabotaje | 3 $ + 2 📣 | Roba 1 carta de la mano del oponente |

---

## 10. Mazo

- Cada jugador empieza con un mazo de **20 cartas** predefinido según su facción.
- Se mezcla al inicio de la partida.
- Si el mazo se agota, se baraja el descarte.
- Mano inicial: **5 cartas**.

---

## 11. UI / Pantalla

```
┌─────────────────────────────────────────────────────┐
│  [Policías]  HP: 100  $:5  ⚡:3  📣:2               │
│  Zona unidades: [ ][ ][ ]                           │
│─────────────────────────────────────────────────────│
│                   CAMPO DE BATALLA                  │
│─────────────────────────────────────────────────────│
│  Zona unidades: [ ][ ][ ]                           │
│  [Manifestantes]  HP: 100  $:5  ⚡:3  📣:2          │
│                                                     │
│  Mano: [c1][c2][c3][c4][c5]      [PASAR TURNO]     │
└─────────────────────────────────────────────────────┘
```

---

## 12. Fuera de scope (v1)

- Construcción de mazos personalizada
- Más de 2 jugadores
- Online / multijugador en red
- Animaciones elaboradas
- Progresión / desbloqueo de cartas

---

## 13. Pendiente de definir

- [ ] ¿Cuántas cartas tiene cada mazo completo?
- [ ] ¿Se puede atacar directamente a las unidades enemigas?
- [ ] ¿Las unidades defensivas protegen automáticamente o el jugador elige?
- [ ] ¿Hay límite de turnos? ¿Empate posible?
