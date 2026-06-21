"""Harness de simulación: corre partidas, agrega métricas y barre knobs (Fase 5).

Métricas reportadas (spec §12 / objetivos de la sesión):
  · win-rate por facción + empates
  · media/mediana de MEDIOS-TURNOS por partida + distribución
  · % de partidas que llegan a muerte súbita, % que tocan maxTurns
  · uso de cartas
  · 3 riesgos: flujo de recursos (starved), Sniper anti-eco (muerte de productoras),
    Emisores (valor gratis aportado, vía deploy/uso)
"""

from __future__ import annotations

import statistics
from dataclasses import dataclass, field
from typing import Dict, List

from knobs import GlobalKnobs
from model import Faction
from policy import take_turn
from rules import GameEngine, Rng

M, P = Faction.MANIFESTANTES, Faction.POLICIAS


def play_game(knobs: GlobalKnobs, seed: int, first_index: int) -> GameEngine:
    rng = Rng(seed)
    engine = GameEngine(knobs, rng, M, P)
    engine.start(first_index)
    guard = 0
    while not engine.is_finished:
        engine.begin_turn()
        if engine.is_finished:
            break
        take_turn(engine)
        if engine.is_finished:
            break
        engine.end_turn()
        guard += 1
        if guard > knobs.max_turns + 10:   # red de seguridad (no debería dispararse)
            break
    return engine


@dataclass
class BatchResult:
    knobs: GlobalKnobs
    n: int
    wins_m: int = 0
    wins_p: int = 0
    draws: int = 0
    wins_first: int = 0     # ganó el que arrancó
    wins_second: int = 0    # ganó el segundo
    half_turns: List[int] = field(default_factory=list)
    cond: Dict[str, int] = field(default_factory=dict)
    cards: Dict[str, int] = field(default_factory=dict)
    starved_turns: int = 0
    action_turns: int = 0
    deploys: Dict[str, int] = field(default_factory=dict)
    deaths: Dict[str, int] = field(default_factory=dict)

    # ── Derivadas ──
    @property
    def wr_m(self) -> float:
        return self.wins_m / self.n

    @property
    def wr_p(self) -> float:
        return self.wins_p / self.n

    @property
    def wr_draw(self) -> float:
        return self.draws / self.n

    @property
    def wr_first(self) -> float:
        decisive = self.wins_first + self.wins_second
        return self.wins_first / decisive if decisive else 0.0

    @property
    def mean_turns(self) -> float:
        return statistics.mean(self.half_turns) if self.half_turns else 0.0

    @property
    def median_turns(self) -> float:
        return statistics.median(self.half_turns) if self.half_turns else 0.0

    @property
    def pct_sudden_death(self) -> float:
        s = self.knobs.sudden_death_start
        return sum(1 for t in self.half_turns if t >= s) / self.n

    @property
    def pct_timeout(self) -> float:
        return self.cond.get("Timeout", 0) / self.n

    @property
    def starved_ratio(self) -> float:
        return self.starved_turns / self.action_turns if self.action_turns else 0.0

    def death_rate(self, arch: str) -> float:
        d = self.deploys.get(arch, 0)
        return self.deaths.get(arch, 0) / d if d else 0.0


def run_batch(knobs: GlobalKnobs, n: int = 400) -> BatchResult:
    r = BatchResult(knobs=knobs, n=n)
    for seed in range(n):
        first = seed % 2   # alterna quién arranca para neutralizar la ventaja de iniciativa
        e = play_game(knobs, seed, first)
        o = e.outcome
        if o is None:
            continue
        if o.winner is None:
            r.draws += 1
        elif o.winner == M:
            r.wins_m += 1
        else:
            r.wins_p += 1
        if o.winner is not None:
            winner_idx = 0 if o.winner == M else 1   # M=player0, P=player1 (lados fijos)
            if winner_idx == first:
                r.wins_first += 1
            else:
                r.wins_second += 1
        r.half_turns.append(o.half_turns)
        r.cond[o.condition] = r.cond.get(o.condition, 0) + 1
        for cid, c in e.cards_played.items():
            r.cards[cid] = r.cards.get(cid, 0) + c
        r.starved_turns += e.starved_turns
        r.action_turns += e.action_turns
        for a, c in e.deploys_by_arch.items():
            r.deploys[a] = r.deploys.get(a, 0) + c
        for a, c in e.deaths_by_arch.items():
            r.deaths[a] = r.deaths.get(a, 0) + c
    return r


def histogram(values: List[int], width: int = 40) -> str:
    if not values:
        return "(sin datos)"
    buckets = [(0, 14), (15, 24), (25, 34), (35, 44), (45, 60), (61, 999)]
    labels = ["<15", "15-24", "25-34", "35-44", "45-60", "60+"]
    counts = [sum(1 for v in values if lo <= v <= hi) for lo, hi in buckets]
    mx = max(counts) or 1
    out = []
    for lbl, c in zip(labels, counts):
        bar = "#" * int(width * c / mx)
        out.append(f"  {lbl:>6} | {bar} {c} ({100*c/len(values):.0f}%)")
    return "\n".join(out)


def format_batch(r: BatchResult, verbose: bool = False) -> str:
    lines = [
        f"knobs: {r.knobs.label()}  (n={r.n})",
        f"  win-rate  Manif {r.wr_m*100:5.1f}%   Pol {r.wr_p*100:5.1f}%   empate {r.wr_draw*100:4.1f}%",
        f"  iniciativa  gana-el-1ro {r.wr_first*100:5.1f}%  (50% = sin ventaja)",
        f"  turnos    media {r.mean_turns:5.1f}   mediana {r.median_turns:5.1f}   "
        f"min {min(r.half_turns)} max {max(r.half_turns)}",
        f"  fin       KO {r.cond.get('KO',0)/r.n*100:4.0f}%   "
        f"muerte-súbita-alcanzada {r.pct_sudden_death*100:4.0f}%   timeout {r.pct_timeout*100:4.0f}%",
        f"  riesgos   starved {r.starved_ratio*100:4.1f}%   "
        f"muerte Productora {r.death_rate('Productora')*100:3.0f}%   "
        f"deploy Sniper {r.deploys.get('Sniper',0)} Emisor {r.deploys.get('Emisor',0)}",
    ]
    if verbose:
        lines.append("  distribución de turnos:")
        lines.append(histogram(r.half_turns))
        top = sorted(r.cards.items(), key=lambda kv: -kv[1])[:8]
        lines.append("  cartas más jugadas: " + ", ".join(f"{k}={v}" for k, v in top))
    return "\n".join(lines)


# ── Score de "qué tan bueno es este combo" para el target de duración ─────────

TARGET_LOW, TARGET_HIGH, TARGET_MID = 30.0, 40.0, 35.0


def combo_score(r: BatchResult) -> float:
    """Menor = mejor. Penaliza desbalance de facción, duración fuera del target y exceso de timeouts."""
    balance_pen = abs(r.wr_m - 0.5) * 100        # 0 = 50/50
    dur_pen = abs(r.mean_turns - TARGET_MID) * 2
    if r.mean_turns < TARGET_LOW or r.mean_turns > TARGET_HIGH:
        dur_pen += 15
    timeout_pen = r.pct_timeout * 60             # casi nada debería tocar maxTurns
    starved_pen = max(0.0, r.starved_ratio - 0.25) * 80
    return balance_pen + dur_pen + timeout_pen + starved_pen
