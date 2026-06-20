using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Una unidad desplegada en un slot del tablero (spec §7.4). La posición es implícita:
    /// es el índice de este slot en <see cref="PlayerState.unitSlots"/>.
    ///
    /// Los stats efectivos se derivan de <see cref="unit"/> (base, inmutable); sólo
    /// <see cref="currentHp"/> es estado mutable. <see cref="count"/> y
    /// <see cref="attachedEquipment"/> son puntos de extensión [FUTURO] (apilamiento, equipo),
    /// inactivos hoy.
    /// </summary>
    public sealed class UnitSlot
    {
        public readonly UnitCardData unit;
        public int currentHp;

        /// <summary>[FUTURO] apilamiento. Default 1, sin mecánica activa.</summary>
        public int count = 1;

        /// <summary>[FUTURO] equipo adjunto mientras la unidad viva.</summary>
        public readonly List<CardData> attachedEquipment = new List<CardData>();

        public UnitSlot(UnitCardData unit)
        {
            this.unit = unit;
            currentHp = MaxHp;
        }

        /// <summary>HP máximo efectivo (base × count; +equipo [FUTURO]).</summary>
        public int MaxHp => unit.maxHp * count;

        public bool IsDead => currentHp <= 0;
    }
}
