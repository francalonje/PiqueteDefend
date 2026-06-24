using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Catálogo de cartas como asset (ScriptableObject). Implementa <see cref="ICardCatalog"/>:
    /// es lo que el <see cref="GameEngine"/> consume para armar manos, reponer cartas y desplegar
    /// las unidades iniciales. Lo genera el editor a partir de <see cref="CardLibrary"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "PiqueteDefend/Card Catalog", order = 2)]
    public class CardCatalog : ScriptableObject, ICardCatalog
    {
        public List<CardData> manifestantes = new List<CardData>();
        public List<CardData> policias = new List<CardData>();

        [Header("Unidades iniciales por facción")]
        public List<UnitCardData> startingManifestantes = new List<UnitCardData>();
        public List<UnitCardData> startingPolicias = new List<UnitCardData>();

        public IReadOnlyList<CardData> GetPool(Faction faction) =>
            faction == Faction.Manifestantes ? manifestantes : policias;

        /// <summary>Mazo = pool expandido por <c>drawWeight</c> (cada carta aparece <c>drawWeight</c>
        /// veces, mín. 1). Derivado en runtime: no necesita serializarse ni regenerar el asset.</summary>
        public IReadOnlyList<CardData> GetDeckList(Faction faction) => ExpandByWeight(GetPool(faction));

        private static List<CardData> ExpandByWeight(IReadOnlyList<CardData> pool)
        {
            var deck = new List<CardData>();
            foreach (CardData c in pool)
            {
                int copies = c.drawWeight > 0 ? c.drawWeight : 1;
                for (int i = 0; i < copies; i++) deck.Add(c);
            }
            return deck;
        }

        public IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction) =>
            faction == Faction.Manifestantes ? startingManifestantes : startingPolicias;
    }
}
