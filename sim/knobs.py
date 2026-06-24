"""Knobs globales de balance (Fase 5).

Escalan TODO de forma pareja (no carta por carta). El catálogo (cards.py) y la config
de partida (rules.py) aplican estos multiplicadores sobre los valores base del spec.

Insight clave de duración: escalar HP y daño por el MISMO factor no cambia la duración
(golpes-para-matar = k·hp / k·dmg = hp/dmg). La duración la mueve la RAZÓN hp/dmg, por
eso `hp_mult` y `dmg_mult` son knobs separados. La economía (`*_mult` de recursos) cambia
cuántas unidades/acciones entran en juego y, con eso, indirectamente la duración y el flujo.
"""

from __future__ import annotations

from dataclasses import dataclass


def scale(value: int, mult: float, minimum: int = 0) -> int:
    """Escala y redondea un valor entero, con piso opcional (p. ej. costo mínimo 1)."""
    out = int(round(value * mult))
    return max(out, minimum) if minimum else out


@dataclass(frozen=True)
class GlobalKnobs:
    # Combate
    hp_mult: float = 1.0       # maxHp de unidades, curaciones, equipo +MaxHp
    dmg_mult: float = 1.0      # daño de ataques/acciones, veneno, retaliate, aura, furia, equipo +Damage

    # Costos de cartas (piso 1)
    cost_mult: float = 1.0

    # Economía de recursos
    base_prod_mult: float = 1.0     # producción base +1/turno de cada recurso
    initial_mult: float = 1.0       # recursos iniciales (5 c/u)
    producer_mult: float = 1.0      # output de unidades productoras (+1/turno)

    # Anti-stalemate (backstop; el sim puede barrerlos pero no son el target de duración)
    sudden_death_start: int = 50
    max_turns: int = 120

    # Inflación (mecánica de juego, no knob de balance): a partir de inflation_start_turn
    # (medios-turnos) las cartas cuestan inflation_pct_per_turn % más, acumulativo por medio-turno.
    # 0 = desactivada.
    inflation_start_turn: int = 0
    inflation_pct_per_turn: int = 0

    # Reglas anti-ventaja-de-iniciativa
    first_no_attack_t1: bool = False   # el primer jugador no puede atacar en el turno 1
    first_produces_t1: bool = False    # el primer jugador SÍ produce en el turno 1 (quita el castigo de producción)

    def label(self) -> str:
        infl = (f" infl@{self.inflation_start_turn}+{self.inflation_pct_per_turn}%"
                if self.inflation_start_turn else "")
        return (f"hp={self.hp_mult:g} dmg={self.dmg_mult:g} cost={self.cost_mult:g} "
                f"base={self.base_prod_mult:g} init={self.initial_mult:g} prod={self.producer_mult:g} "
                f"no_atk_t1={self.first_no_attack_t1} prod_t1={self.first_produces_t1}{infl}")


# ── Config ADOPTADA (la que shippea el juego) ────────────────────────────────
#
# REWORK de cartas: los valores base de cards.py son ahora los números FINALES del spec §9/§10
# (= Core/CardLibrary.cs), así que hp/dmg/producer van en 1.0 (sin tune global; el balance se
# afina carta por carta, playtest-driven). Sólo cost_mult ×1.2 (bump económico, espejo de
# CardLibrary.CostScale) e inflación/reglas de iniciativa difieren del baseline. parity_check.py
# verifica cards.py × SHIPPED == catálogo del Core. El sweep puede variar hp/dmg para explorar.
SHIPPED = GlobalKnobs(
    hp_mult=1.0,
    dmg_mult=1.0,
    producer_mult=1.0,
    cost_mult=1.2,                 # bump económico global uniforme (espejo de CardLibrary.CostScale)
    first_no_attack_t1=True,
    first_produces_t1=True,
    inflation_start_turn=8,        # medios-turnos; arranca temprano, se ve en casi toda partida
    inflation_pct_per_turn=5,      # +5% acumulativo por medio-turno desde ahí
)

# Config "cruda": todo en 1.0 y sin reglas de iniciativa (valores base de cards.py tal cual).
BASELINE = GlobalKnobs()
