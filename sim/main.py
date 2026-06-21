"""CLI del simulador de balance de PiqueteDefend.

Uso (Python via `py` en esta máquina):
  py sim/main.py run                      # 1 batch con la config ADOPTADA (la que shippea), verbose
  py sim/main.py run --n 1000             # batch grande
  py sim/main.py run --hp 1.2 --dmg 0.9   # override de knobs sobre la config adoptada
  py sim/main.py run --baseline           # config "cruda" (todo 1.0, sin reglas de iniciativa)
  py sim/main.py sweep                    # barrido de knobs globales, ranking por score
  py sim/main.py game --seed 7            # traza de una sola partida (debug)

Por default, run/game/dump usan knobs.SHIPPED (hp 0.85 / dmg 1.5 + reglas de iniciativa): así
reproducen el juego que ship­ea el Core. Los `--hp/--dmg/...` y `--no-attack-t1/--produces-t1`
sobreescriben puntualmente; `--baseline` parte de la config cruda (todo 1.0, sin reglas).

Conteo de turnos: MEDIOS-TURNOS (1 por turno de jugador), igual que el spec.
"""

from __future__ import annotations

import argparse
import dataclasses
import itertools
from typing import List

from knobs import BASELINE, SHIPPED, GlobalKnobs
from sweep import (BatchResult, combo_score, format_batch, play_game, run_batch)


def knobs_from_args(args) -> GlobalKnobs:
    """Parte de SHIPPED (o BASELINE con --baseline) y aplica sólo los overrides presentes."""
    base = BASELINE if getattr(args, "baseline", False) else SHIPPED
    overrides = {}
    for name, attr in (("hp_mult", "hp"), ("dmg_mult", "dmg"), ("cost_mult", "cost"),
                       ("base_prod_mult", "base"), ("initial_mult", "init"),
                       ("producer_mult", "prod"), ("sudden_death_start", "sudden"),
                       ("max_turns", "maxturns"), ("first_no_attack_t1", "no_attack_t1"),
                       ("first_produces_t1", "produces_t1")):
        val = getattr(args, attr, None)
        if val is not None:
            overrides[name] = val
    return dataclasses.replace(base, **overrides)


def cmd_run(args):
    knobs = knobs_from_args(args)
    r = run_batch(knobs, n=args.n, paired=args.paired)
    print(format_batch(r, verbose=True))


def cmd_game(args):
    knobs = knobs_from_args(args)
    e = play_game(knobs, seed=args.seed, first_index=args.seed % 2)
    o = e.outcome
    print(f"seed={args.seed}  ganador={o.winner.value if o.winner else 'EMPATE'}  "
          f"condición={o.condition}  medios-turnos={o.half_turns}")
    print("cartas jugadas:", dict(sorted(e.cards_played.items())))
    print("deploys:", e.deploys_by_arch)
    print("muertes:", e.deaths_by_arch)


def cmd_dump(args):
    """Imprime el catálogo con los knobs ya aplicados. Con la config ADOPTADA (default) = los
    números finales horneados en spec §9/§10 y Core/CardLibrary.cs."""
    from cards import build_pool
    from model import (ActionCardData, EquipmentCardData, Faction, UnitCardData)
    knobs = knobs_from_args(args)
    print(f"# Catálogo horneado con {knobs.label()}")
    print(f"# Recursos iniciales: {int(round(5*knobs.initial_mult))} c/u  |  "
          f"Producción base: {int(round(1*knobs.base_prod_mult))}/turno\n")
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
    # default None = "no override": knobs_from_args parte de SHIPPED (o BASELINE con --baseline).
    p.add_argument("--baseline", action="store_true",
                   help="partir de la config cruda (todo 1.0, sin reglas de iniciativa)")
    p.add_argument("--hp", type=float, default=None)
    p.add_argument("--dmg", type=float, default=None)
    p.add_argument("--cost", type=float, default=None)
    p.add_argument("--base", type=float, default=None)
    p.add_argument("--init", type=float, default=None)
    p.add_argument("--prod", type=float, default=None)
    p.add_argument("--sudden", type=int, default=None)
    p.add_argument("--maxturns", type=int, default=None)
    p.add_argument("--no-attack-t1", dest="no_attack_t1", action=argparse.BooleanOptionalAction,
                   default=None, help="el primer jugador no puede atacar en el turno 1")
    p.add_argument("--produces-t1", dest="produces_t1", action=argparse.BooleanOptionalAction,
                   default=None, help="el primer jugador SÍ produce en el turno 1")


def main():
    ap = argparse.ArgumentParser(description="Simulador de balance de PiqueteDefend")
    sub = ap.add_subparsers(dest="cmd", required=True)

    pr = sub.add_parser("run", help="un batch con la config adoptada (o knobs override)")
    add_knob_args(pr)
    pr.add_argument("--n", type=int, default=500)
    pr.add_argument("--paired", action="store_true",
                    help="modo pareado: cada seed juega first=0 y first=1 (2n partidas, balance de facción más preciso)")
    pr.set_defaults(func=cmd_run)

    pg = sub.add_parser("game", help="una sola partida (debug)")
    add_knob_args(pg)
    pg.add_argument("--seed", type=int, default=0)
    pg.set_defaults(func=cmd_game)

    ps = sub.add_parser("sweep", help="barrido de knobs globales")
    ps.add_argument("--n", type=int, default=300)
    ps.set_defaults(func=cmd_sweep)

    pd = sub.add_parser("dump", help="catálogo con knobs aplicados (default = números finales)")
    add_knob_args(pd)
    pd.set_defaults(func=cmd_dump)

    args = ap.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
