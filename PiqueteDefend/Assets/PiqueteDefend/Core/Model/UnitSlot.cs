using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Una unidad desplegada en un slot del tablero (spec §7.4). La posición es implícita:
    /// es el índice de este slot en <see cref="PlayerState.unitSlots"/>.
    ///
    /// Los <b>stats efectivos</b> (maxHp, daño) se derivan de <see cref="unit"/> (base, inmutable)
    /// + <see cref="attachedEquipment"/>; sólo <see cref="currentHp"/> y <see cref="activeStatuses"/>
    /// son estado mutable. El daño efectivo final (con Furia/Aura/Desmoralizar) lo calcula el
    /// <see cref="GameEngine"/>, que conoce los vecinos.
    /// </summary>
    public sealed class UnitSlot
    {
        public readonly UnitCardData unit;
        public int currentHp;

        /// <summary>Cada unidad ataca una vez por turno (spec §6). El motor lo resetea al inicio del
        /// turno de su dueño (<see cref="GameEngine.BeginTurn"/>) y lo marca al atacar.</summary>
        public bool attackedThisTurn;

        /// <summary>
        /// [FUTURO] apilamiento. Default 1, sin mecánica activa. Al activarlo, el Core ya multiplica
        /// MaxHp y la producción por count; OJO: el simulador (`sim/`) hoy NO lo hace — alinear ambos
        /// (y re-validar balance) antes de habilitar el stacking.
        /// </summary>
        public int count = 1;

        /// <summary>Equipo adjunto mientras la unidad viva (spec §8.4). Se destruye con ella.</summary>
        public readonly List<EquipmentCardData> attachedEquipment = new List<EquipmentCardData>();

        /// <summary>Estados por unidad activos (Poison/Stun/Furia/Desmoralizar, spec §7.7).</summary>
        public readonly List<StatusEffect> activeStatuses = new List<StatusEffect>();

        public UnitSlot(UnitCardData unit)
        {
            this.unit = unit;
            currentHp = MaxHp;
        }

        /// <summary>HP máximo efectivo: (base + Σ equipo +MaxHp) × count.</summary>
        public int MaxHp
        {
            get
            {
                int bonus = 0;
                foreach (EquipmentCardData e in attachedEquipment)
                    foreach (StatModifier m in e.statModifiers)
                        if (m.stat == StatType.MaxHp) bonus += m.value;
                return (unit.maxHp + bonus) * count;
            }
        }

        /// <summary>+daño aportado por el equipo (no incluye Furia/Aura/Desmoralizar, que dependen del tablero).</summary>
        public int EquipmentDamage
        {
            get
            {
                int bonus = 0;
                foreach (EquipmentCardData e in attachedEquipment)
                    foreach (StatModifier m in e.statModifiers)
                        if (m.stat == StatType.Damage) bonus += m.value;
                return bonus;
            }
        }

        public bool IsDead => currentHp <= 0;

        public int StatusValue(StatusType type)
        {
            int total = 0;
            foreach (StatusEffect s in activeStatuses)
                if (s.statusType == type) total += s.value;
            return total;
        }

        public bool HasStatus(StatusType type)
        {
            foreach (StatusEffect s in activeStatuses)
                if (s.statusType == type) return true;
            return false;
        }

        public bool IsStunned => HasStatus(StatusType.Stun);

        /// <summary>Pasivas efectivas: las propias de la unidad + las otorgadas por el equipo (spec §8.4).</summary>
        public IEnumerable<PassiveEffect> AllPassives()
        {
            foreach (PassiveEffect p in unit.passiveEffects) yield return p;
            foreach (EquipmentCardData e in attachedEquipment)
                foreach (PassiveEffect p in e.grantedPassives) yield return p;
        }

        /// <summary>Adjunta un equipo: el +MaxHp se siente como buff (sube el HP actual el mismo delta).</summary>
        public void Attach(EquipmentCardData equipment)
        {
            int before = MaxHp;
            attachedEquipment.Add(equipment);
            currentHp += MaxHp - before;
        }
    }
}
