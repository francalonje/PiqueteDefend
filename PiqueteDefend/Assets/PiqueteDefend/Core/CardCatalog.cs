using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Catálogo de cartas como asset (ScriptableObject). Implementa <see cref="ICardCatalog"/>:
    /// es lo que el <see cref="GameEngine"/> consume para armar manos y reponer cartas.
    /// Lo genera el editor a partir de <see cref="CardLibrary"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "PiqueteDefend/Card Catalog", order = 1)]
    public class CardCatalog : ScriptableObject, ICardCatalog
    {
        public List<CardData> manifestantes = new List<CardData>();
        public List<CardData> policias = new List<CardData>();

        public IReadOnlyList<CardData> GetPool(Faction faction) =>
            faction == Faction.Manifestantes ? manifestantes : policias;
    }
}
