"""CLI del simulador de balance de PiqueteDefend.

Uso (Python via `py` en esta máquina):
  py sim/main.py run                      # 1 batch con knobs default, verbose
  py sim/main.py run --n 1000             # batch grande
  py sim/main.py run --hp 1.2 --dmg 0.9   # batch con knobs custom
  py sim/main.py sweep                    # barrido de knobs globales, ranking por score
  py sim/main.py game --seed 7            # traza de una sola partida (debug)

Conteo de turnos: MEDIOS-TURNOS (1 por turno de jugador), igual que el spec.
"""

from __future__ import annotations

import argparse
import itertools
from typing import List

from knobs import GlobalKnobs
from sweep import (BatchResult, combo_score, format_batch, play_game, run_batch)


def cmd_run(args):
    knobs = GlobalKnobs(
        hp_mult=args.hp, dmg_mult=args.dmg, cost_mult=args.cost,
        base_prod_mult=args.base, initial_mult=args.init, producer_mult=args.prod,
        sudden_death_start=args.sudden, max_turns=args.maxturns,
        first_no_attack_t1=args.no_attack_t1, first_produces_t1=args.produces_t1,
    )
    r = run_batch(knobs, n=args.n)
    print(format_batch(r, verbose=True))


def cmd_game(args):
    knobs = GlobalKnobs(hp_mult=args.hp, dmg_mult=args.dmg, cost_mult=args.cost,
                        base_prod_mult=args.base, initial_mult=args.init, producer_mult=args.prod)
    e = play_game(knobs, seed=args.seed, first_index=args.seed % 2)
    o = e.outcome
    print(f"seed={args.seed}  ganador={o.winner.value if o.winner else 'EMPATE'}  "
          f"condición={o.condition}  medios-turnos={o.half_turns}")
    print("cartas jugadas:", dict(sorted(e.cards_played.items())))
    print("deploys:", e.deploys_by_arch)
    print("muertes:", e.deaths_by_arch)


def cmd_dump(args):
    """Imprime el catálogo con los knobs ya aplicados = números finales para spec/CardLibrary."""
    from cards import build_pool
    from model import (ActionCardData, EquipmentCardData, Faction, UnitCardData)
    knobs = GlobalKnobs(hp_mult=args.hp, dmg_mult=args.dmg, cost_mult=args.cost,
                        base_prod_mult=args.base, initial_mult=args.init, producer_mult=args.prod)
    print(f"# Catálogo horneado con {knobs.label()}")
    print(f"# Recursos iniciales: {int(round(5*args.init))} c/u  |  Producción base: {int(round(1*args.base))}/turno\n")
    for fac in (Faction.MANIFESTANTES, Faction.POLICIAS):
        print(f"## {fac.value}")
        for c in build_pool(fac, knobs):
            cost = "+".join(f"{rc.amount}{rc.resource.value[:3]}" for rc in c.costs)
            if isinstance(c, UnitCardData):
                a = c.attack
                eff = "cura" if a.effect.value == "HealAllies" else "dmg"
                slots = "".join(str(s) for s in a.pattern)
                print(f"  {c.name:22} {cost:7} HP {c.max_hp:3}  {a.reference.value[:3]}[{slots}]"
                      f"x{a.pick_count} {eff}{a.amount_per_slot}  [{c.archetype}]")
            elif isinstance(c, ActionCardData):
                desc = "; ".join(f"{e.effect_type.value} v={e.value}"
                                 + (f" {e.status.status_type.value}({e.status.value}/{e.status.counter})" if e.status else "")
                                 for e in c.effects)
                print(f"  {c.name:22} {cost:7} {desc}")
            elif isinstance(c, EquipmentCardData):
                mods = ", ".join(f"{m.stat.value}+{m.value}" for m in c.stat_modifiers)
                pas = ", ".join(p.passive_type.value + f"({p.value})" for p in c.granted_passives)
                print(f"  {c.name:22} {cost:7} {mods or pas}")
        print()


def cmd_sweep(args):
    # Grilla enfocada: la duración la mueve la razón hp/dmg y la economía.
    hp_opts = [1.0, 1.25, 1.5]
    dmg_opts = [0.8, 1.0, 1.2]
    cost_opts = [1.0]
    base_opts = [1.0, 2.0]
    init_opts = [1.0]
    prod_opts = [1.0]

    combos: List[GlobalKnobs] = []
    for hp, dmg, cost, base, init, prod in itertools.product(
            hp_opts, dmg_opts, cost_opts, base_opts, init_opts, prod_opts):
        combos.append(GlobalKnobs(hp_mult=hp, dmg_mult=dmg, cost_mult=cost,
                                  base_prod_mult=base, initial_mult=init, producer_mult=prod))

    results = []
    for i, k in enumerate(combos):
        r = run_batch(k, n=args.n)
        results.append((combo_score(r), r))
        print(f"[{i+1}/{len(combos)}] {format_batch(r)}\n")

    results.sort(key=lambda x: x[0])
    print("=" * 78)
    print("TOP 5 combos (menor score = mejor balance+duración):\n")
    for score, r in results[:5]:
        print(f"score={score:5.1f}")
        print(format_batch(r, verbose=False))
        print()


def add_knob_args(p):
    p.add_argument("--hp", type=float, default=1.0)
    p.add_argument("--dmg", type=float, default=1.0)
    p.add_argument("--cost", type=float, default=1.0)
    p.add_argument("--base", type=float, default=1.0)
    p.add_argument("--init", type=float, default=1.0)
    p.add_argument("--prod", type=float, default=1.0)


def main():
    ap = argparse.ArgumentParser(description="Simulador de balance de PiqueteDefend")
    sub = ap.add_subparsers(dest="cmd", required=True)

    pr = sub.add_parser("run", help="un batch con knobs dados")
    add_knob_args(pr)
    pr.add_argument("--n", type=int, default=500)
    pr.add_argument("--sudden", type=int, default=50)
    pr.add_argument("--maxturns", type=int, default=120)
    pr.add_argument("--no-attack-t1", action="store_true",
                    help="el primer jugador no puede atacar en el turno 1")
    pr.add_argument("--produces-t1", action="store_true",
                    help="el primer jugador SÍ produce en el turno 1")
    pr.set_defaults(func=cmd_run)

    pg = sub.add_parser("game", help="una sola partida (debug)")
    add_knob_args(pg)
    pg.add_argument("--seed", type=int, default=0)
    pg.set_defaults(func=cmd_game)

    ps = sub.add_parser("sweep", help="barrido de knobs globales")
    ps.add_argument("--n", type=int, default=300)
    ps.set_defaults(func=cmd_sweep)

    pd = sub.add_parser("dump", help="catálogo con knobs aplicados (números finales)")
    add_knob_args(pd)
    pd.set_defaults(func=cmd_dump)

    args = ap.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
