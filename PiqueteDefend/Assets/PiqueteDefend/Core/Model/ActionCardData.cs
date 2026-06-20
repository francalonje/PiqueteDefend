using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Carta de acción de un solo uso (spec §7.1). Al jugarse, resuelve sus
    /// <see cref="effects"/> en orden y se repone con otra del pool.
    /// </summary>
    [CreateAssetMenu(fileName = "Action", menuName = "PiqueteDefend/Action Card", order = 1)]
    public sealed class ActionCardData : CardData
    {
        public override CardType CardType => CardType.Accion;

        [Header("Acción")]
        [Tooltip("Categoría visual/temática. No afecta la lógica.")]
        public ActionCategory actionCategory;

        public List<CardEffect> effects = new List<CardEffect>();
    }
}
