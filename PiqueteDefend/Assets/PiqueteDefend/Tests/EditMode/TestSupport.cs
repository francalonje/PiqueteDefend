using System.Collections.Generic;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>RNG determinista para tests: coinflip fijo y Choice = primer elemento del pool.</summary>
    public sealed class FakeRandom : IRandomProvider
    {
        private readonly int _coin;
        public FakeRandom(int coin = 0) => _coin = coin;
        public int Next(int maxExclusive) => _coin % maxExclusive;
        public T Choice<T>(IReadOnlyList<T> list) => list[0];
    }

    /// <summary>Catálogo en memoria: pools por facción a partir de listas dadas.</summary>
    public sealed class TestCatalog : ICardCatalog
    {
        private readonly Dictionary<Faction, IReadOnlyList<CardData>> _pools;

        public TestCatalog(IReadOnlyList<CardData> manifestantes, IReadOnlyList<CardData> policias)
        {
            _pools = new Dictionary<Faction, IReadOnlyList<CardData>>
            {
                { Faction.Manifestantes, manifestantes },
                { Faction.Policias, policias }
            };
        }

        public IReadOnlyList<CardData> GetPool(Faction faction) => _pools[faction];
    }

    /// <summary>
    /// Helpers de cartas para tests. Delega en <see cref="CardLibrary"/> (fuente de verdad única),
    /// para que tests y assets generados nunca se desincronicen.
    /// </summary>
    public static class TestCards
    {
        public static (List<CardData> manifestantes, List<CardData> policias) BuildAll()
            => (CardLibrary.BuildManifestantes(), CardLibrary.BuildPolicias());

        public static CardData Find(IReadOnlyList<CardData> pool, string id)
        {
            foreach (var c in pool)
                if (c.id == id) return c;
            return null;
        }
    }
}
