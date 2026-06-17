"""
PiqueteDefend Balance Simulator
Simulates N games between AI players and reports balance metrics.

Usage:
    python simulator.py                        # 10 000 greedy vs greedy
    python simulator.py --games 5000           # custom game count
    python simulator.py --ai random            # both players random
    python simulator.py --ai1 greedy --ai2 random
"""

import random
import copy
import argparse
from enum import Enum, auto
from dataclasses import dataclass, field
from typing import Optional, List, Tuple
from collections import defaultdict
import statistics


# ──────────────────────────────────────────────────────────────────────────────
# Enums
# ──────────────────────────────────────────────────────────────────────────────

class Faction(Enum):
    MANIFESTANTES = "Manifestantes"
    POLICIAS = "Policias"


class CardType(Enum):
    ACCION = auto()
    UNIDAD = auto()


class UnitSubtype(Enum):
    ATACANTE = auto()
    DEFENSIVA = auto()
    PRODUCTORA = auto()


class ResourceType(Enum):
    DINERO = "dinero"
    FUERZA = "fuerza"
    SOCIAL = "social"


class CardEffectType(Enum):
    MODIFY_HP = auto()
    MODIFY_RESOURCE = auto()
    REMOVE_UNIT = auto()
    APPLY_STATUS = auto()


class TargetType(Enum):
    SELF = auto()
    OPPONENT = auto()


class StatusType(Enum):
    SKIP_PRODUCTION = auto()
    DOUBLE_PRODUCTION = auto()


class WinCondition(Enum):
    KO = "KO"
    SOCIAL = "Hegemonía Social"
    ECONOMICO = "Poder Económico"
    TIMEOUT = "Timeout"


# ──────────────────────────────────────────────────────────────────────────────
# Data classes
# ──────────────────────────────────────────────────────────────────────────────

@dataclass
class StatusEffect:
    status_type: StatusType
    value: int      # e.g. multiplier for DOUBLE_PRODUCTION (=2)
    counter: int    # turns of the affected player until fire


@dataclass
class CardEffect:
    effect_type: CardEffectType
    target: TargetType
    resource_target: Optional[ResourceType] = None
    value: int = 0
    status: Optional[StatusEffect] = None


@dataclass
class CardData:
    id: str
    card_name: str
    faction: Faction
    card_type: CardType
    cost_dinero: int = 0
    cost_fuerza: int = 0
    cost_social: int = 0
    unit_subtype: Optional[UnitSubtype] = None
    production_resource: Optional[ResourceType] = None
    effects: List[CardEffect] = field(default_factory=list)


@dataclass
class UnitSlot:
    unit_data: CardData
    count: int = 1


# ──────────────────────────────────────────────────────────────────────────────
# Card pool
# ──────────────────────────────────────────────────────────────────────────────

def _build_cards():
    D, F, S = ResourceType.DINERO, ResourceType.FUERZA, ResourceType.SOCIAL
    M, P = Faction.MANIFESTANTES, Faction.POLICIAS
    ACT, UNIT = CardType.ACCION, CardType.UNIDAD
    ATK, DEF, PROD = UnitSubtype.ATACANTE, UnitSubtype.DEFENSIVA, UnitSubtype.PRODUCTORA
    SELF, OPP = TargetType.SELF, TargetType.OPPONENT
    MHP, MRES, RUNIT, ASTAT = (CardEffectType.MODIFY_HP, CardEffectType.MODIFY_RESOURCE,
                                CardEffectType.REMOVE_UNIT, CardEffectType.APPLY_STATUS)
    SKIP = StatusType.SKIP_PRODUCTION
    DBLE = StatusType.DOUBLE_PRODUCTION

    def ce(et, tgt, res=None, val=0, st=None):
        return CardEffect(et, tgt, res, val, st)

    def skip_status():
        return StatusEffect(SKIP, 0, 1)

    def double_status():
        return StatusEffect(DBLE, 2, 1)

    cards = [
        # ── MANIFESTANTES ─────────────────────────────────────────────────────
        # Units
        CardData("piquetero",       "Piquetero",        M, UNIT, cost_dinero=2, cost_fuerza=2, unit_subtype=ATK),
        CardData("escudo_humano",   "Escudo Humano",    M, UNIT, cost_dinero=5, cost_fuerza=1, unit_subtype=DEF),
        CardData("olla_popular",    "Olla Popular",     M, UNIT, cost_dinero=2, cost_social=1,  unit_subtype=PROD, production_resource=D),
        CardData("fogon",           "Fogón",            M, UNIT, cost_fuerza=4, cost_social=1,  unit_subtype=PROD, production_resource=F),
        CardData("megafono",        "Megáfono",         M, UNIT, cost_dinero=1, cost_social=1,  unit_subtype=PROD, production_resource=S),
        # Boost
        CardData("colecta",         "Colecta",          M, ACT, cost_social=3,
                 effects=[ce(MRES, SELF, D, 6)]),
        CardData("adrenalina",      "Adrenalina",       M, ACT, cost_dinero=1,
                 effects=[ce(MRES, SELF, F, 3)]),
        CardData("viral_redes",     "Viral en Redes",   M, ACT, cost_dinero=2,
                 effects=[ce(MRES, SELF, S, 7)]),
        # Sabotaje
        CardData("saqueo",          "Saqueo",           M, ACT, cost_fuerza=1,
                 effects=[ce(MRES, OPP, D, -3)]),
        CardData("agotamiento",     "Agotamiento",      M, ACT, cost_dinero=2,
                 effects=[ce(MRES, OPP, F, -7)]),
        CardData("fake_news",       "Fake News",        M, ACT, cost_social=3,
                 effects=[ce(MRES, OPP, S, -5)]),
        CardData("romper_marcha",   "Romper la Marcha", M, ACT, cost_fuerza=1, cost_social=3,
                 effects=[ce(RUNIT, OPP, val=-1)]),
        # Ataque / Defensa
        CardData("paro_general",    "Paro General",     M, ACT, cost_dinero=2, cost_fuerza=3,
                 effects=[ce(MHP, OPP, val=-14)]),
        CardData("abrazo_colectivo","Abrazo Colectivo", M, ACT, cost_dinero=4, cost_social=1,
                 effects=[ce(MHP, SELF, val=16)]),
        # Especiales
        CardData("corte_ruta",      "Corte de Ruta",    M, ACT, cost_fuerza=1, cost_social=2,
                 effects=[ce(ASTAT, OPP, st=skip_status())]),
        CardData("asamblea_popular","Asamblea Popular", M, ACT, cost_social=6,
                 effects=[ce(ASTAT, SELF, st=double_status())]),

        # ── POLICÍAS ─────────────────────────────────────────────────────────
        # Units
        CardData("patrullero",      "Patrullero",           P, UNIT, cost_fuerza=4, cost_dinero=2, unit_subtype=ATK),
        CardData("comisaria",       "Comisaría",            P, UNIT, cost_dinero=2, cost_fuerza=1, unit_subtype=DEF),
        CardData("subsidio",        "Subsidio",             P, UNIT, cost_dinero=4, cost_social=1,  unit_subtype=PROD, production_resource=D),
        CardData("entrenamiento",   "Entrenamiento",        P, UNIT, cost_fuerza=1, cost_social=2,  unit_subtype=PROD, production_resource=F),
        CardData("conferencia",     "Conferencia de Prensa",P, UNIT, cost_dinero=3, cost_social=2,  unit_subtype=PROD, production_resource=S),
        # Boost
        CardData("partida",         "Partida Presupuestaria",P, ACT, cost_social=1,
                 effects=[ce(MRES, SELF, D, 7)]),
        CardData("refuerzo",        "Refuerzo",             P, ACT, cost_dinero=3,
                 effects=[ce(MRES, SELF, F, 8)]),
        CardData("cadena_nacional", "Cadena Nacional",      P, ACT, cost_fuerza=2,
                 effects=[ce(MRES, SELF, S, 4)]),
        # Sabotaje
        CardData("embargo",         "Embargo",              P, ACT, cost_fuerza=3,
                 effects=[ce(MRES, OPP, D, -7)]),
        CardData("detencion",       "Detención",            P, ACT, cost_dinero=1,
                 effects=[ce(MRES, OPP, F, -3)]),
        CardData("censura",         "Censura",              P, ACT, cost_social=2,
                 effects=[ce(MRES, OPP, S, -5)]),
        CardData("infiltrado",      "Infiltrado",           P, ACT, cost_dinero=3, cost_social=1,
                 effects=[ce(RUNIT, OPP, val=-1)]),
        # Ataque / Defensa
        CardData("operativo",       "Operativo Especial",   P, ACT, cost_dinero=4, cost_fuerza=2,
                 effects=[ce(MHP, OPP, val=-18)]),
        CardData("escudo_anti",     "Escudo Antidisturbios",P, ACT, cost_dinero=2, cost_social=3,
                 effects=[ce(MHP, SELF, val=12)]),
        # Especiales
        CardData("toque_queda",     "Toque de Queda",       P, ACT, cost_dinero=4, cost_fuerza=1,
                 effects=[ce(ASTAT, OPP, st=skip_status())]),
        CardData("decreto",         "Decreto de Emergencia",P, ACT, cost_dinero=3,
                 effects=[ce(ASTAT, SELF, st=double_status())]),
    ]
    return cards


ALL_CARDS = _build_cards()
POOL = {
    Faction.MANIFESTANTES: [c for c in ALL_CARDS if c.faction == Faction.MANIFESTANTES],
    Faction.POLICIAS:      [c for c in ALL_CARDS if c.faction == Faction.POLICIAS],
}


# ──────────────────────────────────────────────────────────────────────────────
# PlayerState
# ──────────────────────────────────────────────────────────────────────────────

BASE_PROD = {ResourceType.DINERO: 5, ResourceType.FUERZA: 3, ResourceType.SOCIAL: 2}
MAX_RESOURCE = 99
HP_INITIAL = 100
HAND_SIZE = 6
MAX_SLOTS = 3
MAX_STACK = 5


class PlayerState:
    def __init__(self, faction: Faction):
        self.faction = faction
        self.hp = HP_INITIAL
        self.dinero = 3
        self.fuerza = 2
        self.social = 1
        self.unit_slots: List[UnitSlot] = []
        self.active_statuses: List[StatusEffect] = []
        pool = POOL[faction]
        self.hand: List[CardData] = [random.choice(pool) for _ in range(HAND_SIZE)]

    # ── Resource helpers ──────────────────────────────────────────────────────

    def get_res(self, r: ResourceType) -> int:
        return getattr(self, r.value)

    def set_res(self, r: ResourceType, v: int):
        setattr(self, r.value, max(0, min(MAX_RESOURCE, v)))

    def add_res(self, r: ResourceType, delta: int):
        self.set_res(r, self.get_res(r) + delta)

    def can_afford(self, card: CardData) -> bool:
        return (self.dinero >= card.cost_dinero and
                self.fuerza >= card.cost_fuerza and
                self.social >= card.cost_social)

    def pay(self, card: CardData):
        self.dinero -= card.cost_dinero
        self.fuerza -= card.cost_fuerza
        self.social -= card.cost_social

    # ── Unit helpers ──────────────────────────────────────────────────────────

    def slot_for(self, unit_id: str) -> Optional[UnitSlot]:
        for s in self.unit_slots:
            if s.unit_data.id == unit_id:
                return s
        return None

    def unit_attack(self) -> int:
        return sum(s.count for s in self.unit_slots
                   if s.unit_data.unit_subtype == UnitSubtype.ATACANTE)

    def unit_defense(self) -> int:
        return sum(s.count for s in self.unit_slots
                   if s.unit_data.unit_subtype == UnitSubtype.DEFENSIVA)

    def unit_production(self) -> dict:
        prod = {r: 0 for r in ResourceType}
        for s in self.unit_slots:
            if s.unit_data.unit_subtype == UnitSubtype.PRODUCTORA:
                prod[s.unit_data.production_resource] += s.count
        return prod

    # ── Hand ──────────────────────────────────────────────────────────────────

    def replace_card(self, idx: int):
        self.hand[idx] = random.choice(POOL[self.faction])


# ──────────────────────────────────────────────────────────────────────────────
# Victory check
# ──────────────────────────────────────────────────────────────────────────────

def check_victory(active: PlayerState, opp: PlayerState) -> Optional[Tuple[Faction, WinCondition]]:
    """Priority: KO > Social ≥ 50 > Dinero ≥ 75. Returns (winner, condition) or None."""
    if opp.hp <= 0:
        return (active.faction, WinCondition.KO)
    if active.hp <= 0:
        return (opp.faction, WinCondition.KO)
    for player in (active, opp):
        if player.social >= 60:
            return (player.faction, WinCondition.SOCIAL)
    for player in (active, opp):
        if player.dinero >= 100:
            return (player.faction, WinCondition.ECONOMICO)
    return None


# ──────────────────────────────────────────────────────────────────────────────
# Effect resolution
# ──────────────────────────────────────────────────────────────────────────────

def resolve_effect(effect: CardEffect,
                   active: PlayerState,
                   opp: PlayerState,
                   remove_slot_idx: Optional[int] = None):
    tgt = active if effect.target == TargetType.SELF else opp

    if effect.effect_type == CardEffectType.MODIFY_HP:
        tgt.hp = max(0, tgt.hp + effect.value)

    elif effect.effect_type == CardEffectType.MODIFY_RESOURCE:
        tgt.add_res(effect.resource_target, effect.value)

    elif effect.effect_type == CardEffectType.REMOVE_UNIT:
        target_player = opp if effect.target == TargetType.OPPONENT else active
        if remove_slot_idx is not None and remove_slot_idx < len(target_player.unit_slots):
            slot = target_player.unit_slots[remove_slot_idx]
            slot.count += effect.value  # value is -1
            if slot.count <= 0:
                target_player.unit_slots.pop(remove_slot_idx)

    elif effect.effect_type == CardEffectType.APPLY_STATUS:
        tgt.active_statuses.append(copy.copy(effect.status))


def deploy_unit(card: CardData, player: PlayerState):
    slot = player.slot_for(card.id)
    if slot:
        slot.count = min(slot.count + 1, MAX_STACK)
    elif len(player.unit_slots) < MAX_SLOTS:
        player.unit_slots.append(UnitSlot(card, 1))
    else:
        # Slots full: replace the slot with the lowest count
        weakest = min(range(len(player.unit_slots)),
                      key=lambda i: player.unit_slots[i].count)
        player.unit_slots[weakest] = UnitSlot(card, 1)


# ──────────────────────────────────────────────────────────────────────────────
# AI strategies
# ──────────────────────────────────────────────────────────────────────────────

def ai_random(player: PlayerState, opp: PlayerState, half_turn: int):
    """Randomly plays or discards."""
    playable = [i for i, c in enumerate(player.hand)
                if player.can_afford(c)
                and not (any(e.effect_type == CardEffectType.REMOVE_UNIT for e in c.effects)
                         and not opp.unit_slots)]
    if not playable or random.random() < 0.15:
        return ('discard', random.randint(0, HAND_SIZE - 1), None)
    idx = random.choice(playable)
    slot_tgt = None
    if any(e.effect_type == CardEffectType.REMOVE_UNIT for e in player.hand[idx].effects):
        slot_tgt = random.randint(0, len(opp.unit_slots) - 1)
    return ('play', idx, slot_tgt)


def _score(card: CardData, player: PlayerState, opp: PlayerState) -> float:
    score = 0.0
    for e in card.effects:
        if e.effect_type == CardEffectType.MODIFY_HP:
            if e.target == TargetType.OPPONENT:
                score += -e.value * 1.3
            else:
                score += e.value * 0.9 * (1.5 if player.hp < 50 else 1.0)
        elif e.effect_type == CardEffectType.MODIFY_RESOURCE:
            if e.target == TargetType.SELF:
                r_val = player.get_res(e.resource_target)
                # Diminishing returns near cap
                score += e.value * (0.6 if r_val > 50 else 0.9)
            else:
                score += -e.value * 0.5
        elif e.effect_type == CardEffectType.REMOVE_UNIT:
            if e.target == TargetType.OPPONENT and opp.unit_slots:
                best_slot = max(opp.unit_slots, key=lambda s: s.count)
                score += 8.0 + best_slot.count * 1.5
        elif e.effect_type == CardEffectType.APPLY_STATUS:
            if e.status.status_type == StatusType.SKIP_PRODUCTION:
                score += 16.0
            else:
                score += 12.0
    if card.card_type == CardType.UNIDAD:
        slot = player.slot_for(card.id)
        if slot and slot.count >= MAX_STACK:
            return -1.0  # already maxed, don't waste
        if card.unit_subtype == UnitSubtype.ATACANTE:
            score += 9.0
        elif card.unit_subtype == UnitSubtype.DEFENSIVA:
            score += 6.0 + opp.unit_attack() * 1.2
        else:  # PRODUCTORA
            score += 5.0
    return score


def ai_greedy(player: PlayerState, opp: PlayerState, half_turn: int):
    """Plays the card with highest heuristic score."""
    best_score = 0.0
    best_idx = None
    best_slot = None

    for i, card in enumerate(player.hand):
        if not player.can_afford(card):
            continue
        has_remove = any(e.effect_type == CardEffectType.REMOVE_UNIT for e in card.effects)
        if has_remove and not opp.unit_slots:
            continue
        s = _score(card, player, opp)
        if s > best_score:
            best_score = s
            best_idx = i
            if has_remove:
                best_slot = max(range(len(opp.unit_slots)),
                                key=lambda i: opp.unit_slots[i].count)

    if best_idx is None:
        return ('discard', random.randint(0, HAND_SIZE - 1), None)
    return ('play', best_idx, best_slot)


AI_REGISTRY = {'greedy': ai_greedy, 'random': ai_random}


# ──────────────────────────────────────────────────────────────────────────────
# Game
# ──────────────────────────────────────────────────────────────────────────────

SUDDEN_DEATH_START = 40
MAX_TURNS = 100  # counted in half-turns per player


def play_game(ai_m, ai_p):
    """
    Returns (winner_faction, win_condition, half_turn, first_faction)
    where first_faction is who went first.
    """
    p = [PlayerState(Faction.MANIFESTANTES), PlayerState(Faction.POLICIAS)]
    ais = [ai_m, ai_p]

    first = random.randint(0, 1)  # 0 = Manifestantes first, 1 = Policias first
    active_idx = first
    half_turn = 0

    for _ in range(MAX_TURNS * 2):  # upper bound on iterations
        active = p[active_idx]
        opp = p[1 - active_idx]
        ai_fn = ais[active_idx]
        half_turn += 1

        # ── EFECTOS ──────────────────────────────────────────────────────────
        skip_prod = False
        prod_mult = 1
        for status in active.active_statuses[:]:
            status.counter -= 1
            if status.counter == 0:
                if status.status_type == StatusType.SKIP_PRODUCTION:
                    skip_prod = True
                elif status.status_type == StatusType.DOUBLE_PRODUCTION:
                    prod_mult = status.value
                active.active_statuses.remove(status)

        # ── PRODUCCIÓN ────────────────────────────────────────────────────────
        if not skip_prod:
            for r, base in BASE_PROD.items():
                active.add_res(r, base * prod_mult)
            for r, amount in active.unit_production().items():
                active.add_res(r, amount * prod_mult)
            # Unit combat damage
            enemy_atk = opp.unit_attack()
            own_def = active.unit_defense()
            net = max(0, enemy_atk - own_def)
            active.hp = max(0, active.hp - net)

        result = check_victory(active, opp)
        if result:
            return result + (half_turn, p[first].faction)

        # ── ACCIÓN ────────────────────────────────────────────────────────────
        action, card_idx, slot_tgt = ai_fn(active, opp, half_turn)

        if action == 'play':
            card = active.hand[card_idx]
            if active.can_afford(card):
                active.pay(card)
                if card.card_type == CardType.ACCION:
                    for effect in card.effects:
                        resolve_effect(effect, active, opp, slot_tgt)
                    result = check_victory(active, opp)
                    if result:
                        return result + (half_turn, p[first].faction)
                else:
                    deploy_unit(card, active)
            # always replace card (even if couldn't afford — effectively a discard)
        active.replace_card(card_idx)

        # ── FIN DE TURNO ──────────────────────────────────────────────────────
        if half_turn >= SUDDEN_DEATH_START:
            sd_dmg = half_turn - SUDDEN_DEATH_START + 1
            p[0].hp = max(0, p[0].hp - sd_dmg)
            p[1].hp = max(0, p[1].hp - sd_dmg)
            result = check_victory(active, opp)
            if result:
                return result + (half_turn, p[first].faction)

        active_idx = 1 - active_idx

    # ── TIMEOUT TIEBREAKER ────────────────────────────────────────────────────
    hp0, hp1 = p[0].hp, p[1].hp
    res0 = p[0].dinero + p[0].fuerza + p[0].social
    res1 = p[1].dinero + p[1].fuerza + p[1].social

    if hp0 != hp1:
        winner_idx = 0 if hp0 > hp1 else 1
    elif res0 != res1:
        winner_idx = 0 if res0 > res1 else 1
    else:
        winner_idx = 1 - first  # non-first player wins

    return (p[winner_idx].faction, WinCondition.TIMEOUT, half_turn, p[first].faction)


# ──────────────────────────────────────────────────────────────────────────────
# Simulation & reporting
# ──────────────────────────────────────────────────────────────────────────────

def run_simulation(n_games: int, ai1_name: str, ai2_name: str):
    ai_m = AI_REGISTRY[ai1_name]
    ai_p = AI_REGISTRY[ai2_name]

    wins = defaultdict(int)
    conditions = defaultdict(int)
    first_player_wins = 0
    half_turns = []

    for _ in range(n_games):
        winner, condition, ht, first_faction = play_game(ai_m, ai_p)
        wins[winner.value] += 1
        conditions[condition.value] += 1
        half_turns.append(ht)
        if winner == first_faction:
            first_player_wins += 1

    total = n_games
    avg_ht = statistics.mean(half_turns)
    med_ht = statistics.median(half_turns)
    # half-turns / 2 ≈ full rounds
    avg_rounds = avg_ht / 2

    print("=" * 60)
    print(f"  PiqueteDefend Balance Report  ({total:,} games)")
    print(f"  AI: Manifestantes={ai1_name}  Policias={ai2_name}")
    print("=" * 60)

    print("\n── Win rates ──────────────────────────────────────────────")
    for faction, count in sorted(wins.items()):
        pct = count / total * 100
        bar = "█" * int(pct / 2)
        print(f"  {faction:<20} {count:>6,}  ({pct:5.1f}%)  {bar}")

    print("\n── Win conditions ─────────────────────────────────────────")
    for cond, count in sorted(conditions.items(), key=lambda x: -x[1]):
        pct = count / total * 100
        print(f"  {cond:<22} {count:>6,}  ({pct:5.1f}%)")

    print("\n── Game length ────────────────────────────────────────────")
    print(f"  Avg half-turns  : {avg_ht:.1f}  (~{avg_rounds:.1f} full rounds)")
    print(f"  Median half-turns: {med_ht:.1f}")
    print(f"  Min / Max       : {min(half_turns)} / {max(half_turns)}")

    print("\n── First-player advantage ─────────────────────────────────")
    fpa = first_player_wins / total * 100
    print(f"  First player wins: {first_player_wins:,} / {total:,}  ({fpa:.1f}%)")
    print(f"  (50% = no advantage, >55% = significant)")

    print("\n── Balance assessment ─────────────────────────────────────")
    m_pct = wins.get("Manifestantes", 0) / total * 100
    p_pct = wins.get("Policias", 0) / total * 100
    diff = abs(m_pct - p_pct)
    if diff < 3:
        verdict = "EXCELLENT — within ±3%"
    elif diff < 7:
        verdict = "GOOD — within ±7%"
    elif diff < 12:
        verdict = "ACCEPTABLE — within ±12%, consider minor tweaks"
    else:
        verdict = f"IMBALANCED — {max(wins, key=wins.get)} has a significant edge"
    print(f"  Win-rate gap: {diff:.1f}pp  →  {verdict}")
    print("=" * 60)


# ──────────────────────────────────────────────────────────────────────────────
# Entry point
# ──────────────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="PiqueteDefend balance simulator")
    parser.add_argument("--games", type=int, default=10_000,
                        help="Number of games to simulate (default: 10 000)")
    parser.add_argument("--ai", choices=AI_REGISTRY.keys(),
                        help="Set same AI strategy for both factions")
    parser.add_argument("--ai1", choices=AI_REGISTRY.keys(), default="greedy",
                        help="AI for Manifestantes (default: greedy)")
    parser.add_argument("--ai2", choices=AI_REGISTRY.keys(), default="greedy",
                        help="AI for Policias (default: greedy)")
    parser.add_argument("--seed", type=int, default=None,
                        help="Random seed for reproducibility")
    args = parser.parse_args()

    if args.seed is not None:
        random.seed(args.seed)

    ai1 = args.ai if args.ai else args.ai1
    ai2 = args.ai if args.ai else args.ai2

    run_simulation(args.games, ai1, ai2)
