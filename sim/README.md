# Simulador de balance — PiqueteDefend

Re-implementa las reglas del spec (`docs/game-spec.md`) en Python puro, determinista y
seedeable, para balancear el juego sin abrir Unity. **No llama a Unity.** Espeja §9/§10 (cartas)
y §12 (parámetros) y resuelve combate posicional por slots con victoria por KO.

## Uso (Python via `py`)

```
py main.py run                                   # batch con knobs default, verbose
py main.py run --n 5000 --hp 0.85 --dmg 1.5 --no-attack-t1 --produces-t1   # config final
py main.py game --seed 7                         # traza de 1 partida (debug)
py main.py sweep --n 300                          # barrido de knobs
py main.py dump --hp 0.85 --dmg 1.5              # catálogo con knobs aplicados (números finales)
```

Conteo de turnos: **medios-turnos** (1 por turno de jugador), igual que el spec.

## Módulos

- `model.py` — tipos de dominio (enums + plantillas de carta), espejo de Core.
- `cards.py` — catálogo §9/§10 (44 cartas) + unidades iniciales. **Valores base del spec**; los knobs los escalan.
- `rules.py` — motor de reglas (`GameEngine`): loop de turno, ataques, pasivas, estados, equipo, muerte súbita.
- `policy.py` — política de decisión greedy heurística (spec §16). Ambos jugadores la usan (espejo).
- `knobs.py` — `GlobalKnobs`: multiplicadores globales de balance.
- `sweep.py` — harness de batch + métricas + score de combo.
- `main.py` — CLI.

## Política de decisión

Documentada en `docs/game-spec.md` §16. Greedy sin lookahead, determinista. Validez: el balance
vale lo que vale la política; por eso es espejo (mide la facción, no al bot) y está centralizada.

## Resultado del balanceo (config final adoptada)

Knobs: **hp ×0.85, dmg ×1.5**, economía sin cambios (base +1, iniciales 5), + dos reglas de inicio.
Estos multiplicadores ya están **horneados** en los números finales del spec/CardLibrary
(el juego NO usa multiplicadores globales en runtime; el sim los usaba para tunear).

Métricas (n=5000):

| Métrica | Valor | Target |
|---|---|---|
| Win-rate facción (Manif/Pol) | 48 / 52 % | ~50/50 |
| Gana el primer jugador | 48 % | ~50 (sin ventaja) |
| Media de medios-turnos | 32 | 30–40 |
| Mediana | 21 | — |
| Fin por KO | 97 % | mayoría KO |
| Llega a muerte súbita (t50) | 23 % | pocas |
| Toca maxTurns (t120) | 3 % | casi 0 |
| Recursos "starved" | ~2 % | bajo |

### Reglas de inicio adoptadas (cambian el spec)

1. **El primer jugador no puede atacar con unidades en su turno 1** (nueva). Compensa la ventaja de iniciativa.
2. **El primer jugador SÍ produce en su turno 1** (cambia §3/§6, que decía que no). Sin esto, la regla
   anterior sobre-corrige y el segundo jugador queda favorecido.

Sin reglas de inicio el primer jugador ganaba **58.6%**; con ambas, **48.1%** (parejo).

### Hallazgos de diseño (registrados)

- **Bimodalidad estructural:** con 2 unidades iniciales + KO-a-la-última, ~1/3 de las partidas son
  blowouts cortos y hay cola de grind. La media se ubica con HP/daño; la forma es inherente al diseño.
  +1 unidad inicial **empeora** la cola de grind (más cuerpos → más estancamiento), no la mejora.
- **Balance de facción es per-card y sensible a umbrales** (si un muro sobrevive o no a un golpe), no
  se arregla con knobs globales. Se corrigió tuneando HP/daño per-card (costos y pasivas quedaron fijos).
- **Snipers glass-cannon:** 8 HP — alto alcance anti-eco (one-shotean productoras) a cambio de morir a
  cualquier golpe (counterplay).
- **Productoras frágiles** (mueren ~93%): rol de "economía a riesgo"; la base +1/turno sostiene el flujo.
