"""Chequeo de paridad: cards.py × knobs.SHIPPED == catálogo horneado del Core (spec §9/§10).

El simulador deriva sus números (base × knobs); el Core (CardLibrary.cs) los tiene HORNEADOS.
Coinciden sólo bajo la config adoptada (knobs.SHIPPED). Este script fija esa coincidencia:
si alguien re-tunea knobs, cambia el redondeo o edita los valores base de cards.py sin
re-hornear el Core, FALLA acá (en vez de desincronizarse en silencio).

La tabla EXPECTED es la fuente de verdad del spec §9/§10 (= Core/CardLibrary.cs). Si cambian
los números del juego, se actualizan en AMBOS lados y acá.

Uso:  py sim/parity_check.py    (exit 0 = OK, exit 1 = drift)
"""

from __future__ import annotations

import sys

from cards import build_pool
from knobs import SHIPPED
from model import (ActionCardData, EquipmentCardData, Faction, PassiveType, StatType,
                   UnitCardData)

# id -> (maxHp, atk_amount, {passiveType: value})   — campos escalados por knobs (rework: mults 1.0)
UNITS = {
    "piquetero":       (20, 14, {PassiveType.AURA_DAMAGE: 2}),
    "fisura":          (18, 7,  {PassiveType.PRODUCE_RESOURCE: 1}),
    "jubilado":        (6,  2,  {}),  # OnDeath: value vive en el status Furia (abajo)
    "mortero":         (8,  14, {}),  # Backmost
    "encadenado":      (32, 3,  {PassiveType.RETALIATE: 3}),
    "gordo_sindical":  (12, 3,  {PassiveType.PRODUCE_RESOURCE: 2}),
    "choripanero":     (15, 3,  {}),
    "tuitero":         (10, 2,  {PassiveType.PRODUCE_RESOURCE: 2}),
    "quema_cubiertas": (15, 2,  {PassiveType.TURN_DAMAGE: 2}),
    "infante":         (24, 13, {}),  # vainilla
    "itakero":         (20, 4,  {}),  # vainilla
    "halcon":          (8,  15, {}),
    "gendarme":        (26, 4,  {PassiveType.ARMOR: 2}),
    "carro_hidrante":  (18, 3,  {PassiveType.PUSHBACK: 0}),
    "recaudador":      (12, 3,  {PassiveType.PRODUCE_RESOURCE: 2}),
    "caballeria":      (16, 2,  {}),  # All (carga)
    "trol":            (14, 2,  {PassiveType.PRODUCE_RESOURCE: 2}),
    "gasero":          (15, 2,  {PassiveType.TURN_STATUS: 0}),  # value vive en el status (abajo)
}

# Magnitud del status que algunas unidades aplican (TurnStatus/OnDeath) — value del StatusEffect
UNIT_STATUS_VALUE = {"gasero": 2, "jubilado": 4}

# id -> valor escalado del efecto que importa (ModifyHP firmado, o value/counter de status)
ACTION_HP = {"paro_general": -21, "abrazo": 10, "operativo": -27}
ACTION_STATUS = {  # id -> (value, counter)
    "asamblea": (2, 1), "el_aguante": (4, 2), "causa_judicial": (3, 2), "apriete": (4, 2),
}

# id -> (StatType o PassiveType, value)
EQUIP = {
    "pechera": (StatType.MAX_HP, 10), "cascote": (StatType.DAMAGE, 4),
    "parrilla": (PassiveType.REGENERATION, 2), "miguelitos": (PassiveType.RETALIATE, 4),
    "chaleco": (StatType.MAX_HP, 12), "tonfa": (StatType.DAMAGE, 4),
    "escudo_antimotin": (PassiveType.ARMOR, 2), "hidrante_mano": (PassiveType.PUSHBACK, 0),
}


def check():
    errors = []
    cards = {c.id: c for f in (Faction.MANIFESTANTES, Faction.POLICIAS)
             for c in build_pool(f, SHIPPED)}

    def eq(cid, what, got, exp):
        if got != exp:
            errors.append(f"{cid}: {what} = {got}, esperado {exp}")

    total = len(cards)
    if total != 42:
        errors.append(f"total de cartas = {total}, esperado 42 (21/facción)")

    for cid, (hp, amt, passives) in UNITS.items():
        u = cards.get(cid)
        if not isinstance(u, UnitCardData):
            errors.append(f"{cid}: no es UnitCardData (falta en el pool?)")
            continue
        eq(cid, "maxHp", u.max_hp, hp)
        eq(cid, "atk", u.attack.amount_per_slot, amt)
        for pt, val in passives.items():
            got = next((p.value for p in u.passive_effects if p.passive_type == pt), None)
            eq(cid, f"pasiva {pt.value}", got, val)
        if cid in UNIT_STATUS_VALUE:
            st = next((p.status for p in u.passive_effects if p.status), None)
            eq(cid, "status.value", st.value if st else None, UNIT_STATUS_VALUE[cid])

    for cid, v in ACTION_HP.items():
        a = cards[cid]
        eq(cid, "ModifyHP", a.effects[0].value, v)
    for cid, (val, cnt) in ACTION_STATUS.items():
        st = cards[cid].effects[0].status
        eq(cid, "status.value", st.value, val)
        eq(cid, "status.counter", st.counter, cnt)

    for cid, (kind, val) in EQUIP.items():
        e = cards[cid]
        if isinstance(kind, StatType):
            got = next((m.value for m in e.stat_modifiers if m.stat == kind), None)
        else:
            got = next((p.value for p in e.granted_passives if p.passive_type == kind), None)
        eq(cid, f"equip {kind.value}", got, val)

    return errors


def main():
    errors = check()
    if errors:
        print("PARIDAD ROTA — cards.py × SHIPPED ya no coincide con el Core/spec:\n")
        for e in errors:
            print(f"  ✗ {e}")
        print(f"\n{len(errors)} discrepancia(s). Re-hornear CardLibrary.cs/spec o revisar knobs/cards.")
        sys.exit(1)
    print("OK — cards.py × SHIPPED reproduce el catálogo horneado del Core (42 cartas, spec §9/§10).")


if __name__ == "__main__":
    main()
