"""Política de decisión greedy heurística — implementación de spec §16.

Lee el estado observable del motor y ejecuta, en orden: (1) jugar/descartar 1 carta,
(2) atacar con 1 unidad. Determinista: empates de puntaje se rompen por menor índice.
Ambos jugadores usan esta misma política (espejo), así el win-rate mide la facción.
"""

from __future__ import annotations

from typing import List, Optional, Tuple

from model import (
    ActionCardData, AttackEffect, CardEffectType, EquipmentCardData, PassiveType,
    ResourceType, StatType, StatusType, TargetType, UnitCardData, ALL_RESOURCES, PLAYER_STATUSES,
)
from rules import BOARD, GameEngine, PlayerState, UnitSlot

THRESHOLD = 0.0   # umbral de juego: si nada supera esto, se descarta para ciclar


# ── Valor de unidad (§16.1) ──────────────────────────────────────────────────

def _passive_value(passives) -> float:
    total = 0.0
    for pe in passives:
        if pe.passive_type == PassiveType.PRODUCE_RESOURCE:
            total += 4 * pe.value
        elif pe.passive_type in (PassiveType.AURA_DAMAGE, PassiveType.RETALIATE,
                                 PassiveType.REGENERATION, PassiveType.TURN_DAMAGE):
            total += 3 * pe.value
        elif pe.passive_type == PassiveType.TURN_STATUS:
            total += 4
    return total


def base_unit_value(u: UnitCardData) -> float:
    a = u.attack
    if a.effect == AttackEffect.HEAL_ALLIES:
        dmg_total = 0.0
        pv = _passive_value(u.passive_effects) + 3 * a.amount_per_slot
    else:
        n = a.pick_count if a.pick_count > 0 else len(a.pattern)
        dmg_total = a.amount_per_slot * n
        pv = _passive_value(u.passive_effects)
    return u.max_hp / 4 + dmg_total / 2 + pv


def slot_value(s: UnitSlot) -> float:
    a = s.unit.attack
    if a.effect == AttackEffect.HEAL_ALLIES:
        dmg_total = 0.0
        pv = _passive_value(list(s.all_passives())) + 3 * a.amount_per_slot
    else:
        n = a.pick_count if a.pick_count > 0 else len(a.pattern)
        dmg_total = (a.amount_per_slot + s.equip_damage) * n
        pv = _passive_value(list(s.all_passives()))
    return s.max_hp / 4 + dmg_total / 2 + pv


def is_attacker(s: UnitSlot) -> bool:
    return s.unit.attack.effect == AttackEffect.DAMAGE_ENEMIES and s.unit.attack.amount_per_slot > 0


# ── Selección de objetivos para efectos de carta (§16.5) ─────────────────────

def best_enemy_target(opp: PlayerState, lethal_amount: int = 0) -> int:
    """Slot enemigo de mayor valor; si lethal_amount mata a alguno, prioriza el más valioso que muera."""
    occupied = [i for i in range(BOARD) if opp.slots[i]]
    if not occupied:
        return -1
    if lethal_amount > 0:
        killable = [i for i in occupied if opp.slots[i].current_hp <= lethal_amount]
        if killable:
            return max(killable, key=lambda i: slot_value(opp.slots[i]))
    return max(occupied, key=lambda i: slot_value(opp.slots[i]))


def best_enemy_attacker(opp: PlayerState) -> int:
    cand = [i for i in range(BOARD) if opp.slots[i] and is_attacker(opp.slots[i])]
    if not cand:
        return -1
    return max(cand, key=lambda i: slot_value(opp.slots[i]))


def best_damaged_ally(p: PlayerState) -> int:
    cand = [i for i in range(BOARD) if p.slots[i] and p.slots[i].current_hp < p.slots[i].max_hp]
    if not cand:
        return -1
    return max(cand, key=lambda i: slot_value(p.slots[i]))


def best_own_attacker(p: PlayerState) -> int:
    cand = [i for i in range(BOARD) if p.slots[i] and is_attacker(p.slots[i])]
    if not cand:
        return -1
    return max(cand, key=lambda i: slot_value(p.slots[i]))


# ── Deploy (§16.6) ───────────────────────────────────────────────────────────

def choose_deploy_slot(engine: GameEngine, p: PlayerState, unit: UnitCardData) -> int:
    free = [i for i in range(BOARD) if p.slots[i] is None and unit.allows_slot(i)]
    if free:
        arch = unit.archetype
        if arch in ("Productora", "Sniper", "Emisor", "Healer"):
            return min(free)          # retaguardia: protegerlas
        if arch == "Muro":
            return max(free)          # frente: tapar la línea
        # Escaramuza/Cleave: máxima cobertura del patrón sobre enemigos ocupados; empate → más adelante
        opp = engine.opponent
        a = unit.attack
        best, best_cov = free[0], -1
        for i in free:
            cov = sum(1 for t in engine.resolve_slots(a.reference, a.pattern, i) if opp.slots[t])
            if cov > best_cov or (cov == best_cov and i > best):
                best, best_cov = i, cov
        return best
    return -1  # sin slot libre permitido: no se despliega (no hay reemplazo, §8.3)


# ── Puntaje de cartas (§16.3) ────────────────────────────────────────────────

class CardPlan:
    def __init__(self, score: float, kind: str, **kw):
        self.score = score
        self.kind = kind          # "unit" | "action" | "equip"
        self.params = kw          # parámetros resueltos para engine.play_card


def _resource_short(p: PlayerState, res: ResourceType) -> bool:
    return p.get(res) == min(p.get(r) for r in ALL_RESOURCES)


def evaluate_card(engine: GameEngine, p: PlayerState, opp: PlayerState, idx: int) -> CardPlan:
    card = p.hand[idx]

    if isinstance(card, UnitCardData):
        slot = choose_deploy_slot(engine, p, card)
        if slot < 0:
            return CardPlan(float("-inf"), "unit")
        score = base_unit_value(card)
        if p.alive_count() < 2:
            score += 6   # necesidad de presencia
        return CardPlan(score, "unit", hand_index=idx, deploy_slot=slot)

    if isinstance(card, EquipmentCardData):
        carrier = best_own_attacker(p)
        if carrier < 0:
            carrier = next((i for i in range(BOARD) if p.slots[i]), -1)
        if carrier < 0:
            return CardPlan(float("-inf"), "equip")
        val = sum(m.value for m in card.stat_modifiers) + _passive_value(card.granted_passives)
        return CardPlan(float(val), "equip", hand_index=idx, equip_slot=carrier)

    # ActionCardData: puntúa por su(s) efecto(s) (acá cada acción tiene 1 efecto relevante)
    assert isinstance(card, ActionCardData)
    score = 0.0
    eff_slot = -1
    eff_slot_b = -1
    for eff in card.effects:
        et = eff.effect_type
        if et == CardEffectType.MODIFY_RESOURCE:
            res = eff.resource_target
            if eff.target == TargetType.SELF and eff.value > 0:
                s = eff.value * (1.5 if _resource_short(p, res) else 1.0)
                if p.get(res) >= engine.max_resource - eff.value:
                    s *= 0.2
                score += s
            else:  # drenaje al rival
                score += min(abs(eff.value), opp.get(res)) * 0.5
        elif et == CardEffectType.MODIFY_HP:
            if eff.target == TargetType.OPPONENT and eff.value < 0:
                t = best_enemy_target(opp, lethal_amount=abs(eff.value))
                if t >= 0:
                    d = opp.slots[t]
                    s = min(abs(eff.value), d.current_hp)
                    if abs(eff.value) >= d.current_hp:
                        s += slot_value(d)
                    score += s
                    eff_slot = t
            elif eff.target == TargetType.SELF and eff.value > 0:
                t = best_damaged_ally(p)
                if t >= 0:
                    u = p.slots[t]
                    score += min(eff.value, u.max_hp - u.current_hp)
                    eff_slot = t
        elif et == CardEffectType.APPLY_STATUS and eff.status is not None:
            st = eff.status
            if st.status_type in PLAYER_STATUSES:
                if st.status_type == StatusType.DOUBLE_PRODUCTION:
                    producers = sum(1 for s in p.slots if s for pe in s.all_passives()
                                    if pe.passive_type == PassiveType.PRODUCE_RESOURCE)
                    score += 3 + producers  # producción proyectada del próximo turno
                else:  # SkipProduction al rival
                    score += 3
            elif st.status_type == StatusType.STUN:
                t = best_enemy_attacker(opp)
                if t >= 0:
                    score += 0.5 * slot_value(opp.slots[t]); eff_slot = t
            elif st.status_type == StatusType.POISON:
                t = best_enemy_target(opp)
                if t >= 0:
                    score += st.value * st.counter; eff_slot = t
            elif st.status_type == StatusType.DESMORALIZAR:
                t = best_enemy_attacker(opp)
                if t >= 0:
                    score += st.value * st.counter * 0.4; eff_slot = t
            elif st.status_type == StatusType.FURIA:
                t = best_own_attacker(p)
                if t >= 0:
                    score += st.value * st.counter * 0.5; eff_slot = t
        elif et == CardEffectType.MOVE_UNIT:
            mv = _plan_move(engine, p)
            if mv:
                eff_slot, eff_slot_b = mv
                score += 0.1
        elif et == CardEffectType.SWAP_UNITS:
            sw = _plan_swap_enemy(opp)
            if sw:
                eff_slot, eff_slot_b = sw
                score += 0.1

    return CardPlan(score, "action", hand_index=idx, effect_slot=eff_slot, effect_slot_b=eff_slot_b)


def _plan_move(engine: GameEngine, p: PlayerState) -> Optional[Tuple[int, int]]:
    for src in range(BOARD):
        u = p.slots[src]
        if u is None:
            continue
        for dst in range(BOARD):
            if p.slots[dst] is None and u.unit.allows_slot(dst) and dst != src:
                return (src, dst)
    return None


def _plan_swap_enemy(opp: PlayerState) -> Optional[Tuple[int, int]]:
    occ = [i for i in range(BOARD) if opp.slots[i]]
    if len(occ) < 2:
        return None
    front = max(occ, key=lambda i: (i, slot_value(opp.slots[i])))
    back = min(occ)
    if front != back:
        return (front, back)
    return None


def _discard_potential(card) -> float:
    """Valor latente para elegir qué descartar (ignora afford/posición)."""
    if isinstance(card, UnitCardData):
        return base_unit_value(card)
    if isinstance(card, EquipmentCardData):
        return sum(m.value for m in card.stat_modifiers) + _passive_value(card.granted_passives)
    best = 0.0
    for eff in card.effects:
        if eff.effect_type == CardEffectType.MODIFY_RESOURCE:
            best = max(best, abs(eff.value) * (1.0 if eff.target == TargetType.SELF else 0.5))
        elif eff.effect_type == CardEffectType.MODIFY_HP:
            best = max(best, abs(eff.value))
        elif eff.effect_type == CardEffectType.APPLY_STATUS:
            best = max(best, 4.0)
        else:
            best = max(best, 0.1)
    return best


# ── Elección de ataque (§16.4) ───────────────────────────────────────────────

def _target_contribution(engine: GameEngine, opp: PlayerState, dmg: int, t: int) -> float:
    d = opp.slots[t]
    if d is None:
        return 0.0   # whiff
    retal = sum(pe.value for pe in d.all_passives() if pe.passive_type == PassiveType.RETALIATE)
    val = min(dmg, d.current_hp) - retal
    if dmg >= d.current_hp:
        val += slot_value(d)
    return val


def best_attack(engine: GameEngine, p: PlayerState, opp: PlayerState):
    """Devuelve (attacker_slot, chosen_targets|None, value) o None."""
    best = None
    for i in range(BOARD):
        s = p.slots[i]
        if s is None or s.is_stunned:
            continue
        a = s.unit.attack
        candidates = engine.resolve_slots(a.reference, a.pattern, i)

        if a.effect == AttackEffect.HEAL_ALLIES:
            heals = [(t, min(a.amount_per_slot, p.slots[t].max_hp - p.slots[t].current_hp))
                     for t in candidates if p.slots[t]]
            heals = [(t, h) for t, h in heals if h > 0]
            if not heals:
                continue
            if a.requires_choice:
                heals.sort(key=lambda x: (-x[1], x[0]))
                chosen = [t for t, _ in heals[:a.pick_count]]
                value = sum(h for _, h in heals[:a.pick_count])
                chosen = _pad_targets(chosen, candidates, a.pick_count)
            else:
                chosen, value = None, sum(h for _, h in heals)
            if value > 0 and (best is None or value > best[2]):
                best = (i, chosen, value)
            continue

        dmg = engine.effective_attack_damage(p.slots, i)
        if dmg <= 0:
            continue
        if a.requires_choice:
            ranked = sorted(candidates, key=lambda t: (-_target_contribution(engine, opp, dmg, t), t))
            chosen = ranked[:a.pick_count]
            value = sum(_target_contribution(engine, opp, dmg, t) for t in chosen)
            chosen = _pad_targets(chosen, candidates, a.pick_count)
        else:
            chosen = None
            value = sum(_target_contribution(engine, opp, dmg, t) for t in candidates)
        if value > 0 and (best is None or value > best[2]):
            best = (i, chosen, value)
    return best


def _pad_targets(chosen: List[int], candidates: List[int], pick: int) -> List[int]:
    """El motor exige exactamente pick_count targets válidos; rellena con candidatos sobrantes."""
    out = list(chosen)
    for c in candidates:
        if len(out) >= pick:
            break
        if c not in out:
            out.append(c)
    return out[:pick]


# ── Turno completo ───────────────────────────────────────────────────────────

def take_turn(engine: GameEngine):
    p, opp = engine.active_player, engine.opponent
    engine.action_turns += 1
    if not any(p.can_afford(c) for c in p.hand):
        engine.starved_turns += 1

    # 1) Carta: mejor jugada asequible
    best_plan: Optional[CardPlan] = None
    for idx in range(len(p.hand)):
        if not p.can_afford(p.hand[idx]):
            continue
        plan = evaluate_card(engine, p, opp, idx)
        if best_plan is None or plan.score > best_plan.score:
            best_plan = plan

    if best_plan is not None and best_plan.score > THRESHOLD:
        engine.play_card(**best_plan.params)
    else:
        worst = min(range(len(p.hand)), key=lambda i: _discard_potential(p.hand[i]))
        engine.discard_card(worst)

    if engine.is_finished:
        return

    # 2) Ataque: mejor unidad/objetivos
    atk = best_attack(engine, engine.active_player, engine.opponent)
    if atk is not None:
        attacker_slot, chosen, _ = atk
        engine.attack_with_unit(attacker_slot, chosen)
