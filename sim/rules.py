"""Motor de reglas del simulador — espejo determinista de PiqueteDefend.Core.GameEngine.

Re-implementa el loop de turno del spec §6 con la misma semántica de stats efectivos,
pasivas, estados y muerte súbita. Determinista: toda la aleatoriedad (coinflip, robo)
pasa por un RNG inyectado y seedeable.

Timing de estados (decisión de diseño, ver nota en spec §6/§7.7):
  · Estados de JUGADOR (SkipProduction/DoubleProduction): fire-on-expiry. counter-- en
    EFECTOS (inicio de turno); al llegar a 0 disparan y se eliminan. counter=1 = próximo turno.
  · Estados por UNIDAD (Poison/Stun/Furia/Desmoralizar): active-while-present. Poison hace
    daño en EFECTOS (inicio); el counter de TODOS se decrementa al FIN del turno del dueño
    (se elimina al llegar a 0). counter = nº de turnos activos. Esto evita el off-by-one que
    haría que un Stun de 1 turno expirara antes de la fase de acción.
"""

from __future__ import annotations

import math
import random
from typing import Dict, List, Optional

from cards import build_pool, starting_units
from knobs import GlobalKnobs, scale
from model import (
    ActionCardData, AttackEffect, CardData, CardEffect, CardEffectType,
    EquipmentCardData, Faction, PassiveTarget, PassiveType, ResourceType, StatType, StatusEffect,
    StatusType, TargetMode, TargetType, UnitCardData, ALL_RESOURCES, PLAYER_STATUSES,
)

BOARD = 6


def inflated_amount(amount: int, inflation_pct: int) -> int:
    """Costo con inflación aplicada (spec §3). Redondea hacia ARRIBA (ceil) para que la
    inflación siempre muerda; espejo exacto de GameEngine.InflatedAmount en el Core."""
    if inflation_pct <= 0:
        return amount
    return math.ceil(amount * (100 + inflation_pct) / 100)


class Rng:
    """RNG seedeable con la misma superficie que IRandomProvider (next, choice ponderada)."""

    def __init__(self, seed: int):
        self._r = random.Random(seed)

    def next(self, n: int) -> int:
        return self._r.randrange(n)

    def weighted_choice(self, pool: List[CardData]) -> CardData:
        weights = [max(1, c.draw_weight) for c in pool]
        return self._r.choices(pool, weights=weights, k=1)[0]


class UnitSlot:
    """Unidad desplegada. Stats efectivos = base (unit) + equipo + estados (calculado al vuelo)."""

    def __init__(self, unit: UnitCardData):
        self.unit = unit
        self.equipment: List[EquipmentCardData] = []
        self.statuses: List[StatusEffect] = []
        self.current_hp = self.max_hp

    # ── Stats efectivos ──
    @property
    def max_hp(self) -> int:
        bonus = sum(m.value for e in self.equipment for m in e.stat_modifiers
                    if m.stat == StatType.MAX_HP)
        return self.unit.max_hp + bonus

    @property
    def equip_damage(self) -> int:
        return sum(m.value for e in self.equipment for m in e.stat_modifiers
                   if m.stat == StatType.DAMAGE)

    def status_value(self, st: StatusType) -> int:
        return sum(s.value for s in self.statuses if s.status_type == st)

    def has_status(self, st: StatusType) -> bool:
        return any(s.status_type == st for s in self.statuses)

    @property
    def is_stunned(self) -> bool:
        return self.has_status(StatusType.STUN)

    @property
    def is_dead(self) -> bool:
        return self.current_hp <= 0

    def all_passives(self):
        yield from self.unit.passive_effects
        for e in self.equipment:
            yield from e.granted_passives


class PlayerState:
    def __init__(self, faction: Faction, k: GlobalKnobs):
        self.faction = faction
        self.dinero = scale(5, k.initial_mult)
        self.fuerza = scale(5, k.initial_mult)
        self.social = scale(5, k.initial_mult)
        self.hand: List[CardData] = []
        self.slots: List[Optional[UnitSlot]] = [None] * BOARD
        self.statuses: List[StatusEffect] = []   # estados de jugador (producción)

    def get(self, r: ResourceType) -> int:
        return {ResourceType.DINERO: self.dinero, ResourceType.FUERZA: self.fuerza,
                ResourceType.SOCIAL: self.social}[r]

    def set(self, r: ResourceType, value: int, max_res: int):
        v = 0 if value < 0 else (max_res if value > max_res else value)
        if r == ResourceType.DINERO:
            self.dinero = v
        elif r == ResourceType.FUERZA:
            self.fuerza = v
        else:
            self.social = v

    def add(self, r: ResourceType, delta: int, max_res: int):
        self.set(r, self.get(r) + delta, max_res)

    def pay(self, card: CardData, inflation_pct: int = 0):
        for c in card.costs:
            self.set(c.resource, self.get(c.resource) - inflated_amount(c.amount, inflation_pct), 10 ** 9)

    def can_afford(self, card: CardData, inflation_pct: int = 0) -> bool:
        return all(self.get(c.resource) >= inflated_amount(c.amount, inflation_pct) for c in card.costs)

    def alive_count(self) -> int:
        return sum(1 for s in self.slots if s)

    def has_any_unit(self) -> bool:
        return any(s for s in self.slots)

    def total_hp(self) -> int:
        return sum(s.current_hp for s in self.slots if s)

    def first_free_allowed(self, unit: UnitCardData) -> int:
        for i, s in enumerate(self.slots):
            if s is None and unit.allows_slot(i):
                return i
        return -1

    def has_any_allowed(self, unit: UnitCardData) -> bool:
        return any(unit.allows_slot(i) for i in range(BOARD))


# ── Resultado de partida ─────────────────────────────────────────────────────

class Outcome:
    def __init__(self, winner: Optional[Faction], condition: str, half_turns: int):
        self.winner = winner          # None = empate
        self.condition = condition    # "KO" | "Timeout" | "Draw"
        self.half_turns = half_turns


# ── Motor ────────────────────────────────────────────────────────────────────

class GameEngine:
    def __init__(self, config: GlobalKnobs, rng: Rng,
                 faction0: Faction, faction1: Faction):
        self.k = config
        self.rng = rng
        self.max_resource = 100
        self.pools = {f: build_pool(f, config) for f in (faction0, faction1)}
        self.players = [PlayerState(faction0, config), PlayerState(faction1, config)]
        self.half_turn = 0
        self.active = 0
        self.first = 0
        self.outcome: Optional[Outcome] = None
        # flags por turno
        self.card_used = False
        self.attack_used = False
        # telemetría
        self.cards_played: Dict[str, int] = {}
        self.starved_turns = 0     # turnos donde no pudo pagar ninguna carta de la mano
        self.action_turns = 0      # turnos jugados (denominador de starved)
        self.deploys_by_arch: Dict[str, int] = {}
        self.deaths_by_arch: Dict[str, int] = {}
        # presencia: muestreo por medio-turno (1 muestra por jugador), para medir cuántas
        # unidades hay vivas y cuántas en vanguardia (slots 3-5 base-0) simultáneamente.
        self.presence_sum = 0      # Σ unidades vivas sobre todas las muestras-jugador
        self.vanguard_sum = 0      # Σ unidades en vanguardia sobre todas las muestras-jugador
        self.hand_units_sum = 0    # Σ cartas de UNIDAD en mano (clog: unidades que no se pueden bajar)
        self.presence_samples = 0  # nº de muestras-jugador

    # ── Setup ──
    def start(self, first_index: int = -1):
        for idx, p in enumerate(self.players):
            for unit in starting_units(p.faction, self.k):
                slot = p.first_free_allowed(unit)
                if slot >= 0:
                    p.slots[slot] = UnitSlot(unit)
                    self._record_deploy(unit)
            pool = self.pools[p.faction]
            for _ in range(6):
                p.hand.append(self.rng.weighted_choice(pool))
        self.first = first_index if first_index >= 0 else self.rng.next(2)
        self.active = self.first

    @property
    def is_finished(self) -> bool:
        return self.outcome is not None

    def sample_presence(self):
        """Muestrea el tablero de ambos jugadores (1 muestra-jugador c/u) para las métricas
        de presencia/vanguardia. Se llama una vez por medio-turno desde el harness."""
        for p in self.players:
            self.presence_sum += sum(1 for s in p.slots if s)
            self.vanguard_sum += sum(1 for i in (3, 4, 5) if p.slots[i])
            self.hand_units_sum += sum(1 for c in p.hand if isinstance(c, UnitCardData))
            self.presence_samples += 1

    @property
    def inflation_percent(self) -> int:
        """% de inflación vigente este medio-turno (spec §3). 0 si no arrancó."""
        start = self.k.inflation_start_turn
        if not start or self.half_turn < start:
            return 0
        return (self.half_turn - start + 1) * self.k.inflation_pct_per_turn

    @property
    def active_player(self) -> PlayerState:
        return self.players[self.active]

    @property
    def opponent(self) -> PlayerState:
        return self.players[1 - self.active]

    # ── Targeting genérico (ataques, pasivas, auras), anclado a la formación (spec §6) ──
    @staticmethod
    def resolve_targets(mode: TargetMode, count: int,
                        board: List[Optional[UnitSlot]], src_idx: int) -> List[int]:
        out: List[int] = []
        n = len(board)
        if mode == TargetMode.SELF:
            if 0 <= src_idx < n:
                out.append(src_idx)
        elif mode == TargetMode.ADJACENT:
            for idx in (src_idx - 1, src_idx + 1):
                if 0 <= idx < n:
                    out.append(idx)
        elif mode in (TargetMode.ALL, TargetMode.ANY):
            out = [i for i in range(n) if board[i] is not None]
        elif mode == TargetMode.FRONTMOST:
            f = next((i for i in range(n - 1, -1, -1) if board[i] is not None), -1)
            if f >= 0:
                reach = 1 if count <= 0 else count
                for kk in range(reach):
                    idx = f - kk
                    if idx < 0:
                        break
                    out.append(idx)
        elif mode == TargetMode.BACKMOST:
            b = next((i for i in range(n) if board[i] is not None), -1)
            if b >= 0:
                reach = 1 if count <= 0 else count
                for kk in range(reach):
                    idx = b + kk
                    if idx >= n:
                        break
                    out.append(idx)
        return out

    def aura_bonus_for(self, board: List[Optional[UnitSlot]], slot_index: int) -> int:
        """Suma de AuraDamage de aliadas cuyo objetivo cubre a la atacante en slot_index."""
        total = 0
        for src_i, src in enumerate(board):
            if src is None or src_i == slot_index:
                continue
            for pe in src.all_passives():
                if pe.passive_type == PassiveType.AURA_DAMAGE and pe.target == PassiveTarget.ALLIES:
                    if slot_index in self.resolve_targets(pe.mode, pe.count, board, src_i):
                        total += pe.value
        return total

    def effective_attack_damage(self, board: List[Optional[UnitSlot]], slot_index: int) -> int:
        s = board[slot_index]
        base = s.unit.attack.amount_per_slot + s.equip_damage
        base += s.status_value(StatusType.FURIA) - s.status_value(StatusType.DESMORALIZAR)
        base += self.aura_bonus_for(board, slot_index)
        return max(0, base)

    # ── Turno ──
    def begin_turn(self):
        self.half_turn += 1
        p = self.active_player

        # EFECTOS a) estados de jugador (producción): fire-on-expiry
        skip_production = False
        production_mult = 1
        for st in list(p.statuses):
            st.counter -= 1
            if st.counter <= 0:
                if st.status_type == StatusType.SKIP_PRODUCTION:
                    skip_production = True
                elif st.status_type == StatusType.DOUBLE_PRODUCTION:
                    production_mult = st.value
                p.statuses.remove(st)

        # EFECTOS b) estados por unidad: Poison hace daño (decremento al fin de turno)
        for i, s in enumerate(p.slots):
            if s is None:
                continue
            poison = s.status_value(StatusType.POISON)
            if poison:
                self._direct_damage(p, i, poison)
        self._check_victory()
        if self.is_finished:
            return

        # EFECTOS c) pasivas de inicio de turno: Regeneration, TurnDamage, TurnStatus
        self._resolve_turn_start_passives(p)
        self._check_victory()
        if self.is_finished:
            return

        # PRODUCCIÓN (no en el turno 1 de la partida, salvo regla first_produces_t1)
        produces = self.half_turn > 1 or (self.half_turn == 1 and self.k.first_produces_t1)
        if produces and not skip_production:
            for r in ALL_RESOURCES:
                base = scale(1, self.k.base_prod_mult) * production_mult
                if base:
                    p.add(r, base, self.max_resource)
            for s in p.slots:
                if s is None:
                    continue
                for pe in s.all_passives():
                    if pe.passive_type == PassiveType.PRODUCE_RESOURCE:
                        p.add(pe.resource, pe.value * production_mult, self.max_resource)

        self.card_used = False
        self.attack_used = False

    def _resolve_turn_start_passives(self, owner: PlayerState):
        opp = self.players[1 - self.players.index(owner)]
        for src_i, s in enumerate(owner.slots):
            if s is None:
                continue
            for pe in list(s.all_passives()):
                if pe.passive_type == PassiveType.REGENERATION:
                    board = owner.slots if pe.target != PassiveTarget.ENEMIES else opp.slots
                    for t in self._passive_targets(pe, src_i, board):
                        u = board[t]
                        if u:
                            u.current_hp = min(u.max_hp, u.current_hp + pe.value)
                elif pe.passive_type == PassiveType.TURN_DAMAGE:
                    board_owner = opp if pe.target == PassiveTarget.ENEMIES else owner
                    for t in self._passive_targets(pe, src_i, board_owner.slots):
                        if board_owner.slots[t]:
                            self._direct_damage(board_owner, t, pe.value)
                elif pe.passive_type == PassiveType.TURN_STATUS and pe.status is not None:
                    board_owner = opp if pe.target == PassiveTarget.ENEMIES else owner
                    for t in self._passive_targets(pe, src_i, board_owner.slots):
                        u = board_owner.slots[t]
                        if u:
                            u.statuses.append(pe.status.clone())

    def _passive_targets(self, pe, src_i, board) -> List[int]:
        """Targeting de pasiva anclado a la formación (espejo del Core). Frontmost/Backmost son
        deterministas; los slots vacíos que devuelva los filtran los callers."""
        if pe.mode == TargetMode.SELF or pe.target == PassiveTarget.SELF:
            return [src_i]
        return self.resolve_targets(pe.mode, pe.count, board, src_i)

    # ── Acción: jugar carta ──
    def play_card(self, hand_index: int, deploy_slot: int = -1,
                  effect_slot: int = -1, effect_slot_b: int = -1,
                  equip_slot: int = -1) -> bool:
        if self.card_used or self.is_finished:
            return False
        p, opp = self.active_player, self.opponent
        if not (0 <= hand_index < len(p.hand)):
            return False
        card = p.hand[hand_index]
        infl = self.inflation_percent
        if not p.can_afford(card, infl):
            return False

        if isinstance(card, UnitCardData):
            slot = self._resolve_deploy(card, p, deploy_slot)
            if slot < 0:
                return False
            p.pay(card, infl)
            p.slots[slot] = UnitSlot(card)   # slot siempre libre (sin reemplazo, §8.3)
            self._record_deploy(card)
        elif isinstance(card, EquipmentCardData):
            if equip_slot < 0 or equip_slot >= BOARD or p.slots[equip_slot] is None:
                return False
            p.pay(card, infl)
            self._equip(p.slots[equip_slot], card)
        elif isinstance(card, ActionCardData):
            p.pay(card, infl)
            for eff in card.effects:
                self._resolve_effect(eff, p, opp, effect_slot, effect_slot_b)

        self._replace_card(p, hand_index)
        self.card_used = True
        self.cards_played[card.id] = self.cards_played.get(card.id, 0) + 1
        self._check_victory()
        return True

    def discard_card(self, hand_index: int) -> bool:
        if self.card_used or self.is_finished:
            return False
        p = self.active_player
        if not (0 <= hand_index < len(p.hand)):
            return False
        self._replace_card(p, hand_index)
        self.card_used = True
        return True

    def _equip(self, slot: UnitSlot, card: EquipmentCardData):
        before = slot.max_hp
        slot.equipment.append(card)
        # +MaxHp se siente como buff: sube el HP actual el mismo delta
        slot.current_hp += slot.max_hp - before

    def _resolve_deploy(self, unit: UnitCardData, p: PlayerState, requested: int) -> int:
        # Sin reemplazo: sólo slot permitido y LIBRE (spec §8.3).
        if requested >= 0:
            if requested >= BOARD or not unit.allows_slot(requested) or p.slots[requested] is not None:
                return -1
            return requested
        return p.first_free_allowed(unit)  # -1 si no hay libre → no se despliega

    def _resolve_effect(self, eff: CardEffect, active: PlayerState, opp: PlayerState,
                        chosen: int, chosen_b: int):
        tgt = active if eff.target == TargetType.SELF else opp
        et = eff.effect_type

        if et == CardEffectType.MODIFY_RESOURCE:
            tgt.add(eff.resource_target, eff.value, self.max_resource)
        elif et == CardEffectType.MODIFY_HP:
            slot = eff.target_slot if eff.target_slot >= 0 else chosen
            if 0 <= slot < BOARD and tgt.slots[slot]:
                u = tgt.slots[slot]
                u.current_hp += eff.value
                if u.is_dead:
                    self._remove(tgt, slot)
                elif u.current_hp > u.max_hp:
                    u.current_hp = u.max_hp
        elif et == CardEffectType.REMOVE_UNIT:
            slot = eff.target_slot if eff.target_slot >= 0 else chosen
            if 0 <= slot < BOARD:
                tgt.slots[slot] = None
        elif et == CardEffectType.APPLY_STATUS and eff.status is not None:
            if eff.status.status_type in PLAYER_STATUSES:
                tgt.statuses.append(eff.status.clone())
            else:
                slot = eff.target_slot if eff.target_slot >= 0 else chosen
                if 0 <= slot < BOARD and tgt.slots[slot]:
                    tgt.slots[slot].statuses.append(eff.status.clone())
        elif et == CardEffectType.MOVE_UNIT:
            src = eff.target_slot if eff.target_slot >= 0 else chosen
            dst = eff.target_slot_b if eff.target_slot_b >= 0 else chosen_b
            if (0 <= src < BOARD and 0 <= dst < BOARD and tgt.slots[src]
                    and tgt.slots[dst] is None and tgt.slots[src].unit.allows_slot(dst)):
                tgt.slots[dst] = tgt.slots[src]
                tgt.slots[src] = None
        elif et == CardEffectType.SWAP_UNITS:
            a = eff.target_slot if eff.target_slot >= 0 else chosen
            b = eff.target_slot_b if eff.target_slot_b >= 0 else chosen_b
            if 0 <= a < BOARD and 0 <= b < BOARD and a != b:
                tgt.slots[a], tgt.slots[b] = tgt.slots[b], tgt.slots[a]

    # ── Acción: atacar ──
    @staticmethod
    def _has_valid_target(ua, targets, p: PlayerState, opp: PlayerState) -> bool:
        """≥1 enemigo ocupado (daño) o aliado por debajo de maxHp (cura); si no, se cancela."""
        for t in targets:
            if not (0 <= t < BOARD):
                continue
            if ua.effect == AttackEffect.HEAL_ALLIES:
                u = p.slots[t]
                if u and u.current_hp < u.max_hp:
                    return True
            elif opp.slots[t] is not None:
                return True
        return False

    def attack_with_unit(self, attacker_slot: int, chosen_targets: Optional[List[int]] = None) -> bool:
        if self.attack_used or self.is_finished:
            return False
        # Regla opcional: el primer jugador no ataca en su turno 1 (compensa la iniciativa)
        if self.k.first_no_attack_t1 and self.half_turn == 1:
            return False
        p, opp = self.active_player, self.opponent
        if not (0 <= attacker_slot < BOARD):
            return False
        attacker = p.slots[attacker_slot]
        if attacker is None or attacker.is_stunned:
            return False

        ua = attacker.unit.attack
        # Targeting anclado a la formación del objetivo (spec §6): rival si daña, propio si cura.
        target_board = p.slots if ua.effect == AttackEffect.HEAL_ALLIES else opp.slots
        candidates = self.resolve_targets(ua.mode, ua.count, target_board, attacker_slot)
        if ua.requires_choice:
            if chosen_targets is None or len(chosen_targets) != ua.count:
                return False
            if any(t not in candidates for t in chosen_targets):
                return False
            targets = chosen_targets
        else:
            targets = candidates

        # Cancela (sin gastar el ataque) si no afecta a ningún objetivo válido (spec §6).
        if not self._has_valid_target(ua, targets, p, opp):
            return False

        if ua.effect == AttackEffect.HEAL_ALLIES:
            for t in targets:
                u = p.slots[t]
                if u and u.current_hp < u.max_hp:
                    u.current_hp = min(u.max_hp, u.current_hp + ua.amount_per_slot)
            self.attack_used = True
            return True

        # Daño a enemigos + Retaliate
        dmg = self.effective_attack_damage(p.slots, attacker_slot)
        retaliation = 0
        for t in targets:
            if not (0 <= t < BOARD):
                continue
            d = opp.slots[t]
            if d is None:
                continue  # whiff
            retaliation += sum(pe.value for pe in d.all_passives()
                               if pe.passive_type == PassiveType.RETALIATE)
            d.current_hp -= dmg
            if d.is_dead:
                self._remove(opp, t)
        if retaliation and p.slots[attacker_slot] is not None:
            attacker.current_hp -= retaliation
            if attacker.is_dead:
                self._remove(p, attacker_slot)

        self.attack_used = True
        self._check_victory()
        return True

    # ── Fin de turno ──
    def end_turn(self):
        if self.is_finished:
            return
        # decrementar estados por unidad de AMBOS dueños? No: sólo del jugador activo (su turno).
        self._tick_unit_statuses(self.active_player)

        if self.half_turn >= self.k.sudden_death_start:
            self._apply_sudden_death()
            self._check_victory()
            if self.is_finished:
                return
        if self.half_turn >= self.k.max_turns:
            self._timeout_tiebreak()
            return
        self.active = 1 - self.active

    def _tick_unit_statuses(self, p: PlayerState):
        for s in p.slots:
            if s is None:
                continue
            for st in list(s.statuses):
                st.counter -= 1
                if st.counter <= 0:
                    s.statuses.remove(st)

    def _apply_sudden_death(self):
        for p in self.players:
            for i, s in enumerate(p.slots):
                if s is None:
                    continue
                s.current_hp -= 1
                if s.is_dead:
                    self._remove(p, i)

    # ── Daño directo (no dispara Retaliate) ──
    def _direct_damage(self, owner: PlayerState, slot: int, amount: int):
        u = owner.slots[slot]
        if u is None:
            return
        u.current_hp -= amount
        if u.is_dead:
            self._remove(owner, slot)

    def _remove(self, owner: PlayerState, slot: int):
        s = owner.slots[slot]
        if s is not None:
            self._record_death(s)
        owner.slots[slot] = None

    def _record_deploy(self, unit: UnitCardData):
        self.deploys_by_arch[unit.archetype] = self.deploys_by_arch.get(unit.archetype, 0) + 1

    def _record_death(self, slot: UnitSlot):
        a = slot.unit.archetype
        self.deaths_by_arch[a] = self.deaths_by_arch.get(a, 0) + 1

    def _replace_card(self, p: PlayerState, hand_index: int):
        p.hand[hand_index] = self.rng.weighted_choice(self.pools[p.faction])

    # ── Victoria ──
    def _check_victory(self):
        if self.is_finished:
            return
        a0 = self.players[0].has_any_unit()
        a1 = self.players[1].has_any_unit()
        if not a0 and not a1:
            self.outcome = Outcome(None, "Draw", self.half_turn)
        elif not a1:
            self.outcome = Outcome(self.players[0].faction, "KO", self.half_turn)
        elif not a0:
            self.outcome = Outcome(self.players[1].faction, "KO", self.half_turn)

    def _timeout_tiebreak(self):
        p0, p1 = self.players
        a0, a1 = p0.alive_count(), p1.alive_count()
        if a0 != a1:
            w = 0 if a0 > a1 else 1
        else:
            h0, h1 = p0.total_hp(), p1.total_hp()
            if h0 != h1:
                w = 0 if h0 > h1 else 1
            else:
                w = 1 - self.first
        self.outcome = Outcome(self.players[w].faction, "Timeout", self.half_turn)
