using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Carta de unidad persistente (spec §7.1). Define sus stats de combate, dónde puede
    /// desplegarse y sus efectos pasivos.
    /// </summary>
    [CreateAssetMenu(fileName = "Unit", menuName = "PiqueteDefend/Unit Card", order = 0)]
    public sealed class UnitCardData : CardData
    {
        public override CardType CardType => CardType.Unidad;

        [Header("Unidad")]
        public int maxHp;

        [Tooltip("Slots donde puede desplegarse (0–5). Vacío = cualquiera.")]
        public int[] allowedSlots = Array.Empty<int>();

        public UnitAttack attack = new UnitAttack();

        public List<PassiveEffect> passiveEffects = new List<PassiveEffect>();

        [Tooltip("Subtipo temático. Punto de extensión; no afecta la lógica.")]
        public UnitSubtype unitSubtype;

        /// <summary>True si la unidad puede desplegarse en <paramref name="slotIndex"/> (0–5).</summary>
        public bool AllowsSlot(int slotIndex)
        {
            if (allowedSlots == null || allowedSlots.Length == 0) return true; // vacío = cualquiera
            foreach (int s in allowedSlots)
                if (s == slotIndex) return true;
            return false;
        }
    }
}
