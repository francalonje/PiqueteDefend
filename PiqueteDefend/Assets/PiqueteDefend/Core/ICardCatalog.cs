using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Provee el pool de cartas de cada facción. Inyectado en el motor para mantener
    /// el núcleo libre de carga de assets (Resources, Addressables, etc.). En el juego
    /// lo implementa un asset/MonoBehaviour; en los tests, una lista en memoria.
    /// </summary>
    public interface ICardCatalog
    {
        IReadOnlyList<CardData> GetPool(Faction faction);
    }
}
