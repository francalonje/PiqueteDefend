using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Provee el pool de cartas y las unidades iniciales de cada facción. Inyectado en el motor
    /// para mantener el núcleo libre de carga de assets. En el juego lo implementa un asset;
    /// en los tests, listas en memoria.
    /// </summary>
    public interface ICardCatalog
    {
        IReadOnlyList<CardData> GetPool(Faction faction);

        /// <summary>
        /// Composición del mazo de robo (spec §8.1): el multiset de cartas (con copias) que se baraja al
        /// inicio. v1: el pool expandido por <see cref="CardData.drawWeight"/> (drawWeight = nº de copias).
        /// </summary>
        IReadOnlyList<CardData> GetDeckList(Faction faction);

        /// <summary>Unidades que la facción despliega gratis al empezar la partida (spec §6).</summary>
        IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction);
    }
}
