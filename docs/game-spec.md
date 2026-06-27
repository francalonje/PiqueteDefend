# PiqueteDefend — Game Specification

## 1. Concepto

Juego de cartas por turnos para 2 jugadores en local (hotseat). Dos facciones enfrentadas en el contexto político-cómico argentino: **Manifestantes** vs **Policías**. Inspirado en Castle Wars, The King is Watching, Legends of Runeterra y Slay the Spire.

**Modos:** **Dos jugadores** (hotseat local, base) y **Un jugador** — una **run roguelike-deckbuilder** contra IA sobre un mapa temático (§17, en desarrollo). **[FUTURO]** Jugador vs Jugador online.

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
| **Dinero** ($) | Plata para movilizar gente y pagar logística | +1 / turno | 18 |
| **Fuerza** (⚡) | Capacidad física, represión o resistencia | +1 / turno | 18 |
| **Social** (📣) | Apoyo popular, organización, narrativa | +1 / turno | 18 |

> La producción base es **+1 de cada recurso por turno**. Encima de eso, las unidades con pasiva de producción y las cartas de Acción suman extra. Los valores de producción base son configurables desde código en un único lugar (`GameConfig`) para facilitar el balanceo.

> **Cada recurso paga una cosa (regla de oro — define el eje de build, §6.2):**
> - **$ Dinero** → **comprar unidades** (toda carta de Unidad cuesta Dinero).
> - **📣 Social** → **poderes y equipo** (toda carta de Acción y de Equipo cuesta Social).
> - **⚡ Fuerza** → **atacar** (cada ataque cuesta Fuerza **proporcional a su daño**, ver §6/§7.2:
>   `max(minAttackFuerzaCost, ceil(daño_base × attackFuerzaPerDamage))`).
>
> El **recurso de costo de una carta lo fija su tipo**, no el diseño per-card (lo per-card es el
> **monto**). Así, en cuánto producís cada recurso decidís tu plan: Dinero = cuántos cuerpos bajás,
> Social = cuánta utilidad/control tirás, Fuerza = **cuánto daño repartís por turno** (pegar fuerte
> cuesta más ⚡ que pegar flojo).

**Recursos iniciales al inicio de la partida:** 5 de cada recurso (configurable para balanceo).

**Techo de recursos (anti-atesoramiento):** el máximo es **configurable** (`maxResource`). Con el modelo **multi-carta por turno** (§6), gastar varias cartas **drena los recursos solo** — el atesoramiento ya no es el problema estructural que era con "1 carta/turno". El techo pasa a ser un anti-atesoramiento **suave** (evita acumular indefinidamente entre turnos) en vez de la palanca económica central. ⚠️ **Valor a re-derivar al rebalancear el combate (sim/playtest):** el `maxResource=18` actual estaba tuneado contra el modelo viejo; con multi-carta probablemente sube o se afloja. Ver [[feedback-playtest-driven]].

**Primer turno (regla de iniciativa, §16):** el primer jugador **sí produce** en su turno 1, pero **no puede atacar con unidades** en ese turno (sí puede desplegar/jugar cartas). Esto compensa la ventaja de la iniciativa. El segundo jugador juega normal desde su turno 1. ⚠️ **Los win-rates que validaban esta regla (~59%→~48%) son del modelo viejo (1 carta + 1 ataque); re-validar al rebalancear el combate** — con multi-carta + multi-ataque la ventaja de la iniciativa puede cambiar y la regla quizá necesite ajuste.

**Recursos negativos:** los recursos nunca bajan de 0. El exceso de reducción se descarta.

### Costo de las cartas: factor global e inflación

> ⚠️ **Reescritura por el modelo multi-carta (§6):** con varias cartas por turno, gastar drena los recursos y la "abundancia" (recursos que sobran) deja de ser el problema central que motivó ×1.2 + inflación. El **factor global ×1.2** sobrevive como ajuste fino de costo; la **inflación** conserva su rol de *presión anti-stalemate* (encarece en partidas largas y empuja al desenlace), no de anti-atesoramiento. **Magnitudes (×1.2, `inflationStartTurn`, `%/turno`) a re-derivar al rebalancear el combate por sim/playtest.**

Las dos vías siguen vigentes (con el rol reescrito arriba); nacieron con el modelo viejo (1 carta/turno) para que los recursos no sobraran:

1. **Factor económico global (×1.2):** todas las cartas cuestan un ~20% más que su costo *base de diseño* (per-card, balanceado por valor/costo). Es un bump **uniforme** que no descalibra la facción (validado por simulación: facción ~51/49). Se aplica al **hornear** los assets (es el espejo de `knobs.SHIPPED.cost_mult` en el sim); los costos de §9/§10 son los **base** y el juego los muestra ya escalados.

2. **Inflación (mecánica de juego):** a partir del medio-turno `inflationStartTurn` (default **8**), cada medio-turno las cartas cuestan **`inflationPercentPerTurn`% más, acumulativo** (default **+5%/medio-turno**). El costo efectivo de cada recurso = `ceil(costo × (1 + inflación%/100))` (redondea hacia arriba, así siempre muerde). La inflación **pega a ambos jugadores por igual** (depende sólo del nº de medio-turno), así que no desbalancea; su rol es **comerse el excedente de recursos en las partidas largas** y presionar hacia el desenlace. Sólo afecta el **costo de jugar cartas** (atacar es gratis). Un **medidor** central en pantalla aparece cuando arranca y muestra el % vigente (§11.3).

> Ambos son configurables en `GameConfig` (`inflationStartTurn`, `inflationPercentPerTurn`) y en `knobs.py` del sim. Valores en revisión por playtest.

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

**Inflación (presión económica, complementaria):**
- A partir del medio-turno `inflationStartTurn` (default **8**) las cartas se encarecen progresivamente (§3). No mata unidades como la muerte súbita, pero **seca la economía** de las partidas largas (donde los recursos sobran), empujando a resolver por combate antes de llegar a la muerte súbita.

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
                       (SkipProduction/DoubleProduction) y se elimina (fire-on-expiry).
                    b) Estados por UNIDAD (§7.7): Poison hace su daño AHORA. (El counter de los
                       estados por unidad NO se decrementa acá: se decrementa en FIN DE TURNO,
                       ver nota de timing abajo, para que un Stun de 1 turno no expire antes de
                       la fase de ACCIÓN.) Furia/Desmoralizar/Stun siguen activos mientras counter>0.
                    c) Pasivas de inicio de turno (§7.3): Regeneration (cura), TurnDamage y
                       TurnStatus (daño/estado a los slots objetivo según su targeting).
                  → Evaluar condición de victoria (Poison / TurnDamage pueden matar)

2. PRODUCCIÓN   — Si skipProduction no está activo (incluye el turno 1 del primer jugador):
                    a) Producción base: +1 de cada recurso (GameConfig)
                    b) Producción de unidades con efecto pasivo de producción
                    c) Multiplicar por productionMultiplier (default 1; doble si hay DoubleProduction)
                  → Evaluar condición de victoria

3. ACCIÓN       — El jugador actúa libremente, en cualquier orden, hasta terminar el turno:
                    a) Jugar CUALQUIER cantidad de cartas que pueda pagar (cada una cuesta su
                       recurso, ya ajustado por el factor global e inflación vigente, §3).
                       Gastar varias cartas drena los recursos del turno: la economía se
                       autolimita (es el reemplazo del viejo "1 carta/turno"). Descartar una
                       carta para ciclar sigue disponible.
                    b) Atacar: CADA unidad propia que NO esté aturdida (Stun) puede atacar UNA
                       vez este turno, en el orden que elija el jugador → afecta slots según su
                       ataque. CADA ataque CUESTA ⚡ Fuerza PROPORCIONAL a su daño (§3): los pegadores
                       fuertes cuestan más, los chiquitos siguen baratos. Sólo podés atacar si te
                       alcanza, y se descuenta al atacar. Esto hace que la Fuerza module CUÁNTO daño
                       repartís por turno. Daño efectivo = base + Furia + AuraDamage − Desmoralizar;
                       los defensores con Retaliate devuelven daño al atacante.
                       EXCEPCIÓN: en el turno 1 de la partida el primer jugador NO puede atacar
                       con unidades (regla de iniciativa, §3/§16); sí puede jugar/desplegar cartas.
                       CURAR con un healer usa la acción de ataque de la unidad (§7.2): cuenta como
                       su ataque del turno y también paga su costo de ⚡.
                  → Evaluar condición de victoria tras cada acción

4. REPONER MANO — Las cartas jugadas/descartadas van al descarte; se rellena la mano hasta
                  `handSize` robando del tope del mazo barajado (sin reemplazo; rebaraja el
                  descarte si el mazo se vacía) (§8.1). [Regla rough, tunear por playtest:
                  rellenar a mano llena al fin del turno; alternativa = robar N fijo.]

5. FIN DE TURNO — a) Decrementar el counter de los estados por UNIDAD del jugador activo
                     (Poison/Stun/Furia/Desmoralizar); los que llegan a 0 se eliminan.
                  b) Si turno ≥ suddenDeathStart: todas las unidades de ambos jugadores
                     reciben 1 de daño (ignora defensas)
                  → Evaluar condición de victoria
                  → Pasa el turno al oponente (o termina si turno == maxTurns)
```

> **Nota de timing de estados por unidad (decisión de diseño validada en el sim).** Los estados
> por unidad usan modelo *active-while-present*: el **counter se decrementa al FIN del turno de su
> dueño**, no en EFECTOS. Poison hace su daño en EFECTOS (inicio) y también decrementa al final.
> Así `counter` = nº de turnos del dueño que el estado está activo, y un Stun de 1 turno aplicado
> por el rival sigue activo durante la fase de ACCIÓN del dueño antes de expirar. Los estados de
> JUGADOR (producción) siguen el modelo *fire-on-expiry* (decrementan en EFECTOS, disparan al llegar a 0).

### Resolución de ataque de unidad

El jugador selecciona una unidad propia con ataque disponible. El ataque (`UnitAttack`, §7.2) define **a quién pega** y **cuánto**. El targeting está **anclado a la formación del objetivo** (no a slots fijos): la posición del **atacante NO influye**, sólo la formación enemiga.

El conjunto de objetivos se resuelve según `mode` (`TargetMode`) y `count`:

- **`Frontmost`** — la unidad **más adelantada** ocupada del rival (la más cercana, índice mayor) más los `count-1` espacios consecutivos hacia el fondo. `count=1` = "al de adelante de todo" (**nunca whiffea**); `count>1` = **penetrante** (los espacios profundos vacíos whiffean).
- **`Backmost`** — la unidad **más atrasada** ocupada más los `count-1` hacia el frente (excepción "pega al fondo").
- **`Any`** — el jugador **elige `count`** unidades cualesquiera (snipe; reservado a cartas de acción y unidades premium).
- **`All`** — todas las unidades del objetivo (AoE).

El daño/cura (`damagePerSlot`) se aplica a la unidad de cada objetivo. **Si un objetivo profundo está vacío, ese golpe se desperdicia (whiff)** — no se redirige. Si el HP llega a 0, la unidad muere y el slot queda libre.

> **Garantía anti-deadlock (§5.1):** `Frontmost`/`Backmost`/`All`/`Any` siempre resuelven a ≥1 unidad **ocupada** si el tablero objetivo no está vacío. Por eso **un ataque de daño nunca se cancela** contra un rival con unidades: siempre conecta al menos en una. El whiff sólo existe en los alcances profundos (`count>1`).

> **Objetivo válido obligatorio:** la acción se cancela (sin gastar) si no afecta a ningún objetivo válido. Para **daño** esto sólo pasa si el rival no tiene unidades (la partida ya terminó); para **cura**, si no hay ningún aliado por debajo de su maxHp.

> **La posición es decisión defensiva:** quién pongas más adelante es quien **tankea** (se come el melee/penetrante); las frágiles atrás quedan protegidas hasta que caiga el frente, salvo del snipe (`Any`) y del "pega al fondo" (`Backmost`).

**[FUTURO]** Cada unidad podría tener múltiples poderes (`List<UnitAttack>`) con distintos modos.

### Geometría de combate (frente / fondo)

El tablero de cada jugador es **una única línea de 6 slots**, un **eje de profundidad**: el **frente** (cerca del rival) es el extremo de índice alto; el **fondo** (lejos del rival), el de índice bajo.

> **Numeración:** slots **1–6 cara al usuario**, **0–5 en código** (slot `k` del spec = índice `k-1`). Frente = índices altos `{3,4,5}`; fondo/retaguardia = `{0,1,2}`.

- **"El de adelante de todo"** = la primera unidad ocupada contando desde el frente (saltea huecos). Es a quien pega el melee (`Frontmost count=1`).
- **`allowedSlots`** sigue siendo data por unidad (`int[]`): "esta unidad requiere tal espacio" (un muro obligado al frente para tankear, una productora al fondo para esconderse). La Escaramuza y la Productora iniciales arrancan en la retaguardia; el Muro inicial, **adelante de todo** (slot 6, §11.3).
- La posición del atacante **no** cambia a quién pega (el targeting mira la formación enemiga). Posicionar es **100% defensivo**: a quién exponés adelante y a quién protegés atrás.

### Catálogo de modos de targeting

`UnitAttack` y las pasivas dirigidas usan un enum **cerrado** (`TargetMode`) + `count`. El mismo vocabulario aplica a ataques, **curas** (sobre el tablero propio), **pasivas dirigidas** (§7.3) y se reusa el concepto en **efectos de carta** (§7.6).

| `TargetMode` | `count` | Efecto | Keyword |
|--------------|---------|--------|---------|
| `Frontmost` | 1 | la unidad más adelantada ocupada | Melee |
| `Frontmost` | N>1 | las N más adelantadas (penetra; whiff en profundidad vacía) | Penetrante / Cleave |
| `Backmost` | 1 / N | la(s) más atrasada(s) ocupada(s) | Pega al fondo (excepción) |
| `Any` | N | N unidades a elección | Snipe (premium) |
| `All` | — | todas las unidades del objetivo | AoE |
| `Adjacent` | — | las vecinas (±1) de la unidad fuente | Aura (pasiva) |
| `Self` | — | la propia unidad fuente | Regen / Espinas (pasiva) |

> En **ataques** con `mode=Any` el jugador elige (`count` clicks). En **pasivas** (automáticas) el motor resuelve de forma determinista. **Zonas de deploy** (`allowedSlots`): `{}`=cualquiera, fondo `{1,2,3}`, frente `{4,5,6}`, o cualquier subconjunto de slots.

> **Heurística de balance** (se valida por simulación, Fase 5): `daño_total = damagePerSlot × slots_que_pega`. A más slots golpeados o más HP, menos daño por golpe; a menos HP, más daño. Guía: `valor ≈ HP/4 + daño_total/2 + flexibilidad_de_deploy + valor_pasiva`; el costo de la carta sigue a ese valor.
>
> **Principio vainilla:** una **pasiva** (o una acción de utilidad como **curar**) suma al `valor`. Una unidad **vainilla** —sin pasiva y con ataque de daño normal— recupera ese presupuesto como **+HP o +daño**: no debería existir una unidad que no aporte nada extra y además pegue/aguante como una que sí.

### 6.1 Principios de diseño de cartas

> La **constitución del balance**: la guía que debe respetar cada carta del pool. La heurística de
> arriba dice *cómo se valora* una carta; estos principios dicen *qué queremos que el pool produzca*:
> balance verificable, sensación de piedra-papel-tijera y un meta de builds emergentes. Cualquier
> carta nueva o re-balanceada se justifica contra ellos.

**Stats / combate**

1. **Presupuesto sobre una curva.** Cada unidad reparte un presupuesto entre HP, daño y utilidad.
   Más HP ⇒ menos daño. La utilidad (pasiva, cura) **se paga con stats** (principio vainilla): una
   unidad sin pasiva debe pegar/aguantar más que una con utilidad equivalente.
2. **Gradiente por posición: atrás pega, adelante aguanta.** Como norma, las unidades que se
   despliegan en **retaguardia pegan más** (carrys, snipers, morteros: frágiles que necesitan estar
   protegidos) y las de **vanguardia aguantan más** (muros/tanques que comen el melee). El
   presupuesto del punto 1 se inclina a **daño** cuanto más atrás puede ir la unidad y a **HP**
   cuanto más al frente. Esto refuerza el RPS de formación: el alcance que saltea posición (punto 6)
   es premium justamente porque rompe este gradiente (llega al carry escondido atrás).
3. **Stats baratos en bulk, acciones caras.** El poder numérico es **exponencialmente** más caro.
   Corolario buscado: dos unidades baratas superan en stats crudos a una cara; lo que compra la cara
   es **alcance + economía de acción** (mata en 1 acción lo que dos baratas en 2). Esa tensión es
   deseada.
4. **Las unidades son la carta central.** Acciones y equipo **orbitan** a las unidades (las
   habilitan, protegen, castigan); nunca son el motor del juego por sí solas.

**Targeting / RPS de formación**

5. **Cada modo de targeting tiene una formación que lo castiga** (triángulo del §6):
   - Melee/Penetra (`Frontmost`) ← muro gordo adelante + frágiles atrás.
   - Muro + carry escondido ← snipe (`Any`) y "pega al fondo" (`Backmost`).
   - Snipe (snipers frágiles y caros) ← agro de cuerpos baratos.
   - Ir ancho (swarm) ← AoE (`All`).

   El pool de **cada facción** debe cubrir **cada esquina**; ninguna formación gana contra todas.
6. **El alcance es el recurso premium.** `Frontmost ×1` es gratis/default; `Any`/`Backmost`/`All`/
   `Penetra` cuestan caro **porque saltean la posición** (única palanca defensiva, §6).
7. **No saturar de snipe/AoE.** Posicionar es la counterplay del defensor; si medio pool ignora la
   formación, muere el eje de profundidad.

   > **Excepciones de alcance — la máxima (operacionaliza #6/#7/#13):** pegar al frente es la regla;
   > la **excepción** es pegar *más allá* del frente, y viene en **sabores**: **penetrar** (`Frontmost ×N`,
   > atraviesa al muro), **saltar al fondo** (`Backmost`), **elegir blanco** (`Any`) y **caer sobre todos**
   > (`All`). Que haya **varias por facción, repartidas en posición / arquetipo / recurso, con punch
   > mixto**: algunas llegan por **daño** (sniper), otras por **utilidad** (chip / estado / AoE a la línea
   > de atrás, sin pegar fuerte — una excepción NO tiene que ser punzante). **Pocas en total (~4 de 9):
   > el muro sigue tapando a los cuerpos** (#7) — ni tan poco que siempre pegues al muro sin decidir, ni
   > tanto que el muro deje de importar. **Sets concretos en §9/§10.**

**Economía / tempo**

8. **El recurso es la moneda real.** Con **varias cartas por turno limitadas por recursos** (§6), se
   valora por **valor-por-recurso** (cuánto impacto comprás con tu producción del turno), no por stats
   crudos ni por "valor-por-acción". La producción es **tempo diferido** ⇒ debe ser **castigable por el
   agro** (invertir en economía te deja con menos cartas jugables ahora). RPS estratégico: *agro >
   codicia (economía) > midrange > agro*.
9. **La inversión debe ser vulnerable.** Unidad cara/equipada/buffeada = huevos en una canasta ⇒
   snipe/remoción/desmoralizar la castigan; el AoE castiga ir ancho. Sin counters no hay meta, hay
   dominante.

**Meta / builds**

10. **La sinergia vive en pasivas + posición + economía, no en stats.** Sin deckbuilding (v1), el
    "build" es la **formación + el lean de recurso** (aura sobre adyacentes, muro+carry, eco+healer).
    El meta son *formaciones y planes económicos* emergentes, no listas.
11. **Balance simétrico, no espejado.** Las facciones se equilibran en **poder agregado**
    (win-rate del sim ≈ 50/50), **no clonándose**: cada una puede tener arquetipos, pasivas y
    lineups propios y divergentes. La simetría es de *fuerza*, no de *forma* — la asimetría da
    identidad y matchups más ricos. El eje recurso=build (§6.2) sí vale en ambas. (Ej.: el
    **Jubilado** mártir de Manifestantes, §9, no tiene equivalente espejado en Policías.)

**Flavor / tono**

12. **Tono de nombre y descripción.** Humor político y de cultura popular argentina: **negro,
    bizarro, ácido**, en **dialecto argentino** (voseo, lunfardo). El nombre y la descripción
    reflejan los **stats/rol reales** de la carta (un tanque suena duro; un frágil de sacrificio,
    patético-épico; un sniper, frío y distante).

**Excepciones (regla sobre las reglas)**

13. **Toda regla tiene su excepción encarnada.** Cada máxima de esta sección debe tener **al menos
    una unidad** del pool que sea una **excepción clara y deliberada** a ella (y, si conviene, varias
    —incluso una por facción). Las excepciones son **contenido de diseño, no descuidos**: son las
    cartas que generan sorpresa, counters y techo de habilidad, y que evitan que el pool se sienta
    una plantilla. La excepción **paga su ruptura** (un costo, un downside o un nicho estrecho) para
    no romper el balance agregado del punto 11. Ejemplos de rupturas buscadas: al gradiente de
    posición (#2), una unidad pesada que **pega fuerte desde la vanguardia** o un frágil de
    retaguardia **puramente defensivo**; al principio vainilla (#1), una unidad icónica
    **sobrecargada de utilidad** a cambio de stats castigados. Al crear/rebalancear una carta,
    anotá explícitamente **qué regla rompe** y **cómo la paga**.

> **Proceso:** `docs/game-spec.md` + `Core/CardLibrary.cs` + `sim/` se mueven juntos
> (`py sim/parity_check.py`). Valores rough validados por feel de playtest, no sobre-tune en el sim.

### 6.2 Eje de build: recurso → arquetipo

La palanca central de la "sensación de builds". El **recurso de costo lo fija el tipo de carta/acción**
(§3), así que *leanear un recurso* es **en qué recurso invertís producción** (productoras + boosts) y,
por lo tanto, **qué parte de tu plan podés sostener**. El build es **emergente de la economía**:

| Recurso | Paga | Si te sobra / si te falta |
|---------|------|---------------------------|
| **$ Dinero** | **Unidades** (cuerpos en el tablero) | mucho $ = inundás de unidades; poco $ = tablero ralo |
| **📣 Social** | **Poderes (acciones) + Equipo** (utilidad, control, buffs) | mucho 📣 = control/combos; poco 📣 = sólo pegar a lo bruto |
| **⚡ Fuerza** | **Atacar** (`attackFuerzaCost` por golpe, §6) | mucho ⚡ = atacás con todo el tablero; poco ⚡ = elegís a quién golpear |

> **Por qué funciona.** Antes el costo era per-card; ahora cada recurso es un **dial de plan**: subir
> producción de ⚡ = más agresión por turno, de $ = más presencia, de 📣 = más utilidad/control. La
> tensión: producís +1 de cada (§3) y boosts/productoras, pero no alcanza para maximizar las tres a la
> vez. **Asimetría de facción (§6.1 #11):** Manif y Pol invierten distinto (p. ej. Pol con más control
> en 📣, Manif con más cuerpos baratos en $), no por el recurso sino por **qué cartas** ofrece cada pool.

> **Producción e ingreso por recurso.** Base = +1 de cada recurso/turno (§3). Una **productora** suma
> **+2** de su recurso. Las cartas de boost (acciones que dan recurso) convierten/aceleran un eje. ⚠️
> **Montos a re-derivar al rebalancear el combate** (sim/playtest): el cambio a costo-por-tipo +
> ataque-cuesta-⚡ corrió toda la economía.

> **Propiedad emergente buscada:** mono-recurso = rápido y enfocado **pero ciego a parte del
> triángulo RPS** (te falta acceso fiable a tu counter); mixto = flexible pero más lento. Esa es la
> tensión de build. La producción base (+1 de cada recurso) permite *splashear* el counter ocasional.

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
| `mode` | TargetMode | A quién pega, anclado a la formación (§6): `Frontmost` / `Backmost` / `Any` / `All` (default `Frontmost`) |
| `count` | int | **Objetivos**: profundidad/alcance (`Frontmost`/`Backmost`) o cuántas elegir (`Any`); ignorado en `All` |
| `hits` | int | **Golpes por objetivo** (multi-hit): el daño/cura se aplica `hits` veces al MISMO objetivo, en golpes separados. Default 1. Reparte el daño total en pegues más chicos: mantiene el total pero **dispara las reacciones por golpe** (Espinas/`Retaliate` y Blindaje cuentan por cada hit). `≤0` se trata como 1 |
| `damagePerSlot` | int | Magnitud **por golpe**: **daño** si `effect=DamageEnemies`, **curación** si `effect=HealAllies` (tope = maxHp) |
| `effect` | AttackEffect | `DamageEnemies` (default) = afecta el tablero rival / `HealAllies` = afecta el tablero propio curando |

**TargetMode** enum: `Frontmost`, `Backmost`, `Any`, `All`, `Adjacent`, `Self` (§6).
**AttackEffect** enum: `DamageEnemies`, `HealAllies` *(extensible: p. ej. `BuffAllies` a futuro)*

El tablero objetivo lo decide `effect` (rival para daño, propio para cura). El **vocabulario de modos del §6 aplica igual a curaciones** (`Frontmost`/`Any` sobre el tablero propio). Un objetivo profundo vacío = whiff; curar a una unidad ya en maxHp se desperdicia. `requires_choice` ⟺ `mode = Any`.

> **`count` (objetivos) vs `hits` (golpes) — son ejes distintos:** `count` es a **cuántas unidades** llega el ataque (penetrar/snipear); `hits` es **cuántas veces pega a CADA una**. Ej.: el **Piquetero** (`Frontmost count=1, hits=2, 7`) pega **dos golpes de 7** al de adelante (14 total, **un solo click**), y por eso le come **Espinas dos veces**; el **Encadenado** (`Frontmost count=2, hits=1, 3`) pega **3 a las 2 unidades de adelante**. El **costo en ⚡** es proporcional al **daño total por objetivo** (`damagePerSlot × hits`), así repartirlo en multi-hit no lo abarata.

**[FUTURO]** → `List<UnitAttack>` para múltiples poderes por unidad con distintos modos.

---

### 7.3 PassiveEffect

Un pasivo puede **producir recursos, curar/buffear aliadas o dañar/debuffear enemigas**. Las pasivas dirigidas usan **el mismo targeting que un ataque** (§6): `mode` + `count` sobre el tablero indicado por `target`.

| Campo | Tipo | Descripción |
|-------|------|-------------|
| `passiveType` | PassiveType | Qué hace + cuándo (ver tabla) |
| `value` | int | Magnitud (daño, cura, recurso, +daño de aura) |
| `resource` | ResourceType | Recurso afectado (sólo `ProduceResource`) |
| `status` | StatusEffect | Plantilla a aplicar (sólo `TurnStatus`) |
| `target` | PassiveTarget | `Self` / `Allies` / `Enemies` — sobre qué tablero recae |
| `mode` | TargetMode | Targeting igual que `UnitAttack` (`Self` lo ignora) |
| `count` | int | Profundidad/alcance o cantidad (según `mode`) |

**PassiveType** enum:

| Tipo | Timing | Efecto |
|------|--------|--------|
| `ProduceResource` | Inicio de turno (PRODUCCIÓN) | +`value` de `resource` al dueño |
| `Regeneration` | Inicio de turno del dueño | Cura `value` HP (target `Self` por default; puede curar aliadas con patrón) |
| `AuraDamage` | Continuo (al resolver el ataque de la aliada) | +`value` daño a las aliadas del `target`/`mode` (adyacentes = `Adjacent`) |
| `Retaliate` | Reactivo, al ser golpeada por un **ataque de unidad** | El atacante recibe `value` de daño |
| `TurnDamage` | Inicio de turno del dueño | `value` daño a los objetivos (típico `Enemies`, `mode=Frontmost` = vanguardia) |
| `TurnStatus` | Inicio de turno del dueño | Aplica `status` a los slots objetivo (`Enemies` o `Allies`) |
| `OnDeath` | Al morir la unidad (cualquier fuente) | Death-rattle: con `status` aplica el estado a los objetivos (`target`/`mode`/`count`); sin `status`, `value` de daño directo. Ej.: Jubilado → Furia a aliados adyacentes |
| `Armor` (Blindaje) | Reactivo, al recibir un **ataque de unidad** | Reduce el daño del golpe en `value` (piso 0). NO mitiga daño directo (Poison/TurnDamage/ModifyHP/OnDeath) ni muerte súbita |
| `PushBack` (Chorro) | Continuo, tras el ataque de la portadora | Empuja a cada objetivo sobreviviente al slot libre más al fondo de su formación (no-op si no hay lugar); ignora `allowedSlots` |

**PassiveTarget** enum: `Self`, `Allies`, `Enemies`. *(La adyacencia se expresa con `mode = Adjacent` sobre `Allies`.)*

Reglas: las auras **se suman** y no se aplican a sí mismas. `Retaliate` sólo responde a ataques de unidad (no a daño directo de cartas, pasivas ni muerte súbita) y **dispara aunque la unidad muera** (re-evaluar KO). `TurnDamage`/`TurnStatus` se resuelven al inicio del turno del dueño y respetan whiff en slots vacíos. El daño/estado de pasivas **no** dispara `Retaliate` (no es ataque de unidad). `OnDeath` dispara al llegar a 0 HP por **cualquier** fuente, antes de liberar el slot, y puede encadenar más muertes (re-evaluar KO); `RemoveUnit` (remoción directa) **no** lo dispara. `Armor` mitiga sólo el daño de **ataques de unidad** (no el directo ni la muerte súbita, que ignora defensas, §5.1). `PushBack` reposiciona tras aplicar el daño y antes del `Retaliate`.

> **Targeting de pasivas:** como la pasiva es **automática** (no hay elección humana), `mode=Frontmost`/`Backmost` resuelven anclados a la formación (deterministas; espejo exacto en el sim). Ej.: el **Gas** del Gasero (`Frontmost count=1`) envenena **a la unidad más adelantada** del rival; el **Humo** (`Frontmost count=3`) daña a las 3 de vanguardia.

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

**`IPlayerController` (PLANIFICADO — single-player, §17):** abstracción de "controlador de jugador" (impl `Human` y `AI`) para que el motor consulte las acciones del turno sin conocer la escena. Habilita IA y, a futuro, multijugador online, sin modificar `GameEngine`. La IA porta la heurística de `sim/policy.py` (§16) adaptada al turno multi-acción.

**Deckbuilding (IMPLEMENTADO — single-player, §17):** vía `PlayerSetup.deck` + `StartGame(PlayerSetup, PlayerSetup, firstIndex)` el motor recibe el **mazo del jugador** inyectado (mazo de la run) en vez de derivarlo de la facción. Con `PlayerSetup.ForFaction` cae al mazo del catálogo (hotseat). `PlayerSetup` también lleva los handicaps de dificultad (recursos/unidades iniciales extra, §17.1) y `initialStatuses` (estados sembrados al iniciar — único seam de motor para reliquias y pasivas de jefe, §17.4/§17.5; ruteo igual que `ApplyStatus`: de jugador→`activeStatuses`, por-unidad→unidades desplegadas). Punto de inyección para arquetipos de enemigo, reliquias y mazos configurables (tienda, armado pre-run).

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

- Cada jugador tiene un **mazo barajado** del que roba **sin reemplazo**. En **hotseat** es el mazo de la facción (Unidades, Acciones, Equipo); en **single-player** es el **mazo de la run** (starter + recompensas, §17), inyectado en el motor.
- Al inicio: el mazo se baraja y se roban 6 cartas en mano.
- Como el turno permite **jugar/descartar varias cartas** (§6), al **final del turno** la mano se **rellena hasta `handSize`** robando del tope del mazo (no "1 por carta jugada").
- Cuando el mazo se vacía, el **descarte se baraja y vuelve a ser el mazo** (las cartas circulan; nada se pierde). Las 3 unidades iniciales se despliegan gratis **y** su copia también está en el mazo.
- **Composición del mazo:** cada carta aparece `drawWeight` (int en `CardData`) veces (= **nº de copias**). **Copias actuales** (protagonismo de producción sin tapar la mano de unidades que no se pueden bajar): **productoras y cartas de producción (boost de recurso propio) = 2**, todo lo demás (unidades comunes, acciones, equipo) = **1** → ~27 cartas/facción. Se derivan por tipo en el catálogo (`CardCatalog.GetDeckList` / `cards.build_deck`), no carta por carta. El mazo finito **garantiza el acceso** (las cartas de 1 copia salen seguro en partidas largas — el sniper deja de "no salir nunca") y **auto-corrige el clumping** (lo robado sale del mazo). *Nota: con 3 unidades iniciales el tablero se llena rápido; subir copias de unidades comunes tapa la mano, así que se dejan en 1.* **[FUTURO]** deckbuilding: decklist explícita por jugador. Más afinado por simulación/playtest.

### 8.2 Acción (un solo uso)

Se juega, se resuelven todos sus `CardEffect` en orden, va al descarte y se roba una del mazo para reponer.

### 8.3 Unidad (persistente)

Se despliega en un slot **permitido** (`allowedSlots`; vacío = cualquiera) que esté **libre**. **No hay reemplazo: no se puede desplegar sobre una unidad ya presente** (es un objetivo inválido, tanto al clickear como al soltar en drag&drop). Si no hay ningún slot permitido libre, la unidad **no se puede jugar** (la carta queda en la mano, sin gastar recursos). Si hay varios slots libres permitidos y el jugador no elige uno (p. ej. soltó en JUGAR), el motor toma el de menor índice.

No hay apilamiento activo: un slot contiene una unidad. **[FUTURO]** El modelo reserva un punto de extensión para apilamiento (`UnitSlot.count`, §7.4), hoy inactivo.

### 8.4 Equipo

Una carta de Equipo (`EquipmentCardData`, §7.1) se juega **sobre una unidad propia** (se arrastra sobre el slot, §11.4): le suma `statModifiers` (+maxHp, +daño) y/o `grantedPassives`. El efecto dura **hasta que la unidad muere** (o es reemplazada): ahí el equipo se destruye con ella (no vuelve a la mano ni al pool). Una unidad puede acumular varios equipos (`UnitSlot.attachedEquipment`); los modificadores se suman en la **capa de stats efectivos** (§7.4). **[DEFINIR]** tope de equipos por unidad (hoy sin límite). Catálogo concreto en §9/§10.

---

## 9. Cartas — Manifestantes

> **Identidad (asimétrica, §6.1 #11):** los Manifestantes ganan con **cantidad, aguante popular,
> martirio y momentum** — buffean a los suyos, se sacrifican y aguantan. Sus pasivas distintivas
> (no presentes en Policías): **Aura +daño**, **Espinas**, **OnDeath-mártir** (Furia), **Cura**,
> **Mortero al fondo** (`Backmost`) y **Humo** (TurnDamage `All`). Su economía de ⚡ **gotea** por
> combate (Fisura +1⚡). Su único control duro es el **Escrache** (Stun); el resto es buff/economía/daño.
>
> **Excepciones de alcance (§6.1, pegan más allá del frente):** Fisura (penetra ×3), **Mortero** (al
> fondo, fuerte), Quema (snipe, utilidad + Humo `All`), **Jubilado** (snipe mártir, débil) + Choripanero
> (cura `Any` a aliadas). Repartidas en posición/arquetipo, punch mixto. El resto pega al frente (el muro
> tapa a los cuerpos). Manif salta al fondo con el mortero; su AoE es el Humo pasivo, no un ataque `All`.
>
> **Valores rough** (anclas de diseño, §6.1): pendientes de validar por sim/playtest — el sim/`valuation.py`
> afina los **costos** (curva super-lineal) y el balance afina HP/daño/pasivas. Encima, el juego aplica
> el factor económico global y la inflación (§3). Notación (targeting anclado a la formación, §6):
> `Adelante` = más adelantada del rival (`Frontmost ×1`); `Penetra ×N` = las N más adelantadas (whiff
> en profundidad vacía); `Al fondo` = la más atrasada (`Backmost`); `Snipe` = elige cualquiera (`Any`);
> `Todos` = AoE (`All`); `cura X` = cura HP a aliadas (`HealAllies`); `×N golpes` = multi-hit, pega N veces
> al MISMO objetivo (§7.2). `Todos` = AoE (`All`); Pasivas: §7.3.

### Unidades (9)
| Carta | Costo | Arquetipo | maxHp | Deploy | Ataque · daño | Pasiva | Descripción |
|-------|-------|-----------|-------|--------|---------------|--------|-------------|
| **Piquetero** | 4 ⚡ | Escaramuza | 20 | Cualquiera | Adelante · 7 ×2 golpes | Aura +2 daño (adyac.) | *Bombo, bandera y aguante para parar el país. El camionero lo putea en seis idiomas y él ni se inmuta.* |
| **Fisura** | 5 ⚡ | Cleave | 18 | {2,3,4,5} | Penetra ×3 · 7 | +1 ⚡/turno | *Arranca la baldosa con las manos y la parte en cuatro. Cada cascote ya tiene nombre y apellido.* |
| **Jubilado** | 2 ⚡ | Mártir | 6 | Cualquiera | Snipe · 2 | OnDeath: Furia (+4, 2t) a aliados adyacentes | *Mil miércoles de marcha en el lomo y cero miedo a esta altura del partido. Cuando cae, la columna redobla el bombo y sale con todo.* |
| **Mortero Casero** | 5 ⚡ | Morterista | 8 | {2,3,4} | Al fondo · 14 | — | *Un caño, pólvora trucha y fe. No le apunta a nadie, pero siempre le encaja al de la oficina del fondo.* |
| **Encadenado** | 5 $ | Muro | 32 | Frente {4,5,6} | Penetra ×2 · 3 | Espinas 3 (Retaliate) | *Se candó al obelisco a las seis de la mañana y tiró la llave. De ahí no lo saca nadie, y el que lo intenta se lleva los candados de recuerdo.* |
| **Gordo Sindical** | 3 $ | Productora | 12 | Retaguardia {1,2,3} | Adelante · 3 | +2 $/turno | *Maneja la caja, la lista y el micro. Aparece en el palco, jamás en la primera fila.* |
| **Choripanero** | 4 $ | Healer | 15 | {2,3,4,5} | Cura (snipe) · 3 | — | *Pan, chori y un chimi que resucita muertos. El que morfa, vuelve a la marcha como si nada.* |
| **Tuitero Militante** | 2 📣 | Productora | 10 | Retaguardia {1,2,3} | Adelante · 2 | +2 📣/turno | *2.300 seguidores y la certeza absoluta de que cambió la historia con un hilo de Twitter.* |
| **Quema de Cubiertas** | 5 📣 | Emisor | 15 | {2,3,4,5} | Snipe · 2 | Humo: 2 daño/turno a **todo** el rival (`All`) | *Diez gomas viejas, un fósforo y el viento a favor. El humo negro no discrimina: te entra a todos.* |

### Acciones (8)
| Carta | Categoría | Costo | Efecto | Descripción |
|-------|-----------|-------|--------|-------------|
| **Colecta** | Boost | 3 📣 | +6 $ propio | *Pasamos la gorra. La de los compañeros, no la de la cana.* |
| **Fernet con Cola** | Boost | 1 $ | +3 ⚡ propio | *Hidratación táctica. No es doping si lo toma toda la marcha.* |
| **Viral en Redes** | Boost | 2 $ | +7 📣 propio | *Catorce segundos de video, tres palos de reproducciones. El ministerio ya está llamando.* |
| **Paro General** | Ataque | 5 ⚡ | 21 daño directo a una unidad enemiga | *24 horas de nada. No hay bondi, no hay banco, no hay delivery. El país clavado.* |
| **El Aguante** | Buff | 2 ⚡ | Furia (+4 daño, 2 turnos) a una unidad propia | *Cantito, bombo y se renueva el aguante. Treinta cuadras más, fácil.* |
| **Asamblea Popular** | Especial | 6 📣 | Doble producción propia el próximo turno | *Se vota a mano alzada. Cuatro horas de bardo, pero esta vez salió.* |
| **Abrazo Colectivo** | Defensa | 5 $ | +10 HP a una unidad propia | *El abrazo que cura todo. Menos la deuda en pesos.* |
| **Escrache** | Control | 4 📣 | Aturde 1 turno a una unidad enemiga | *Le golpean la puerta a las 7 de la mañana con bombos. No se asoma en todo el día.* |

### Equipamiento (4)
> Se juega sobre una unidad propia; dura hasta que la unidad muere (§8.4). +HP/+daño son básicos
> (compartidos con Policías); los *grants* de pasiva (Regeneración, Espinas) son exclusivos de Manif.

| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Pechera de Cartón** | 3 $ | +10 maxHp | *Cartón, cinta de embalar y fe. Aguanta más de lo que el sentido común permite.* |
| **Cascote** | 2 ⚡ | +4 daño | *El fierro más democrático: gratis, abundante y siempre a mano.* |
| **Parrilla Portátil** | 3 $ | Otorga Regeneración (+2 HP/turno) | *Media parrilla, una bolsa de carbón y olor a asado. Cura lo que ninguna obra social.* |
| **Miguelitos** | 3 ⚡ | Otorga Espinas 4 (Retaliate) | *Tres clavos soldados con saña. El patrullero los encuentra tarde, siempre.* |

---

## 10. Cartas — Policías

> **Identidad (asimétrica, §6.1 #11):** los Policías ganan con **precisión, control, equipo y plata**
> — cuerpos vainilla potentes, snipe quirúrgico y supresión. Sus rasgos distintivos (no presentes en
> Manif): **Snipe preciso** (`Any`), **Blindaje** (mitiga ataques), **Chorro** (reposiciona al rival),
> **Carga AoE** (`All` activo), **Gas** (TurnStatus `All`) y un **arsenal de control en acciones**
> (Veneno / Desmoralizar / Skip-producción / Swap enemigo). Su economía de ⚡ no gotea: llega en **lump**
> por la **Licitación Express** (boost grande). No tiene Stun (es exclusivo de Manif).
>
> **Valores rough** (anclas de diseño, §6.1): pendientes de validar por sim/playtest (ver notación e
> intro en §9). **Watch-point de balance:** Pol acumula dos fuentes de AoE (Carga + Gas) → es fuerte
> vs swarm por diseño (dispersa multitudes), pero validar que no sea opresivo.
>
> **Excepciones de alcance (§6.1, pegan más allá del frente):** Itakero (penetra ×3), **Halcón** (snipe,
> fuerte), **Caballería** (AoE `All`), Carro (snipe, utilidad + Chorro). Repartidas en posición/arquetipo,
> punch mixto. El resto pega al frente (el muro tapa a los cuerpos). Pol cae sobre todos con la carga; no
> tiene mortero al fondo (su precisión es el snipe del Halcón).

### Unidades (9)
| Carta | Costo | Arquetipo | maxHp | Deploy | Ataque · daño | Pasiva | Descripción |
|-------|-------|-----------|-------|--------|---------------|--------|-------------|
| **Infante** | 5 ⚡ | Escaramuza | 24 | Cualquiera | Adelante · 7 ×2 golpes | — (vainilla: presupuesto en stats) | *Casco, escudo y cara de pocas pulgas. Va al frente porque es lo que mejor hace: plantarse y no moverse ni con grúa.* |
| **Itakero** | 4 ⚡ | Cleave | 20 | {2,3,4,5} | Penetra ×3 · 4 | — (vainilla) | *Escopeta Itaka y postas de goma para todos. Apunta al montón y reza, total alguno cae.* |
| **Halcón** | 6 ⚡ | Sniper | 8 | {2,3,4} | Snipe · 15 | — | *Mira telescópica desde la terraza. Te tiene en la cruz desde antes de que llegaras a la esquina.* |
| **Gendarme** | 5 $ | Muro | 26 | Frente {4,5,6} | Penetra ×2 · 4 | Blindaje 2 (−2 al daño de ataques) | *Lo trajeron de la frontera a cuidar una baldosa, y la cuida con la vida. No se mueve, no se cansa, no afloja.* |
| **Carro Hidrante** | 4 $ | Control | 18 | {2,3,4,5} | Snipe · 3 | Chorro: empuja al objetivo al fondo | *Diez mil litros a presión. Te despega del asfalto y te deja en la otra cuadra antes de que termines el cántico.* |
| **Recaudador** | 3 $ | Productora | 12 | Retaguardia {1,2,3} | Adelante · 3 | +2 $/turno | *La plata sale de algún lado y mejor no preguntes. Reparte sobres y se queda con el vuelto.* |
| **Caballería** | 6 📣 | Carga | 16 | {2,3,4,5} | Carga a **todos** (`All`) · 2 | — | *Entran al galope y a lo que venga. El comunicado oficial lo tituló "reordenamiento dinámico del espacio público".* |
| **Trol Oficial** | 3 📣 | Productora | 14 | Retaguardia {1,2,3} | Adelante · 2 | +2 📣/turno | *Diez cuentas, un solo sueldo del Estado y cero ortografía. Inventa la tendencia antes del café.* |
| **Gasero** | 5 📣 | Emisor | 15 | {2,3,4,5} | Adelante · 2 | Gas: Veneno (2) a **todo** el rival (`All`) | *Granada de gas en una mano, pañuelo en la otra. "Es para dispersar", avisa, y la nube no lee carteles: dispersa la plaza, la esquina y el kiosco de paso.* |

### Acciones (8)
| Carta | Categoría | Costo | Efecto | Descripción |
|-------|-----------|-------|--------|-------------|
| **Partida Presupuestaria** | Boost | 2 📣 | +7 $ propio | *Existe en el papel. Se aprobó a las 3 de la mañana y nadie sabe para qué.* |
| **Licitación Express** | Boost | 3 $ | +10 ⚡ propio | *Una empresa, un sobre y 48 horas. El pliego lo hicieron el lunes a la tarde.* |
| **Cadena Nacional** | Boost | 2 $ | +4 📣 propio | *Interrumpe la novela. El presidente habla 40 minutos. Nadie pidió que arranque.* |
| **Operativo Apretón** | Ataque | 6 $ | 27 daño directo a una unidad enemiga | *Cuatro camiones, veinte efectivos y un drone. Todo para un jubilado con un cartel.* |
| **Causa Judicial** | Sabotaje | 4 $ | Veneno (3 daño/turno, 2 turnos) a una unidad enemiga | *Te arman un expediente. Te va comiendo de a poco, durante años.* |
| **Apriete** | Sabotaje | 2 ⚡ | Desmoraliza (−4 daño, 2 turnos) a una unidad enemiga | *Una charla en voz baja contra la pared. Se te van las ganas solas.* |
| **Toque de Queda** | Especial | 5 $ | El oponente no produce el próximo turno | *A las 22 todos adentro. El que se manda afuera, va en cana.* |
| **Reubicación Forzosa** | Especial | 2 $ | Intercambia dos unidades enemigas de slot | *Los suben a un patrullero, los bajan en la otra punta. Protocolo, dicen.* |

### Equipamiento (4)
> Se juega sobre una unidad propia; dura hasta que la unidad muere (§8.4). +HP/+daño son básicos
> (compartidos con Manif); los *grants* de pasiva (Blindaje, Chorro) son exclusivos de Policías.

| Carta | Costo | Efecto | Descripción |
|-------|-------|--------|-------------|
| **Chaleco Antibalas** | 3 $ | +12 maxHp | *Importado. Al menos figura en el inventario, que ya es algo.* |
| **Tonfa** | 2 ⚡ | +4 daño | *Reglamentaria. El uso, a criterio del que la empuña.* |
| **Escudo Antimotín** | 3 $ | Otorga Blindaje 2 (−2 al daño de ataques) | *Policarbonato y reglamento. El palazo que da, no el que recibe.* |
| **Hidrante de Mano** | 3 📣 | Otorga Chorro (empuja al objetivo al fondo) | *Versión de bolsillo del carro. Igual te despeina el cántico.* |

---

## 11. UI / Pantalla

### 11.1 Menú principal

Pantalla de inicio con **dos botones**: **Un jugador** (run roguelike vs IA, §17) y **Dos jugadores** (hotseat local). Cada uno setea el modo y va a la selección de facción. Espacio reservado para futuras opciones (Ajustes, Créditos, etc.).

### 11.2 Selección de facción

Por ahora los **lados son fijos**: **Manifestantes** siempre a la izquierda, **Policías** siempre a la derecha. La pantalla es una **sola elección** que define **qué facción juega primero**: la facción elegida arranca y la otra es el rival.

> [FUTURO] Permitir que cada jugador elija su facción (incluido mirror, ambos lados con la misma facción). Hoy el modelo asume un Manifestantes vs un Policías con lados fijos.

### 11.3 Pantalla de juego

Pantalla única, **lados fijos**: Manifestantes a la izquierda, Policías a la derecha. El layout se organiza en **dos franjas full-width**: arriba la **franja de batalla** (las unidades de ambos jugadores, 6 slots por lado, frentes hacia el centro) y abajo la **mano en abanico** del jugador activo. 6 slots de unidades por jugador siempre visibles, pegados al borde externo de cada lado. La mano pertenece solo al jugador activo.

Los slots van del **1 al 6**. De las **3 unidades iniciales**, el **Muro** arranca **adelante de todo** (slot 6, hacia el centro) — tankea el melee desde el turno 1 y nada se despliega por delante de él —, y la Escaramuza y la Productora arrancan en la **retaguardia** (slots 1 y 2, los más externos de cada lado), protegidas detrás del muro. La fila derecha (Policías) se dibuja invertida.

```
┌────────────────────────────────────────────────────────────────────────────┐
│ MANIFESTANTES            [ Terminar turno ]  (▶ chip)            POLICÍAS      │
│ $:9 ⚡:6 📣:14                                            $:12 ⚡:8 📣:5         │
│                       (fondo / escena de la marcha)                            │
│                    ┌─────────────┐                                             │
│                    │ ⚔ Atacar·5  │ ← popover sobre la unidad clickeada         │
│  FRANJA DE BATALLA └──────┬──────┘                                             │
│ [1][2][3][4][5][6]                                       [6][5][4][3][2][1]    │
│  slots Manif.                                             slots Policías       │
│  (1·2 al borde izq)                                       (1·2 al borde der)   │
│                    ╲ c2  c3  c4  c5 ╱   ← mano en abanico (solo activo)        │
│                      ╲__c1________c6_╱     (asoma desde abajo; hover = sube)    │
└────────────────────────────────────────────────────────────────────────────┘
```

**Jugar carta (sin botón JUGAR):** todas las cartas se juegan en **dos pasos** (seleccionar → confirmar):
- **Con click:** click en la carta la *arma*; los slots/objetivos válidos se **iluminan** y un segundo click confirma. Cartas **con objetivo** (desplegar unidad, equipar, acción que apunta a una unidad, Move/Swap) piden el slot objetivo. Cartas **globales** (sin objetivo de slot) se confirman con un click sobre **cualquier slot del tablero** (todos resaltados), evitando jugadas accidentales.
- **Con drag & drop:** se arrastra la carta y se suelta directamente sobre un **slot objetivo válido** (o, para globales, sobre **cualquier slot**). Mientras se arrastra, una **copia de la carta ("ghost")** acompaña el puntero y los slots elegibles se **iluminan**. Soltar fuera de un drop target válido **cancela sin gastar recursos** (la carta vuelve a la mano). El **click derecho** a mitad de arrastre **aborta el drag** (saca el ghost y limpia los resaltados) sin jugar ni descartar.

**Descartar carta (sin botón DESCARTAR):** **Ctrl+Click** sobre la carta (mouse) o **Ctrl + 1–6** (teclado). Para anticipar la acción, mientras se mantiene **Ctrl con el mouse sobre una carta** ésta se **tiñe de rojo** y aparece un cartel **"DESCARTAR"** cruzado; al soltar Ctrl vuelve al look normal.

**Mano en abanico:** las cartas se dibujan solapadas en arco y asoman parcialmente desde el borde inferior; al **hover** una carta **sube, se endereza, crece y pasa al frente** para leerse completa (y abre su popover de detalle). Esto deja la franja de batalla más grande y prominente.

**Atacar con una unidad:** las unidades propias que **pueden actuar este turno** (del jugador activo, sin atacar aún, no aturdidas y permitido por la regla del turno 1) se **resaltan**. **Click** sobre una de ellas → aparece un **popover** con su acción disponible — `⚔ Atacar` (o `✚ Curar`) con el **daño/cura en un chip destacado**, una sub-línea con el **alcance en palabras** + el **costo en ⚡** del ataque, y un **caret que apunta a la unidad** — más un *preview* de a qué slots llega; al clickearlo, actúa. **Si no le alcanza la ⚡:** la unidad **igual es clickeable** (se resalta en gris), pero el popover sale **deshabilitado** con el costo en rojo, dejando claro por qué no puede atacar. Si es a elección (`mode = Any`, snipe), a continuación se clickea la unidad objetivo (en el tablero rival si daña, **en el propio si es un healer** que cura aliadas, §7.2); los modos anclados (`Frontmost`/`Backmost`/`All`) actúan directo sin elegir. **[FUTURO]** si una unidad llega a tener varios ataques (`List<UnitAttack>`), el popover los lista.

**Equipar:** se arrastra la carta de Equipo sobre una **unidad propia** (el slot es el *drop target*, §8.4).

**Mover / intercambiar (efectos de dos slots, MoveUnit/SwapUnits):** se elige el **primer** slot (arrastrando la carta a ese slot, o clickeando la carta y luego el slot) y luego se clickea el **segundo** (Move: slot propio libre y permitido; Swap: la otra unidad enemiga). El primer slot queda marcado mientras se elige el segundo.

**Anatomía de la unidad en el tablero (implementado):** el **sprite llena toda la caja** del slot (cover, anclado al piso) y la info va **superpuesta abajo, por encima del sprite**: nombre → **barra de HP con el valor superpuesto** → **fila de iconos**. El **daño/cura efectivo** se muestra como un **badge compacto arriba-derecha** (chip rojo `⚔ N` para ataque, verde `✚ N` para cura), donde **N es el daño por golpe**. Para el alcance se usa una **convención clara, sin `×N`** (que se confundía con "pega N veces"): junto al daño aparece una etiqueta aparte según el caso — **`N obj.`** / **`todos`** si pega a varias unidades (`count>1` / AoE), y **`N golpes`** si es **multi-hit** (`hits>1`, varios golpes al mismo objetivo, §7.2). La **fila de iconos** (chips compactos) cubre el resto: **pasivas** (producción/regen/aura/espinas/blindaje/empuje/al-morir/daño o estado por turno), **estados** (Veneno/Aturdir/Furia/Desmoralizar) y **equipo** adjunto. El **hover sobre el badge de daño** abre el detalle del ataque (daño, alcance/golpes y **costo en ⚡**). El sprite, el nombre y la barra de HP son **decorativos (no capturan el puntero)**; sólo los iconos y el badge lo hacen: así toda la caja es una única región de hover y el popover no parpadea. Cada icono es *sprite-ready* (glifo emoji como fallback). Además, indicador por jugador para sus `activeStatuses` de producción en el panel de stats.

**Hover de detalle (dos niveles):** al pasar el mouse sobre **un icono** se abre el popover con el detalle de *ese* efecto (su forma de ataque/zona, magnitud y turnos restantes); al salir del icono, vuelve el popover **completo** de la unidad. Pasar el mouse sobre el resto de la unidad muestra directamente la info completa (nombre, descripción, alcance, pasivas, efectos activos y equipo).

**Popover informativo (hover):** al pasar el mouse sobre una **unidad** del tablero o una **carta** de la mano, se despliega un panel con su nombre, descripción, alcance (a qué slots pega/cura, cuánto y en cuántos golpes), **costo del ataque en ⚡**, pasivas, efectos activos y equipo.

**Indicador de turno:** un **chip** (pill) que salta al lado del jugador activo, más la marca **▶** en su panel de stats. El botón **Terminar turno** está centrado en el tope.

**Medidor de inflación:** un cartel central (en el medio de la pantalla) que **aparece cuando arranca la inflación** (medio-turno `inflationStartTurn`, §3) y muestra el **% vigente** ("INFLACIÓN +X%"). El color se intensifica (amarillo → rojo) a mayor inflación. Mientras está activa, el **costo mostrado en las cartas** ya viene inflado (y se tiñe de naranja).

**Turno de la IA (single-player, §17):** durante el turno del oponente IA, el **input del humano se bloquea** y se muestra un indicador **"Turno de la IA"**. La IA juega **con delays** (reusa el `schedule`/animaciones de ataque, §7.10) para que sus jugadas —puede jugar varias cartas y atacar con varias unidades en un turno (§6)— se puedan seguir. Al terminar, el control vuelve al humano.

### 11.4 Input

| Acción | Mouse | Teclado |
|--------|-------|---------|
| Jugar carta (con objetivo) | Click en la carta → click en el slot objetivo (los elegibles se iluminan); o arrastrar la carta al slot | — |
| Jugar carta (global, sin objetivo) | Click en la carta → click en **cualquier slot** (todos resaltados) para confirmar; o arrastrar a cualquier slot | — |
| Descartar carta | **Ctrl+Click** sobre la carta | **Ctrl + 1–6** |
| Desplegar unidad | Click → slot propio **libre** y permitido (no hay reemplazo, §8.3); o arrastrar al slot | — |
| Equipar a una unidad | Click → unidad propia; o arrastrar el equipo sobre la unidad propia | — |
| Atacar / curar con una unidad | Click en la unidad (resaltada) → click en el popover de acción | — |
| Elegir slot objetivo (ataque/cura a elección, sabotaje) | Click en el slot | — |
| Mover / intercambiar unidad (MoveUnit / SwapUnits) | Arrastrar al primer slot (o click carta→slot) → click en el segundo | — |
| Ver info de unidad / carta | Hover sobre la unidad o la carta | — |

### 11.5 Anatomía de una carta

Cada carta muestra:
- **Imagen**
- **Nombre**
- **Costo** — ícono del recurso + cantidad
- **Efecto** — texto corto

Al hacer **hover** sobre una carta se despliega un **popover informativo** con el detalle completo: nombre, costo, tipo, descripción y, según el tipo, HP/alcance/deploy/pasivas (unidad), efectos (acción) o modificadores y pasivas otorgadas (equipo).

### 11.6 Pantalla de victoria

Overlay con:
- Mensaje de victoria (facción ganadora + condición)
- Botón **Revancha** — reinicia con las mismas facciones
- Botón **Menú principal**

---

## 12. Parámetros configurables

| Parámetro | Valor |
|-----------|-------|
| Cartas visibles en mano (`handSize`) | 6 |
| **Cartas por turno** | **Varias: tantas como pueda pagar** (limitadas por recursos, §6) |
| **Recurso de costo (por tipo, §3)** | Unidad → **$ Dinero** · Acción/Equipo → **📣 Social** |
| **Ataques por turno** | **Uno por unidad** propia no aturdida (§6), si alcanza la ⚡ |
| **`attackFuerzaCost`** | ⚡ Fuerza que cuesta cada ataque de unidad. Default **1** (rough, tunear; §6) |
| **Reposición de mano** | Rellenar a `handSize` al fin del turno (rough, tunear; §6/§8.1) |
| Slots de unidades por jugador | 6 |
| Apilamiento de unidades | No activo (punto de extensión [FUTURO], `UnitSlot.count`) |
| Unidades iniciales por facción | Predefinidas (data por facción) |
| Recursos iniciales | 5 de cada uno (configurable) |
| Producción base por turno | +1 de cada recurso ($/⚡/📣); en `GameConfig` (configurable) |
| Producción de una productora | **+2** de su recurso (recurso = 3/turno; §6.2) |
| Factor económico global de costo | **×1.2** sobre el costo base (uniforme, horneado; `knobs.cost_mult`). ⚠️ Re-derivar al rebalancear el combate (multi-carta, §6) |
| `inflationStartTurn` | Medio-turno **8** (rol = presión anti-stalemate, §3). ⚠️ Re-derivar al rebalancear el combate |
| `inflationPercentPerTurn` | **+5%** acumulativo por medio-turno. ⚠️ Re-derivar al rebalancear el combate |
| Primer jugador: turno 1 | **Produce** pero **no puede atacar** (regla de iniciativa, §3/§16) |
| Lados de facción | Fijos (por ahora): Manifestantes izquierda, Policías derecha |
| Primer jugador | Lo elige la selección de facción (la que arranca); coinflip si no se especifica |
| `suddenDeathStart` | Turno 50 (backstop, configurable; bien por encima de la duración ideal) |
| `maxTurns` | 120 (backstop duro, configurable) |
| Duración ideal de partida | ~30–40 medios-turnos (objetivo; re-validar tras el rework de cartas) |
| Cartas por facción | 21 (9 unidades + 8 acciones + 4 equipo) |
| Mazo de robo | Barajado, **sin reemplazo**; descarte que se rebaraja al vaciarse (§8.1). Hotseat = mazo de facción; single-player = **mazo de la run** (§17) |
| Copias en el mazo (`drawWeight`) | Productoras y boosts de producción 2 · resto (incl. unidades) 1 → ~27/facción (§8.1) |
| Techo de recursos (`maxResource`) | **18** (anti-atesoramiento **suave** con multi-carta, §3). ⚠️ Re-derivar al rebalancear el combate |
| Unidades iniciales por facción | **3**: Escaramuza (retag) + Productora (retag) + Muro (frente) |

> **Balance por simulación (`sim/`) — PENDIENTE de re-validar tras el rework de cartas (§9/§10).** El
> rework cambió todo el pool (roster asimétrico de 9/facción, pasivas nuevas `OnDeath`/`Armor`/`PushBack`,
> productoras +2, 8 acciones). Los números de balance previos (duración, presencia, facción) quedaron
> **obsoletos**. Tras bakear `CardLibrary` + `sim/`, correr `py sim/parity_check.py` (paridad motor↔sim)
> y `py sim/main.py run` (facción ≈ 50/50 en agregado, duración objetivo, presencia/vanguardia) y
> apretar carta por carta. Valores en revisión por playtest.

---

## 13. Pendientes [DEFINIR]

- **Balance de unidades:** ⏳ **pendiente de re-balanceo tras el rework de cartas** (§9/§10 son valores rough). Validar en `sim/` (facción ≈ 50/50 en agregado, duración objetivo) y apretar carta por carta.
- **Apilamiento:** punto de extensión reservado (`UnitSlot.count`), inactivo en v1.
- **Unidades iniciales por facción:** **3** — los tres pilares de apertura: **Muro** (defensa) + **Productora** (economía) + **Escaramuza** (ataque). Manifestantes = Encadenado + Gordo Sindical + Piquetero; Policías = Gendarme + Recaudador + Infante. El **Muro** arranca **adelante de todo** (slot de mayor índice libre, §11.3) para tankear el melee desde el turno 1 y que nada se despliegue por delante; la Productora y la Escaramuza arrancan en la retaguardia (slots 1–2), protegidas detrás. Da presencia en el frente desde el arranque y combates más largos. La colocación la hace `GameEngine.StartingSlot`.
- **Feedback visual por unidad:** ✅ **implementado** — slot tipo "carta de unidad" (área de arte sprite-ready + barra de HP con valor + fila de iconos para stat de acción/pasivas/estados/equipo, con *tooltip*), más el popover informativo de hover (§11.3). Pendiente sólo, si se quisiera, reemplazar los glifos emoji de los iconos por **iconos dibujados** (asignando el `Sprite` de cada `SlotIcon`) y el **sprite del personaje** en el área de arte.
- **Tope de equipos por unidad:** hoy sin límite (§8.4); definir si conviene un máximo.
- **EquipmentCardData:** diseñado e incluido en el catálogo (§7.1 / §8.4 / §9/§10, 4 cartas/facción); falta implementar (capa de stats efectivos, §15 Fase 4).
- **IPlayerController:** planificado para el single-player (impl Human + AI, §7.8/§17.5). Online sigue [FUTURO].
- **Deckbuilding:** mazo de la run (starter + recompensas) en el single-player (§17.2); tienda y armado pre-run = puntos de extensión (§17.4/§17.6).
- **Combate automático por tempo:** alternativa al combate manual a evaluar en sesión próxima (§17.6).

---

## 14. Fuera de scope (v1)

- Construcción de mazos personalizada **pre-run** (el single-player la deja como punto de extensión, §17; el mazo evoluciona por recompensas, no por armado libre todavía)
- Tienda de cartas en la run (punto de extensión, §17)
- Meta-progresión entre runs (desbloqueos persistentes; §17 lo deja como extensión)
- Más de 2 jugadores
- Online / multijugador en red
- Animaciones elaboradas (hoy: shake y flash)
- Más de 2 facciones

---

## 15. Extensiones de modelo [IMPLEMENTAR]

Checklist para la sesión de implementación. **El spec es la fuente de verdad: `CardLibrary.cs` y los assets deben quedar alineados con él.**

> **Nota (rediseño de combate):** el targeting que estas fases describen (`AttackReference Absolute/Relative` + `pattern` + `pickCount`, y `PassiveScope`) fue **reemplazado** por el modelo de **formación anclada** — `TargetMode` (`Frontmost`/`Backmost`/`Any`/`All`/`Adjacent`/`Self`) + `count` — descrito en §6/§7.2/§7.3. Los nombres de campo en las cajas de abajo son **históricos**; la semántica vigente es la del §6.

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

---

## 16. Política de decisión (bot heurístico / simulador)

El simulador de balance (Fase 5, en `sim/`) y el futuro `IPlayerController` de IA (§7.8) necesitan decidir, sin un humano, en cada **punto de elección** del turno. Esta sección define esa política como **data de comportamiento documentada**: greedy (sin lookahead), determinista y reproducible. Es deliberadamente simple — el objetivo del simulador es validar el **escalado parejo** de los knobs globales, no jugar perfecto.

> **Advertencia de validez:** el balance que reporta el simulador vale lo que vale esta política. Por eso (a) queda documentada acá y centralizada en un solo módulo (`policy.py`), (b) ambos jugadores usan **la misma** política (espejo) para que el win-rate por facción mida la facción y no al bot, y (c) cualquier cambio de política se versiona junto con los números que produjo.

### 16.1 Función de valor de una unidad

Sirve para priorizar objetivos (a quién pegar/curar) y para puntuar el deploy. Sigue la heurística del §6:

```
valor_unidad(u) = maxHp_efectivo/4 + daño_total_efectivo/2 + valor_pasiva(u)
daño_total_efectivo = damagePerSlot_efectivo × n_golpes   (n_golpes = count si Frontmost/Backmost/Any; ~3 si All)
valor_pasiva: ProduceResource → 4·value ; Aura/Retaliate/Regeneration → 3·value ;
              TurnDamage → 3·value ; TurnStatus → 4 ; HealAllies → 3·amount
```

### 16.2 Orden del turno (fase ACCIÓN)

El bot puede jugar **varias cartas** (mientras pueda pagar) y atacar con **cada unidad** no aturdida (§6). Resuelve en este orden fijo:

1. **Cartas primero, ataques después.** Jugar las cartas puede mejorar los ataques (buff, deploy, debuff al rival).
2. **Fase de cartas (greedy, en bucle):** mientras quede una carta asequible cuyo puntaje (§16.3) supere el **umbral de juego** (default `0`), juega la de **mayor puntaje** y **re-evalúa** (recalcula con los recursos y el tablero ya actualizados). Termina cuando ninguna asequible supera el umbral. Una sola vez por turno, si conviene ciclar, **descarta** la carta de menor valor potencial. (Re-evaluar cada vez evita gastar todo en algo que un deploy previo volvió subóptimo.)
3. **Fase de ataques (greedy, en bucle):** mientras alguna unidad propia no aturdida y **sin atacar aún** tenga una jugada de valor positivo (§16.4), ejecuta la de **mayor valor** y re-evalúa. Cada unidad ataca a lo sumo una vez. Termina cuando ninguna aporta valor (p. ej. todos los objetivos son whiff).

### 16.3 Puntaje de cartas (qué jugar)

Cada carta recibe un puntaje; se juega la de mayor puntaje > umbral.

| Tipo de carta | Puntaje |
|---------------|---------|
| **Unidad** | `valor_unidad` (§16.1). Penaliza −∞ si no hay slot permitido **libre** (no hay reemplazo, §8.3). Bonus si el tablero propio tiene < 2 unidades (necesidad de presencia). |
| **Boost (recurso propio)** | `value` del recurso ganado, ×1.5 si ese recurso es el que más limita jugar el resto de la mano (está "corto"); ≈0 si ya está cerca del tope o no lo necesita. |
| **Sabotaje (drenaje rival)** | `min(value, recurso_actual_rival) × 0.5` (drenar lo que el rival no tiene no vale). |
| **Daño directo (ModifyHP−)** | Daño aplicable al **mejor objetivo enemigo** (§16.5). Bonus grande (`+valor_unidad` del objetivo) si **mata**. 0 si no hay enemigos. |
| **Cura/Defensa (ModifyHP+)** | HP que realmente restaura (cap maxHp) sobre el **aliado dañado más valioso**; 0 si nadie está dañado. |
| **Status a enemigo** (Stun/Poison/Desmoralizar) | Stun → `0.5·valor_unidad` del mejor atacante enemigo; Poison → `value·counter` proyectado; Desmoralizar → `value·counter·0.4`. 0 si no hay objetivo. |
| **Status a aliado** (Furia/Double prod) | Furia → `value·counter·0.5` sobre el mejor atacante propio; DoubleProduction → producción proyectada del próximo turno. |
| **MoveUnit / SwapUnits** | Utilidad situacional baja por default (`0.1`): se juega sólo si nada mejor supera el umbral. Reubicación rival (Swap enemigo) puntúa si rompe una línea (heurística simple: hay enemigo en frente que conviene mandar atrás). |
| **Equipo** | `valor_aportado` (Σ statModifiers vía §16.1 + valor_pasiva otorgada) si existe un **portador** propio vivo razonable (prefiere la unidad propia de mayor valor que no muera pronto); −∞ si no hay portador. |

### 16.4 Elección de ataque (con qué unidad y a quién)

Para cada unidad propia no aturdida, evalúa su `UnitAttack`:

- **Daño (`DamageEnemies`):** resuelve los objetivos según `mode`/`count` sobre la formación rival (§6). El **valor de la jugada** = Σ sobre los objetivos de `min(daño_efectivo, hp_objetivo)` + `valor_unidad(objetivo)` por cada objetivo que **muere**. Daño efectivo = base + Furia + Aura − Desmoralizar; resta el `Retaliate` esperado del defensor al valor.
  - **`mode = Any` (snipe):** elige las `count` unidades que maximizan ese valor (greedy: prioriza kills, luego mayor daño-no-desperdiciado, luego mayor `valor_unidad` del objetivo). Los modos anclados (`Frontmost`/`Backmost`/`All`) no eligen: golpean lo que la formación determina.
- **Cura (`HealAllies`):** valor = Σ `min(amount, maxHp − hp_actual)` sobre los aliados objetivo; prioriza al aliado dañado de mayor `valor_unidad`. `mode=Any` análogo.

Elige la unidad+objetivos de mayor valor de jugada. Empates: menor índice de slot (determinista).

### 16.5 "Mejor objetivo" (targets de efectos de carta a slot)

Cuando un efecto pide un slot (daño directo, cura, status, swap/move) y `targetSlot = -1`:

- **Daño/Status ofensivo a enemigo:** el slot enemigo que **muere** con el efecto y tenga mayor `valor_unidad`; si ninguno muere, el de mayor `valor_unidad` (rematar a la pieza más cara). Para Stun, el mejor **atacante** enemigo.
- **Cura/Buff/Equipo a aliado:** para cura, el aliado dañado de mayor `valor_unidad` que más HP recupere; para Furia/equipo, el mejor **atacante** propio vivo.
- **MoveUnit (propio):** mueve la unidad de retaguardia más valiosa-y-frágil fuera del alcance estimado del rival, o un muro al frente; default: primer movimiento legal que mejore la posición (si ninguno, no juega la carta).
- **SwapUnits (enemigo):** intercambia para sacar del frente al enemigo de mayor `valor_unidad` (mandarlo a retaguardia), si hay tal configuración.

### 16.6 Deploy (slot de despliegue)

Al desplegar una unidad sin slot forzado por `allowedSlots`, el bot elige entre los slots permitidos **libres** con este orden de preferencia (determinista):

1. Productoras / Snipers / Emisores / Healers → **retaguardia** (menor índice permitido): se quieren proteger.
2. Muros → **frente** (mayor índice permitido): tapan la línea.
3. Escaramuza / Cleave → **frente** (mayor índice permitido): la posición ya no cambia a quién pegan (el targeting está anclado a la formación rival, §6), así que van adelante para tankear el melee enemigo.
4. Si no hay slot permitido **libre**: la unidad no se puede desplegar (no hay reemplazo, §8.3) → no se juega.

### 16.7 Determinismo

Toda la política es función pura del estado observable + un RNG inyectado (sólo para coinflip inicial y robo de cartas, §7.9). Sin desempates aleatorios: los empates de puntaje se rompen por menor índice. Misma seed + mismos knobs → misma partida, byte a byte.

> **Adaptación al turno multi-acción (§6):** la política de arriba se aplica **en bucle** (§16.2): juega cartas hasta que ninguna supere el umbral y ataca con cada unidad. El `AIPlayerController` del single-player (§17) porta esta misma política a C# en `Core`.

---

## 17. Modo un jugador (run roguelike-deckbuilder)

El single-player **no es un combate suelto**: es una **run** de varios combates encadenados con
progresión, inspirada en Slay the Spire (progresión + recompensas) y The King is Watching (ritmo).
Reusa íntegro el motor de combate (§6) — el rival lo controla la **IA** (§16, vía `IPlayerController`,
§7.8). El hotseat 2-jugadores comparte el mismo motor.

> **Estado:** diseño aprobado (sesión 2026-06-27); **base de Core extendida e implementada** (pasos 1-7
> del plan, **95/95 tests EditMode verde**): arquetipos de enemigo, oro, tipos de nodo + tesoro/élite,
> reliquias, boss data-driven y el **acto 1 = Línea A del subte**. **Pendiente:** wirear la presentación
> a esta base nueva (hoy la presentación de Fase 4 corría el mapa default de combates), crear el
> **contenido** (assets de arquetipos/reliquias/boss) y los pasos 8-10 (tienda/taller/evento, upgrade,
> consumibles). Esta sección es la fuente de verdad del modo. **Mapa NO por carriles**: puntos a elección
> con dificultad por distancia (§17.1). Valores = **rough, a iterar por playtest** ([[feedback-playtest-driven]]).

### 17.1 Objetivo y estructura

- **Mapa = SUBTE de Buenos Aires** (decisión 2026-06-27): **línea = acto**, **estación = nodo**,
  **transbordo/combinación = bifurcación** entre ramas (y puente entre actos cuando haya multi-acto). El
  jugador **elige a qué estación ir** entre las disponibles; cada estación se visita **una sola vez**
  (elegir una ruta **saltea** las hermanas). **Multi-acto eventual**; hoy **un acto**.
- **Acto 1 = Línea A** (`RunMapLibrary.BuildActo1`): de **Primera Junta** (el barrio) a **Plaza de Mayo /
  Casa Rosada** (cabecera = jefe). 7 estaciones: 3 combates + 1 **tesoro** + 1 **élite** + boss, con ramas
  paralelas → **3-4 peleas por pasada** según ruta. Forma/tamaño = **data, rough, iterable**.
- **Variedad por ARQUETIPOS de enemigo curados** (`EncounterDefinition`, §17.5): cada combate enfrenta un
  arquetipo (mazo + unidades iniciales + handicap propio + estilo) sorteado de un pool **sin repetir** en la
  run (`RunState.usedEncounterIds`). Reemplaza el viejo "mazo default opuesto + handicap" (que queda como
  **fallback** si no hay pool). La variedad viene del enemigo, no de subir un número.
- **Dificultad por distancia:** más lejos del inicio = más difícil. Distancia = **saltos** (BFS). La IA
  recibe un **handicap escalado por distancia** (`RunConfig.aiResourceBonusPerLevel`, default +2/nivel)
  que **se suma** al bonus propio del arquetipo. El humano **no** recibe handicap (salvo reliquias, §17.4).
- **Tipos de nodo** (`MapNodeType`, todos **data**): `Combat`, `Elite` (combate más duro, mejor paga),
  `Boss` (cabecera), `Shop` (tienda), `Event` (decisión), `Workshop` (taller: upgrade/remoción),
  `Treasure` (oro/reliquia), `Mystery` (resultado oculto). **Implementados en Core:** Combat/Elite/Boss
  (camino de combate) y Treasure (atómico, otorga oro). Shop/Event/Workshop/Mystery = pasos 8/10.
- **Objetivo de la run:** vencer el combate del **jefe** (cabecera de línea, tipo `Boss`) →
  `RunState.status = Won`. **[Seam multi-acto]** con varios actos, vencer un jefe que no es el último
  incrementa `RunState.actIndex` y carga el mapa del próximo acto en vez de ganar (hoy: un acto = Won).
- **Derrota = fin de la run** (permadeath): perder o empatar corta la run.

### 17.2 Mazo de la run y deckbuilding

- Se arranca con un **mazo starter** de la facción (hoy el default; **starter chico** = afinado de
  contenido). Crece con recompensas **1-de-3** tras cada combate. El mazo es **estado persistente** y se
  **inyecta en el motor** al iniciar cada combate (§8.1/§7.8).
- **Capas de profundidad acordadas** (2026-06-27): **reliquias** (§17.4, ✅), **mejora de cartas**
  (upgrade), **remoción de cartas** (✅ Core), **consumibles** (un uso), y **economía de oro + tienda**.
  **Implementado:** oro (`RunState.gold`, se gana en combate/élite/tesoro) y **remoción** (taller/`Workshop`:
  `RunManager.EnterWorkshop`/`RemoveCardAndLeave`/`LeaveWorkshop`, con `RunConfig.minDeckSize`; falta su
  pantalla y el nodo en `BuildActo1`). **Pendiente:** upgrade (`RunState.deck` pasa a `List<RunCardEntry>`
  + `RunCardFactory.Materialize`, migración aislada — paso 9), tienda y consumibles.

### 17.3 Persistencia entre combates

- **Reset total de vida** (decisión 2026-06-27): el **tablero**, **recursos**, **estados** y **mano** se
  reinician cada combate (sin atrición de vida — no hay HP global, §4). La tensión de la run viene del
  **mazo + oro + reliquias**, no de la vida; por eso el "descanso/curación" se reemplaza por el **taller
  de mazo** (`Workshop`).
- **Persisten:** el **mazo** (evolucionado), las **reliquias** (§17.4), el **oro** y el progreso del mapa.

### 17.4 Reliquias (implementadas)

Modificadores **persistentes** de la run (`RelicData`, ScriptableObject) que se traducen a bonos del
`PlayerSetup` del humano al iniciar cada combate — **el mismo seam que el handicap de la IA, sin tocar el
motor**. `RunState.relics` = lista (no campos sueltos, [[feedback-buenas-practicas]]). `RunManager.ApplyRelics`
las vuelca acumulando sobre lo que ya traiga el setup (ej. una pasiva de jefe). Tipos (`RelicEffectKind`):
- **`BonusResource`** — +N a un recurso inicial del humano.
- **`ExtraStartingUnit`** — despliega una unidad inicial extra.
- **`InitialStatus`** — siembra un estado al iniciar (vía `PlayerSetup.initialStatuses`, §7.8).

> Reliquias con **hooks dinámicos** (al-matar, al-inicio-de-turno-global) necesitarían un `ICombatRule`
> inyectable en el motor — **reservado, no implementado** (§17.6). Las tres variantes de arriba no tocan
> el motor. Cómo se obtienen (tesoro/élite/tienda) = contenido/pasos siguientes.

### 17.5 Arquitectura (Core, C# puro)

- **`RunState`:** `map`, `currentNodeId`, `actIndex`, `status`, `deck` persistente, `clearedNodeIds`,
  `usedEncounterIds` (no repetir arquetipos), `gold`, `relics`. Datos puros, testeable sin Unity.
- **`EncounterDefinition`** (SO, §17.1): `faction`, `difficulty` (tier), `deck` (multiset literal),
  `startingUnits`, `bonus{Dinero,Fuerza,Social}`, `aiInitialStatuses`/`playerInitialStatuses` (estados
  sembrados en la IA o en el humano), y sólo-jefe `isBoss` + `leaderUnit` (unidad-líder única cuyas pasivas
  son la "pasiva de combate del jefe"). La pasiva de jefe se expresa con **pasivas del líder + estados
  sembrados**, sin tocar el motor.
- **`EncounterLibrary`** (Core): fuente de verdad de los arquetipos del acto 1 (espejo de `CardLibrary`).
  `BuildActo1Pool(catalog)` arma el pool **en memoria** desde el catálogo (por `unitSubtype`, sin duplicar
  assets): por facción Patota/Búnker/Aparato + Jefe. La presentación (`FactionSelectController`) lo pasa al
  `RunManager`.
- **`RelicData`** (SO, §17.4).
- **`RunMap` / `MapNode` / `MapNodeType` / `RunMapLibrary`:** grafo de puntos como **data**. `MapNode` =
  `{ id, type, title, connections, x/y (inertes en Core) }`. Dificultad = distancia (BFS).
  `RunMapLibrary.BuildActo1` arma la Línea A; `BuildDefaultMap` se conserva como fixture de tests.
- **`RunManager`:** `AvailableNodes` (bloquea con recompensa o taller abierto), `BeginCombat(nodeId)` (arma
  la IA desde el arquetipo del pool — fallback al default opuesto sin pool — aplica reliquias al humano y
  devuelve un `GameEngine` iniciado), `PickEncounter` (tier + no-repetir, RNG inyectado), `ResolveCombat`
  (gana→recompensa+oro / jefe→`Won` / pierde→`Lost`), `EnterTreasure` (otorga oro y avanza), `EnterWorkshop`/
  `RemoveCardAndLeave`/`LeaveWorkshop` (taller de remoción, respeta `minDeckSize`), `AdvanceTo` (avance único
  compartido combate / no-combate), `ChooseReward`/`SkipReward`. `RunConfig` = parámetros (handicap,
  `rewardCount`, oro por combate/élite/tesoro, `minDeckSize`). Lados fijos: humano = índice de su facción, IA la otra.
- **Seam de motor único:** `PlayerSetup.initialStatuses` (§7.8) — siembra estados al iniciar (ruteo igual
  que `ApplyStatus`: de jugador→`activeStatuses`, por-unidad→unidades desplegadas). Lo usan reliquias y la
  pasiva de jefe. Todo lo demás se materializa en `PlayerSetup`/`RunState` sin tocar el resolutor.
- **IA del rival:** `IPlayerController` / `HeuristicAiController` (§16), sin cambios. Una sola dificultad.

### 17.6 Puntos de extensión (resumen)

- **Pasos 8-9 (Core):** `Workshop` (remoción ✅ Core, falta pantalla + nodo en el mapa), `Shop` (stock con
  RNG + gastar oro), `Event` (`EventDefinition` data-driven), `Mystery` (resuelve a otro tipo); **upgrade de
  cartas** (`RunCardEntry` + `RunCardFactory`, migra `RunState.deck`).
- **Reliquias jugables (hueco actual):** el motor las aplica, pero falta una `RelicLibrary` + flujo para
  obtenerlas (tesoro oro-o-reliquia / boss / élite / tienda) + HUD.
- **Presentación pendiente:** pantallas de taller/tienda/evento, HUD de reliquias, estilos USS de los tipos
  de nodo, estética del subte. Ya wireado: `FactionSelectController` (BuildActo1 + pool) y `MapController`
  (dispatch de tesoro, HUD de oro, clases por tipo).
- **Diferidos (paso 10):** **consumibles** (carta con flag `consumable`), **`AiProfile`** activo (estilos
  de IA por arquetipo), **`ICombatRule`** (hooks dinámicos de reliquia/boss), **generación procedural** de
  mapa, **meta-progresión** entre runs, **armado de mazo pre-run**, **3ª facción**.
- **Multi-acto:** seam listo (`RunState.actIndex`); el acto 2+ = nuevas líneas del subte (contenido).
