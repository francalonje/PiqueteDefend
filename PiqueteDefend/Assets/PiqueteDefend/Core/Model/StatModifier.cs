using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Modificador a un stat efectivo de la unidad portadora (spec §7.1). Lo aporta una carta de
    /// <see cref="EquipmentCardData"/> y se suma en la capa de stats efectivos de <see cref="UnitSlot"/>.
    /// </summary>
    [Serializable]
    public class StatModifier
    {
        public StatType stat;
        public int value;

        public StatModifier() { }

        public StatModifier(StatType stat, int value)
        {
            this.stat = stat;
            this.value = value;
        }
    }
}
