using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Un slot ocupado en la zona de unidades de un jugador. Apila la misma unidad
    /// hasta <see cref="GameConfig.maxStack"/> mediante el contador <see cref="count"/>.
    /// </summary>
    [Serializable]
    public class UnitSlot
    {
        public CardData unitData;
        public int count;

        public UnitSlot(CardData unitData, int count = 1)
        {
            this.unitData = unitData;
            this.count = count;
        }
    }
}
