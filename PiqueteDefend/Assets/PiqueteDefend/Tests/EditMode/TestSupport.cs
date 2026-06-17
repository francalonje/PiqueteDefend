using System.Collections.Generic;
using UnityEngine;
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
    /// Fábrica de cartas que reproduce el set canónico de tools/balance_sim/simulator.py.
    /// Doble propósito: alimentar los tests y documentar que la data C# coincide con el simulador.
    /// </summary>
    public static class TestCards
    {
        public static CardData Find(IReadOnlyList<CardData> pool, string id)
        {
            foreach (var c in pool)
                if (c.id == id) return c;
            return null;
        }

        public static (List<CardData> manifestantes, List<CardData> policias) BuildAll()
        {
            const ResourceType D = ResourceType.Dinero, F = ResourceType.Fuerza, S = ResourceType.Social;
            const TargetType SELF = TargetType.Self, OPP = TargetType.Opponent;

            var manifestantes = new List<CardData>
            {
                Unit("piquetero", Faction.Manifestantes, UnitSubtype.Atacante, d: 2, f: 2),
                Unit("jubilado", Faction.Manifestantes, UnitSubtype.Defensiva, d: 5, f: 1),
                Unit("olla_popular", Faction.Manifestantes, UnitSubtype.Productora, d: 2, s: 1, prod: D),
                Unit("quilombero", Faction.Manifestantes, UnitSubtype.Productora, f: 4, s: 1, prod: F),
                Unit("tuitero", Faction.Manifestantes, UnitSubtype.Productora, d: 1, s: 1, prod: S),
                Action("colecta", Faction.Manifestantes, ActionCategory.Boost, s: 3, effects: new[] { ModRes(SELF, D, 6) }),
                Action("fernet", Faction.Manifestantes, ActionCategory.Boost, d: 1, effects: new[] { ModRes(SELF, F, 3) }),
                Action("viral_redes", Faction.Manifestantes, ActionCategory.Boost, d: 2, effects: new[] { ModRes(SELF, S, 7) }),
                Action("saqueo", Faction.Manifestantes, ActionCategory.Sabotaje, f: 1, effects: new[] { ModRes(OPP, D, -3) }),
                Action("asamblea_6hs", Faction.Manifestantes, ActionCategory.Sabotaje, d: 2, effects: new[] { ModRes(OPP, F, -7) }),
                Action("fake_news", Faction.Manifestantes, ActionCategory.Sabotaje, s: 3, effects: new[] { ModRes(OPP, S, -5) }),
                Action("romper_marcha", Faction.Manifestantes, ActionCategory.Sabotaje, f: 1, s: 3, effects: new[] { RemoveUnit(OPP) }),
                Action("paro_general", Faction.Manifestantes, ActionCategory.Ataque, d: 2, f: 3, effects: new[] { ModHP(OPP, -14) }),
                Action("abrazo_colectivo", Faction.Manifestantes, ActionCategory.Defensa, d: 4, s: 1, effects: new[] { ModHP(SELF, 16) }),
                Action("corte_ruta", Faction.Manifestantes, ActionCategory.EfectoEspecial, f: 1, s: 2, effects: new[] { ApplyStatus(OPP, Skip()) }),
                Action("asamblea_popular", Faction.Manifestantes, ActionCategory.EfectoEspecial, s: 6, effects: new[] { ApplyStatus(SELF, Double()) }),
            };

            var policias = new List<CardData>
            {
                Unit("patrullero", Faction.Policias, UnitSubtype.Atacante, f: 4, d: 2),
                Unit("comisaria", Faction.Policias, UnitSubtype.Defensiva, d: 2, f: 1),
                Unit("subsidio", Faction.Policias, UnitSubtype.Productora, d: 4, s: 1, prod: D),
                Unit("gorra_barrio", Faction.Policias, UnitSubtype.Productora, f: 1, s: 2, prod: F),
                Unit("conferencia", Faction.Policias, UnitSubtype.Productora, d: 3, s: 2, prod: S),
                Action("partida", Faction.Policias, ActionCategory.Boost, s: 1, effects: new[] { ModRes(SELF, D, 7) }),
                Action("licitacion", Faction.Policias, ActionCategory.Boost, d: 3, effects: new[] { ModRes(SELF, F, 8) }),
                Action("cadena_nacional", Faction.Policias, ActionCategory.Boost, f: 2, effects: new[] { ModRes(SELF, S, 4) }),
                Action("embargo", Faction.Policias, ActionCategory.Sabotaje, f: 3, effects: new[] { ModRes(OPP, D, -7) }),
                Action("detencion", Faction.Policias, ActionCategory.Sabotaje, d: 1, effects: new[] { ModRes(OPP, F, -3) }),
                Action("censura", Faction.Policias, ActionCategory.Sabotaje, s: 2, effects: new[] { ModRes(OPP, S, -5) }),
                Action("infiltrado", Faction.Policias, ActionCategory.Sabotaje, d: 3, s: 1, effects: new[] { RemoveUnit(OPP) }),
                Action("operativo", Faction.Policias, ActionCategory.Ataque, d: 4, f: 2, effects: new[] { ModHP(OPP, -18) }),
                Action("balas_goma", Faction.Policias, ActionCategory.Defensa, d: 2, s: 3, effects: new[] { ModHP(SELF, 12) }),
                Action("toque_queda", Faction.Policias, ActionCategory.EfectoEspecial, d: 4, f: 1, effects: new[] { ApplyStatus(OPP, Skip()) }),
                Action("decreto", Faction.Policias, ActionCategory.EfectoEspecial, d: 3, effects: new[] { ApplyStatus(SELF, Double()) }),
            };

            return (manifestantes, policias);
        }

        // ── Builders ──────────────────────────────────────────────────────────

        public static CardData Unit(string id, Faction faction, UnitSubtype sub,
                                    int d = 0, int f = 0, int s = 0, ResourceType prod = ResourceType.Dinero)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.id = id;
            card.cardName = id;
            card.faction = faction;
            card.cardType = CardType.Unidad;
            card.unitSubtype = sub;
            card.productionResource = prod;
            card.costDinero = d; card.costFuerza = f; card.costSocial = s;
            return card;
        }

        public static CardData Action(string id, Faction faction, ActionCategory cat,
                                      int d = 0, int f = 0, int s = 0, CardEffect[] effects = null)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.id = id;
            card.cardName = id;
            card.faction = faction;
            card.cardType = CardType.Accion;
            card.actionCategory = cat;
            card.costDinero = d; card.costFuerza = f; card.costSocial = s;
            card.effects = new List<CardEffect>(effects ?? new CardEffect[0]);
            return card;
        }

        public static CardEffect ModHP(TargetType t, int v) => new CardEffect(CardEffectType.ModifyHP, t, value: v);
        public static CardEffect ModRes(TargetType t, ResourceType r, int v) => new CardEffect(CardEffectType.ModifyResource, t, r, v);
        public static CardEffect RemoveUnit(TargetType t, int v = -1) => new CardEffect(CardEffectType.RemoveUnit, t, value: v);
        public static CardEffect ApplyStatus(TargetType t, StatusEffect s) => new CardEffect(CardEffectType.ApplyStatus, t, status: s);

        public static StatusEffect Skip() => new StatusEffect(StatusType.SkipProduction, 0, 1);
        public static StatusEffect Double() => new StatusEffect(StatusType.DoubleProduction, 2, 1);
    }
}
