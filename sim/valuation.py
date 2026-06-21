"""Pricing por eficiencia valor/costo — Fase 1 del balanceo per-card.

Valúa cada carta de forma DETERMINISTA (sin correr partidas) con la heurística del
spec §16.1 — la MISMA que usa `policy.py` para que sea consistente con lo que el bot
percibe — divide por su costo total, y rankea para detectar outliers: las de mayor
valor/costo son las "más fuertes" (subvaluadas) → candidatas a ENCARECER.

Escala de valor ("puntos de combate"):
  - Unidades y Equipo: HP/4 + daño_total/2 + valor_pasiva (§16.1, vía policy).
  - Acciones: se valúa cada efecto en esa misma escala con supuestos DOCUMENTADOS
    (constantes abajo, tuneables). La comparación más firme es DENTRO de cada
    categoría ("entre sí"); el ranking global es orientativo.

Limitación (asumida): el valor de las acciones es teórico y de un solo tiro, no
captura economía compuesta ni sinergias. La Fase 2 (reporte de fuerza per-card,
empírico: win-rate/pick-rate) valida y afina estos números.

Uso (Python via `py`):
  py sim/valuation.py            # tabla por facción/categoría + lista a ENCARECER (SHIPPED)
  py sim/valuation.py --baseline # sobre los valores crudos
"""

from __future__ import annotations

import argparse
import dataclasses
import math
import sys
from statistics import median
from typing import Dict, List

# La consola de Windows suele ser cp1252; forzamos UTF-8 para los acentos/§.
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

from cards import build_pool
from knobs import BASELINE, SHIPPED, GlobalKnobs
from model import (
    ActionCardData, CardData, CardEffectType, EquipmentCardData, Faction,
    PLAYER_STATUSES, StatusType, TargetType, UnitCardData,
)
from policy import _passive_value, base_unit_value


# ── Constantes de valuación de acciones (documentadas, tuneables) ─────────────
#
# Llevan cada efecto a la escala de "puntos de combate" (mismo eje que HP/4 + daño/2).
ECON_TO_COMBAT = 0.5     # 1 punto de recurso ganado ≈ 0.5 puntos de combate
DRAIN_FACTOR = 0.5       # drenar recurso al rival vale la mitad que ganarlo uno
DAMAGE_TO_VALUE = 0.5    # daño directo X ≈ X/2 (igual que el daño de unidad en §16.1)
HEAL_TO_VALUE = 0.25     # cura/defensa X ≈ X/4 (igual que el HP en §16.1)
POISON_TO_VALUE = 0.5    # veneno total (value·counter) ≈ daño → /2
DESMOR_FACTOR = 0.4      # −daño proyectado (igual que policy §16.3)
FURIA_FACTOR = 0.5       # +daño proyectado (igual que policy §16.3)
PROD_TURN_VALUE = 5.0    # doblar/saltear una producción ≈ un turno de economía
STUN_VALUE = 5.0         # aturdir 1 turno ≈ negar ~1 ataque fuerte
MOVE_SWAP_VALUE = 1.0    # reposicionar: utilidad situacional baja


def action_value(card: ActionCardData) -> float:
    """Valor intrínseco de una acción, sumando sus efectos (hoy 1 relevante c/u)."""
    total = 0.0
    for eff in card.effects:
        total += _effect_value(eff)
    return total


def _effect_value(eff) -> float:
    et = eff.effect_type
    if et == CardEffectType.MODIFY_RESOURCE:
        if eff.target == TargetType.SELF and eff.value > 0:
            return eff.value * ECON_TO_COMBAT
        return abs(eff.value) * ECON_TO_COMBAT * DRAIN_FACTOR    # drenaje al rival
    if et == CardEffectType.MODIFY_HP:
        if eff.value < 0:
            return abs(eff.value) * DAMAGE_TO_VALUE              # daño directo
        return eff.value * HEAL_TO_VALUE                          # cura/defensa
    if et == CardEffectType.APPLY_STATUS and eff.status is not None:
        st = eff.status
        if st.status_type in PLAYER_STATUSES:
            return PROD_TURN_VALUE                                # Double/Skip producción
        if st.status_type == StatusType.STUN:
            return STUN_VALUE
        if st.status_type == StatusType.POISON:
            return st.value * st.counter * POISON_TO_VALUE
        if st.status_type == StatusType.DESMORALIZAR:
            return st.value * st.counter * DESMOR_FACTOR
        if st.status_type == StatusType.FURIA:
            return st.value * st.counter * FURIA_FACTOR
        return 0.0
    if et in (CardEffectType.MOVE_UNIT, CardEffectType.SWAP_UNITS):
        return MOVE_SWAP_VALUE
    return 0.0


def equip_value(card: EquipmentCardData) -> float:
    return sum(m.value for m in card.stat_modifiers) + _passive_value(card.granted_passives)


def card_value(card: CardData) -> float:
    if isinstance(card, UnitCardData):
        return base_unit_value(card)
    if isinstance(card, EquipmentCardData):
        return equip_value(card)
    if isinstance(card, ActionCardData):
        return action_value(card)
    return 0.0


def total_cost(card: CardData) -> int:
    return sum(c.amount for c in card.costs)


def cost_label(card: CardData) -> str:
    return "+".join(f"{c.amount}{c.resource.value[:3]}" for c in card.costs)


def category_of(card: CardData) -> str:
    if isinstance(card, UnitCardData):
        return f"Unidad·{card.archetype}"
    if isinstance(card, EquipmentCardData):
        return "Equipo"
    if isinstance(card, ActionCardData):
        return f"Acción·{card.category}"
    return "?"


def broad_category(card: CardData) -> str:
    if isinstance(card, UnitCardData):
        return "Unidad"
    if isinstance(card, EquipmentCardData):
        return "Equipo"
    return "Acción"


# ── Análisis ──────────────────────────────────────────────────────────────────

@dataclasses.dataclass
class Row:
    card: CardData
    value: float
    cost: int
    eff: float          # value / cost
    cat: str
    broad: str


def build_rows(k: GlobalKnobs) -> List[Row]:
    rows: List[Row] = []
    for fac in (Faction.MANIFESTANTES, Faction.POLICIAS):
        for c in build_pool(fac, k):
            cost = total_cost(c)
            val = card_value(c)
            rows.append(Row(c, val, cost, val / cost if cost else float("inf"),
                            category_of(c), broad_category(c)))
    return rows


def suggest_cost(row: Row, target_eff: float) -> int:
    """Costo que llevaría la eficiencia de la carta a `target_eff` (sólo encarece)."""
    if target_eff <= 0:
        return row.cost
    raw = math.ceil(row.value / target_eff - 1e-9)
    return max(row.cost, raw)


def main():
    ap = argparse.ArgumentParser(description="Pricing por eficiencia valor/costo (Fase 1)")
    ap.add_argument("--baseline", action="store_true",
                    help="usar valores crudos (knobs 1.0) en vez de los que shippean")
    ap.add_argument("--target", type=float, default=None,
                    help="eficiencia objetivo para sugerir costos (default: mediana por categoría amplia)")
    ap.add_argument("--flag", type=float, default=1.20,
                    help="factor sobre la mediana de su categoría para marcar una carta como fuerte")
    args = ap.parse_args()

    k = BASELINE if args.baseline else SHIPPED
    rows = build_rows(k)

    # Medianas de eficiencia por categoría amplia (Unidad / Acción / Equipo).
    by_broad: Dict[str, List[Row]] = {}
    for r in rows:
        by_broad.setdefault(r.broad, []).append(r)
    broad_median = {b: median(r.eff for r in rs) for b, rs in by_broad.items()}

    print(f"# Pricing valor/costo  ({k.label()})")
    print(f"# valor = §16.1 (HP/4 + daño/2 + pasiva); acciones por efecto (ver valuation.py)\n")

    for fac in (Faction.MANIFESTANTES, Faction.POLICIAS):
        print(f"## {fac.value}")
        fac_rows = [r for r in rows if r.card.faction == fac]
        # Ordenar por categoría amplia y luego por eficiencia desc.
        for broad in ("Unidad", "Acción", "Equipo"):
            sub = sorted((r for r in fac_rows if r.broad == broad),
                         key=lambda r: -r.eff)
            if not sub:
                continue
            med = broad_median[broad]
            print(f"  — {broad} (mediana eff {med:.2f}) —")
            print(f"    {'carta':24} {'costo':7} {'valor':>6} {'eff':>6}  {'cat'}")
            for r in sub:
                mark = "  <== fuerte" if r.eff >= med * args.flag else ""
                print(f"    {r.card.name:24} {cost_label(r.card):7} {r.value:6.1f} "
                      f"{r.eff:6.2f}  {r.cat}{mark}")
            print()

    # Comparación cruzada por arquetipo/categoría: Manif vs Pol lado a lado. Acá vive el
    # "balancear entre sí" — dos cartas que cumplen el MISMO rol deberían tener eff parecida.
    print("=" * 72)
    print("COMPARACIÓN CRUZADA (mismo rol, Manif vs Pol):\n")
    by_cat: Dict[str, List[Row]] = {}
    for r in rows:
        by_cat.setdefault(r.cat, []).append(r)
    print(f"  {'rol':22} {'Manifestantes':>26}   {'Policías':>26}")
    for cat in sorted(by_cat):
        m = next((r for r in by_cat[cat] if r.card.faction == Faction.MANIFESTANTES), None)
        p = next((r for r in by_cat[cat] if r.card.faction == Faction.POLICIAS), None)
        def cell(r: Row) -> str:
            if r is None:
                return f"{'—':>26}"
            return f"{r.card.name[:14]:14} {cost_label(r.card):6} e{r.eff:4.1f}"
        gap = ""
        if m and p and min(m.eff, p.eff) > 0:
            ratio = max(m.eff, p.eff) / min(m.eff, p.eff)
            if ratio >= 1.4:
                stronger = "M" if m.eff > p.eff else "P"
                gap = f"  <-- {stronger} +{(ratio-1)*100:.0f}%"
        print(f"  {cat[:22]:22} {cell(m):>26}   {cell(p):>26}{gap}")
    print()

    # Lista de candidatas a ENCARECER: eff por encima de la mediana de su categoría amplia.
    print("=" * 72)
    print("CANDIDATAS A ENCARECER (eff > mediana de su categoría × factor):\n")
    print(f"  {'carta':24} {'costo':7} {'eff':>6}   sugerido(-> mediana)")
    flagged = [r for r in rows if r.eff >= broad_median[r.broad] * args.flag]
    flagged.sort(key=lambda r: -r.eff)
    for r in flagged:
        target = args.target if args.target is not None else broad_median[r.broad]
        new_cost = suggest_cost(r, target)
        res = r.card.costs[0].resource.value[:3] if r.card.costs else "?"
        arrow = f"{new_cost}{res}" if new_cost != r.cost else "(=)"
        print(f"  {r.card.name:24} {cost_label(r.card):7} {r.eff:6.2f}   {arrow}")


if __name__ == "__main__":
    main()
