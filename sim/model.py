"""Modelo de dominio del simulador (espejo de PiqueteDefend.Core).

Re-implementa, en Python puro, los tipos del spec (§7): enums, plantillas de carta
(unidad/acción/equipo), ataque, pasiva y status. NO conoce a Unity. Los stats que el
balanceo escala (HP, daño, costo, producción) viven como valores base acá y se
multiplican por los knobs globales (knobs.py) al construir el catálogo (cards.py).

Convención de índices: slots 0–5 (base 0, igual que el código C#). Los offsets
`Relative` son idénticos a los del spec; los `Absolute`/`allowedSlots` ya vienen
convertidos a base 0 en cards.py (slot k del spec = índice k-1).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import List, Optional


# ── Enums (spec §7) ──────────────────────────────────────────────────────────

class Faction(Enum):
    MANIFESTANTES = "Manifestantes"
    POLICIAS = "Policias"


class ResourceType(Enum):
    DINERO = "Dinero"
    FUERZA = "Fuerza"
    SOCIAL = "Social"


ALL_RESOURCES = (ResourceType.DINERO, ResourceType.FUERZA, ResourceType.SOCIAL)


class CardType(Enum):
    UNIDAD = "Unidad"
    ACCION = "Accion"
    EQUIPO = "Equipo"


class TargetMode(Enum):
    """Cómo se eligen objetivos, anclado a la formación (spec §6). El frente del tablero
    objetivo es el extremo de índice alto (cerca del rival)."""
    FRONTMOST = "Frontmost"   # la más adelantada ocupada + (count-1) hacia el fondo
    BACKMOST = "Backmost"     # la más atrasada ocupada + (count-1) hacia el frente
    ANY = "Any"               # count unidades a elección (snipe)
    ALL = "All"               # todas (AoE)
    ADJACENT = "Adjacent"     # vecinas (±1) de la fuente (auras)
    SELF = "Self"             # la propia fuente


class AttackEffect(Enum):
    DAMAGE_ENEMIES = "DamageEnemies"   # afecta el tablero rival, dañando
    HEAL_ALLIES = "HealAllies"         # afecta el tablero propio, curando


class PassiveType(Enum):
    PRODUCE_RESOURCE = "ProduceResource"
    REGENERATION = "Regeneration"
    AURA_DAMAGE = "AuraDamage"
    RETALIATE = "Retaliate"
    TURN_DAMAGE = "TurnDamage"
    TURN_STATUS = "TurnStatus"
    ONDEATH = "OnDeath"      # death-rattle (spec §7.3): dispara al morir (cualquier fuente)
    ARMOR = "Armor"          # Blindaje: reduce el daño de ataques de unidad
    PUSHBACK = "PushBack"    # Chorro: empuja al objetivo al fondo tras atacar


class PassiveTarget(Enum):
    SELF = "Self"
    ALLIES = "Allies"
    ENEMIES = "Enemies"


class StatusType(Enum):
    SKIP_PRODUCTION = "SkipProduction"      # jugador
    DOUBLE_PRODUCTION = "DoubleProduction"  # jugador
    POISON = "Poison"                        # unidad
    STUN = "Stun"                            # unidad
    FURIA = "Furia"                          # unidad
    DESMORALIZAR = "Desmoralizar"            # unidad


PLAYER_STATUSES = (StatusType.SKIP_PRODUCTION, StatusType.DOUBLE_PRODUCTION)


class CardEffectType(Enum):
    MODIFY_HP = "ModifyHP"
    MODIFY_RESOURCE = "ModifyResource"
    REMOVE_UNIT = "RemoveUnit"
    APPLY_STATUS = "ApplyStatus"
    MOVE_UNIT = "MoveUnit"
    SWAP_UNITS = "SwapUnits"


class TargetType(Enum):
    SELF = "Self"
    OPPONENT = "Opponent"


class StatType(Enum):
    MAX_HP = "MaxHp"
    DAMAGE = "Damage"


# ── Estructuras compartidas (spec §7) ────────────────────────────────────────

@dataclass
class ResourceCost:
    resource: ResourceType
    amount: int


@dataclass
class StatusEffect:
    status_type: StatusType
    value: int = 0
    counter: int = 1

    def clone(self) -> "StatusEffect":
        return StatusEffect(self.status_type, self.value, self.counter)


@dataclass
class UnitAttack:
    mode: TargetMode
    count: int                                 # profundidad/alcance (Frontmost/Backmost) o cuántas elegir (Any)
    amount_per_slot: int                       # daño si DamageEnemies, cura si HealAllies
    effect: AttackEffect = AttackEffect.DAMAGE_ENEMIES
    hits: int = 1                              # golpes por objetivo (multi-hit): el daño se aplica `hits` veces al mismo target

    @property
    def requires_choice(self) -> bool:
        return self.mode == TargetMode.ANY

    @property
    def effective_hits(self) -> int:
        return self.hits if self.hits >= 1 else 1


@dataclass
class PassiveEffect:
    passive_type: PassiveType
    value: int = 0
    resource: Optional[ResourceType] = None         # sólo ProduceResource
    status: Optional[StatusEffect] = None            # sólo TurnStatus
    target: PassiveTarget = PassiveTarget.SELF
    mode: TargetMode = TargetMode.SELF
    count: int = 1


@dataclass
class StatModifier:
    stat: StatType
    value: int


@dataclass
class CardEffect:
    effect_type: CardEffectType
    target: TargetType
    resource_target: Optional[ResourceType] = None
    value: int = 0
    status: Optional[StatusEffect] = None
    target_slot: int = -1     # -1 = lo elige la política
    target_slot_b: int = -1   # MoveUnit destino / SwapUnits segundo slot

    @property
    def targets_a_unit_slot(self) -> bool:
        """True si el efecto recae sobre una unidad en un slot (necesita elección)."""
        if self.effect_type in (CardEffectType.MODIFY_HP, CardEffectType.REMOVE_UNIT,
                                 CardEffectType.MOVE_UNIT, CardEffectType.SWAP_UNITS):
            return True
        # ApplyStatus a una unidad (los estados de producción van al jugador)
        if self.effect_type == CardEffectType.APPLY_STATUS and self.status is not None:
            return self.status.status_type not in PLAYER_STATUSES
        return False


# ── Plantillas de carta (spec §7.1) ──────────────────────────────────────────

@dataclass
class CardData:
    id: str
    name: str
    faction: Faction
    costs: List[ResourceCost]
    card_type: CardType
    draw_weight: int = 1


@dataclass
class UnitCardData(CardData):
    max_hp: int = 0
    allowed_slots: List[int] = field(default_factory=list)   # vacío = cualquiera
    attack: Optional[UnitAttack] = None
    passive_effects: List[PassiveEffect] = field(default_factory=list)
    archetype: str = ""   # diagnóstico/política (Escaramuza, Muro, Productora, ...)

    def allows_slot(self, idx: int) -> bool:
        return not self.allowed_slots or idx in self.allowed_slots


@dataclass
class ActionCardData(CardData):
    effects: List[CardEffect] = field(default_factory=list)
    category: str = ""


@dataclass
class EquipmentCardData(CardData):
    stat_modifiers: List[StatModifier] = field(default_factory=list)
    granted_passives: List[PassiveEffect] = field(default_factory=list)
