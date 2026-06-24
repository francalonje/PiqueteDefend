"""Catálogo de cartas — espejo de docs/game-spec.md §9/§10 (44 cartas: 22/facción).

Los valores de HP/daño/costo/producción son los del spec (provisionales) y se escalan
con los knobs globales al construir. Índices en base 0: los `Absolute`/`allowedSlots`
ya vienen convertidos (slot k del spec = índice k-1); los offsets `Relative` no cambian.

Presets de zona (§6), en base 0:
  RETAGUARDIA = [0,1,2]   FRENTE = [3,4,5]   (vanguardia enemiga = frente enemigo = [3,4,5])
"""

from __future__ import annotations

from typing import Dict, List

from knobs import GlobalKnobs, scale
from model import (
    ActionCardData, AttackEffect, CardData, CardEffect, CardEffectType,
    CardType, EquipmentCardData, Faction, PassiveEffect, PassiveTarget, PassiveType,
    ResourceCost, ResourceType, StatModifier, StatType, StatusEffect, StatusType, TargetMode,
    TargetType, UnitAttack, UnitCardData,
)

M = Faction.MANIFESTANTES
P = Faction.POLICIAS
DIN, FZA, SOC = ResourceType.DINERO, ResourceType.FUERZA, ResourceType.SOCIAL


def TS() -> TargetType:
    return TargetType.SELF


def TO() -> TargetType:
    return TargetType.OPPONENT

RETAGUARDIA = [0, 1, 2]
FRENTE = [3, 4, 5]


# ── Builders (aplican knobs) ─────────────────────────────────────────────────

def _cost(res: ResourceType, amount: int, k: GlobalKnobs) -> List[ResourceCost]:
    return [ResourceCost(res, scale(amount, k.cost_mult, minimum=1))]


def unit(id, name, faction, archetype, cost_res, cost, max_hp, allowed, attack,
         passives, k: GlobalKnobs) -> UnitCardData:
    # Robo: unidades pesan 1; productoras 2 (protagonismo de producción sin tapar la mano). Espejo de CardLibrary.
    dw = 2 if any(p.passive_type == PassiveType.PRODUCE_RESOURCE for p in passives) else 1
    return UnitCardData(
        id=id, name=name, faction=faction, costs=_cost(cost_res, cost, k),
        card_type=CardType.UNIDAD, max_hp=scale(max_hp, k.hp_mult, minimum=1),
        allowed_slots=list(allowed), attack=attack, passive_effects=passives,
        archetype=archetype, draw_weight=dw,
    )


def atk(mode, count, amount, k: GlobalKnobs,
        effect=AttackEffect.DAMAGE_ENEMIES) -> UnitAttack:
    mult = k.hp_mult if effect == AttackEffect.HEAL_ALLIES else k.dmg_mult
    return UnitAttack(mode, count, scale(amount, mult, minimum=1), effect)


def produce(res, value, k: GlobalKnobs) -> PassiveEffect:
    return PassiveEffect(PassiveType.PRODUCE_RESOURCE, value=scale(value, k.producer_mult, minimum=1),
                         resource=res)


def aura(value, k: GlobalKnobs) -> PassiveEffect:
    # +daño a aliadas adyacentes (vecinas ±1)
    return PassiveEffect(PassiveType.AURA_DAMAGE, value=scale(value, k.dmg_mult, minimum=1),
                         target=PassiveTarget.ALLIES, mode=TargetMode.ADJACENT, count=0)


def retaliate(value, k: GlobalKnobs) -> PassiveEffect:
    return PassiveEffect(PassiveType.RETALIATE, value=scale(value, k.dmg_mult, minimum=1),
                         target=PassiveTarget.SELF)


def regen(value, k: GlobalKnobs) -> PassiveEffect:
    return PassiveEffect(PassiveType.REGENERATION, value=scale(value, k.hp_mult, minimum=1),
                         target=PassiveTarget.SELF)


def turn_damage(value, k: GlobalKnobs) -> PassiveEffect:
    # Humo: daño/turno a TODO el tablero rival (AoE). Spec §7.3/§9.
    return PassiveEffect(PassiveType.TURN_DAMAGE, value=scale(value, k.dmg_mult, minimum=1),
                         target=PassiveTarget.ENEMIES, mode=TargetMode.ALL, count=0)


def turn_status(status, k: GlobalKnobs) -> PassiveEffect:
    # Gas: estado/turno a TODO el tablero rival (AoE). Spec §7.3/§10.
    return PassiveEffect(PassiveType.TURN_STATUS, status=status, target=PassiveTarget.ENEMIES,
                         mode=TargetMode.ALL, count=0)


def on_death_furia(value, counter, k: GlobalKnobs) -> PassiveEffect:
    # Jubilado mártir: al morir, Furia a los aliados adyacentes (spec §7.3/§9).
    return PassiveEffect(PassiveType.ONDEATH, status=furia(value, counter, k),
                         target=PassiveTarget.ALLIES, mode=TargetMode.ADJACENT, count=0)


def blindaje(value, k: GlobalKnobs) -> PassiveEffect:
    # Blindaje: reduce el daño de ataques de unidad (spec §7.3, Policías).
    return PassiveEffect(PassiveType.ARMOR, value=scale(value, k.dmg_mult, minimum=1),
                         target=PassiveTarget.SELF)


def chorro(k: GlobalKnobs) -> PassiveEffect:
    # Chorro: al atacar, empuja al objetivo al fondo del rival (spec §7.3, Policías).
    return PassiveEffect(PassiveType.PUSHBACK, target=PassiveTarget.SELF)


def action(id, name, faction, category, cost_res, cost, effects, k: GlobalKnobs) -> ActionCardData:
    # Las cartas de producción (boost de recurso propio) pesan 2 en el robo. Espejo de CardLibrary.
    dw = 2 if any(e.effect_type == CardEffectType.MODIFY_RESOURCE and e.target == TargetType.SELF
                  and e.value > 0 for e in effects) else 1
    return ActionCardData(id=id, name=name, faction=faction, costs=_cost(cost_res, cost, k),
                          card_type=CardType.ACCION, effects=effects, category=category, draw_weight=dw)


def equip(id, name, faction, cost_res, cost, mods, passives, k: GlobalKnobs) -> EquipmentCardData:
    return EquipmentCardData(id=id, name=name, faction=faction, costs=_cost(cost_res, cost, k),
                             card_type=CardType.EQUIPO, stat_modifiers=mods, granted_passives=passives)


# Efectos de acción (helpers)
def mod_res(target, res, value, k: GlobalKnobs) -> CardEffect:
    # El "value" de boost/drenaje es economía → no se escala con dmg/hp (se deja tal cual;
    # la economía se escala vía recursos globales, no por carta).
    return CardEffect(CardEffectType.MODIFY_RESOURCE, target, resource_target=res, value=value)


def mod_hp(target, value, k: GlobalKnobs) -> CardEffect:
    mult = k.hp_mult if value > 0 else k.dmg_mult   # cura escala con hp, daño con dmg
    signed = scale(abs(value), mult, minimum=1) * (1 if value > 0 else -1)
    return CardEffect(CardEffectType.MODIFY_HP, target, value=signed)


def apply_status(target, status: StatusEffect) -> CardEffect:
    return CardEffect(CardEffectType.APPLY_STATUS, target, status=status)


def move_unit(target) -> CardEffect:
    return CardEffect(CardEffectType.MOVE_UNIT, target)


def swap_units(target) -> CardEffect:
    return CardEffect(CardEffectType.SWAP_UNITS, target)


def furia(value, counter, k: GlobalKnobs) -> StatusEffect:
    return StatusEffect(StatusType.FURIA, scale(value, k.dmg_mult, minimum=1), counter)


def desmoralizar(value, counter, k: GlobalKnobs) -> StatusEffect:
    return StatusEffect(StatusType.DESMORALIZAR, scale(value, k.dmg_mult, minimum=1), counter)


def poison(value, counter, k: GlobalKnobs) -> StatusEffect:
    return StatusEffect(StatusType.POISON, scale(value, k.dmg_mult, minimum=1), counter)


# ── Manifestantes (§9) ───────────────────────────────────────────────────────

def build_manifestantes(k: GlobalKnobs) -> List[CardData]:
    HEAL = AttackEffect.HEAL_ALLIES
    return [
        # Unidades (9)
        unit("piquetero", "Piquetero", M, "Escaramuza", FZA, 4, 20, [],
             atk(TargetMode.FRONTMOST, 1, 14, k), [aura(2, k)], k),
        unit("fisura", "Fisura", M, "Cleave", FZA, 5, 18, [1, 2, 3, 4],
             atk(TargetMode.FRONTMOST, 3, 7, k), [produce(FZA, 1, k)], k),
        unit("jubilado", "Jubilado", M, "Martir", FZA, 2, 6, [],
             atk(TargetMode.FRONTMOST, 1, 2, k), [on_death_furia(4, 2, k)], k),
        unit("mortero", "Mortero Casero", M, "Morterista", FZA, 5, 8, [1, 2, 3],
             atk(TargetMode.BACKMOST, 1, 14, k), [], k),
        unit("encadenado", "Encadenado", M, "Muro", DIN, 5, 32, FRENTE,
             atk(TargetMode.FRONTMOST, 2, 3, k), [retaliate(3, k)], k),
        unit("gordo_sindical", "Gordo Sindical", M, "Productora", DIN, 3, 12, RETAGUARDIA,
             atk(TargetMode.FRONTMOST, 1, 3, k), [produce(DIN, 2, k)], k),
        unit("choripanero", "Choripanero", M, "Healer", DIN, 4, 15, [1, 2, 3, 4],
             atk(TargetMode.ANY, 1, 3, k, effect=HEAL), [], k),
        unit("tuitero", "Tuitero Militante", M, "Productora", SOC, 2, 10, RETAGUARDIA,
             atk(TargetMode.FRONTMOST, 1, 2, k), [produce(SOC, 2, k)], k),
        unit("quema_cubiertas", "Quema de Cubiertas", M, "Emisor", SOC, 5, 15, [1, 2, 3, 4],
             atk(TargetMode.FRONTMOST, 1, 2, k), [turn_damage(2, k)], k),

        # Acciones (8)
        action("colecta", "Colecta", M, "Boost", SOC, 3, [mod_res(TS(), DIN, 6, k)], k),
        action("fernet", "Fernet con Cola", M, "Boost", DIN, 1, [mod_res(TS(), FZA, 3, k)], k),
        action("viral", "Viral en Redes", M, "Boost", DIN, 2, [mod_res(TS(), SOC, 7, k)], k),
        action("paro_general", "Paro General", M, "Ataque", FZA, 5, [mod_hp(TO(), -21, k)], k),
        action("el_aguante", "El Aguante", M, "Boost", FZA, 2, [apply_status(TS(), furia(4, 2, k))], k),
        action("asamblea", "Asamblea Popular", M, "Especial", SOC, 6,
               [apply_status(TS(), StatusEffect(StatusType.DOUBLE_PRODUCTION, 2, 1))], k),
        action("abrazo", "Abrazo Colectivo", M, "Defensa", DIN, 5, [mod_hp(TS(), 10, k)], k),
        action("escrache", "Escrache", M, "Control", SOC, 4,
               [apply_status(TO(), StatusEffect(StatusType.STUN, 0, 1))], k),

        # Equipo (4)
        equip("pechera", "Pechera de Cartón", M, DIN, 3, [StatModifier(StatType.MAX_HP, scale(10, k.hp_mult, 1))], [], k),
        equip("cascote", "Cascote", M, FZA, 2, [StatModifier(StatType.DAMAGE, scale(4, k.dmg_mult, 1))], [], k),
        equip("parrilla", "Parrilla Portátil", M, DIN, 3, [], [regen(2, k)], k),
        equip("miguelitos", "Miguelitos", M, FZA, 3, [], [retaliate(4, k)], k),
    ]


# ── Policías (§10) ───────────────────────────────────────────────────────────

def build_policias(k: GlobalKnobs) -> List[CardData]:
    HEAL = AttackEffect.HEAL_ALLIES
    return [
        # Unidades (9)
        unit("infante", "Infante", P, "Escaramuza", FZA, 5, 24, [],
             atk(TargetMode.FRONTMOST, 1, 14, k), [], k),
        unit("itakero", "Itakero", P, "Cleave", FZA, 4, 20, [1, 2, 3, 4],
             atk(TargetMode.FRONTMOST, 3, 4, k), [], k),
        unit("halcon", "Halcón", P, "Sniper", FZA, 6, 8, [1, 2, 3],
             atk(TargetMode.ANY, 1, 15, k), [], k),
        unit("gendarme", "Gendarme", P, "Muro", DIN, 5, 26, FRENTE,
             atk(TargetMode.FRONTMOST, 2, 4, k), [blindaje(2, k)], k),
        unit("carro_hidrante", "Carro Hidrante", P, "Control", DIN, 4, 18, [1, 2, 3, 4],
             atk(TargetMode.FRONTMOST, 1, 3, k), [chorro(k)], k),
        unit("recaudador", "Recaudador", P, "Productora", DIN, 3, 12, RETAGUARDIA,
             atk(TargetMode.FRONTMOST, 1, 3, k), [produce(DIN, 2, k)], k),
        unit("caballeria", "Caballería", P, "Carga", SOC, 6, 16, [1, 2, 3, 4],
             atk(TargetMode.ALL, 0, 2, k), [], k),
        unit("trol", "Trol Oficial", P, "Productora", SOC, 3, 14, RETAGUARDIA,
             atk(TargetMode.FRONTMOST, 1, 2, k), [produce(SOC, 2, k)], k),
        unit("gasero", "Gasero", P, "Emisor", SOC, 5, 15, [1, 2, 3, 4],
             atk(TargetMode.FRONTMOST, 1, 2, k),
             # Veneno board-wide re-emitido cada turno: counter 1 = 1 tick/turno, sin apilarse (roadmap).
             [turn_status(poison(2, 1, k), k)], k),

        # Acciones (8)
        action("partida", "Partida Presupuestaria", P, "Boost", SOC, 2, [mod_res(TS(), DIN, 7, k)], k),
        action("licitacion", "Licitación Express", P, "Boost", DIN, 3, [mod_res(TS(), FZA, 10, k)], k),
        action("cadena", "Cadena Nacional", P, "Boost", DIN, 2, [mod_res(TS(), SOC, 4, k)], k),
        action("operativo", "Operativo Apretón", P, "Ataque", DIN, 6, [mod_hp(TO(), -27, k)], k),
        action("causa_judicial", "Causa Judicial", P, "Sabotaje", DIN, 4, [apply_status(TO(), poison(3, 2, k))], k),
        action("apriete", "Apriete", P, "Sabotaje", FZA, 2, [apply_status(TO(), desmoralizar(4, 2, k))], k),
        action("toque_queda", "Toque de Queda", P, "Especial", DIN, 5,
               [apply_status(TO(), StatusEffect(StatusType.SKIP_PRODUCTION, 0, 1))], k),
        action("reubicacion", "Reubicación Forzosa", P, "Especial", DIN, 2, [swap_units(TO())], k),

        # Equipo (4)
        equip("chaleco", "Chaleco Antibalas", P, DIN, 3, [StatModifier(StatType.MAX_HP, scale(12, k.hp_mult, 1))], [], k),
        equip("tonfa", "Tonfa", P, FZA, 2, [StatModifier(StatType.DAMAGE, scale(4, k.dmg_mult, 1))], [], k),
        equip("escudo_antimotin", "Escudo Antimotín", P, DIN, 3, [], [blindaje(2, k)], k),
        equip("hidrante_mano", "Hidrante de Mano", P, SOC, 3, [], [chorro(k)], k),
    ]


# ── Pools y unidades iniciales ───────────────────────────────────────────────

STARTING_IDS: Dict[Faction, List[str]] = {
    M: ["encadenado", "gordo_sindical", "piquetero"],   # Muro(adelante) + Productora + Escaramuza
    P: ["gendarme", "recaudador", "infante"],
}


def build_pool(faction: Faction, k: GlobalKnobs) -> List[CardData]:
    return build_manifestantes(k) if faction == M else build_policias(k)


def starting_units(faction: Faction, k: GlobalKnobs) -> List[UnitCardData]:
    import os
    env = os.environ.get("PD_START_M" if faction == M else "PD_START_P")
    ids = [s for s in env.split(",") if s] if env else STARTING_IDS[faction]
    pool = {c.id: c for c in build_pool(faction, k)}
    return [pool[i] for i in ids]  # type: ignore[misc]
