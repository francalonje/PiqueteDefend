using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Carta de equipo (spec §7.1 / §8.4). Se juega sobre una unidad propia: le suma
    /// <see cref="statModifiers"/> (+maxHp, +daño) y le otorga <see cref="grantedPassives"/>
    /// mientras esté viva. Se destruye con la unidad (no vuelve al pool).
    /// </summary>
    [CreateAssetMenu(fileName = "Equipment", menuName = "PiqueteDefend/Equipment Card", order = 2)]
    public sealed class EquipmentCardData : CardData
    {
        public override CardType CardType => CardType.Equipo;

        [Header("Equipo")]
        public List<StatModifier> statModifiers = new List<StatModifier>();
        public List<PassiveEffect> grantedPassives = new List<PassiveEffect>();
    }
}
