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

        public IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction) =>
            faction == Faction.Manifestantes ? startingManifestantes : startingPolicias;
    }
}
