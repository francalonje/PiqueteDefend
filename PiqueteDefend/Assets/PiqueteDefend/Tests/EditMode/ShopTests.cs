using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de la tienda (spec §17.6): genera stock (cartas + reliquias) con oro, comprar descuenta y
    /// suma al mazo/reliquias, la remoción respeta el mínimo de mazo, y salir avanza. Core puro.
    /// </summary>
    public class ShopTests
    {
        private sealed class RunCatalog : ICardCatalog
        {
            public readonly List<CardData> manif = new List<CardData>();
            public readonly List<CardData> pol = new List<CardData>();
            public readonly List<UnitCardData> startManif = new List<UnitCardData>();
            public readonly List<UnitCardData> startPol = new List<UnitCardData>();
            private List<CardData> Pool(Faction f) => f == Faction.Manifestantes ? manif : pol;
            public IReadOnlyList<CardData> GetPool(Faction faction) => Pool(faction);
            public IReadOnlyList<CardData> GetDeckList(Faction faction)
            {
                var deck = new List<CardData>();
                foreach (CardData c in Pool(faction)) deck.Add(c);
                return deck;
            }
            public IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction) =>
                faction == Faction.Manifestantes ? startManif : startPol;
        }

        private sealed class ZeroRng : IRandomProvider
        {
            public int Next(int maxExclusive) => 0;
            public T Choice<T>(IReadOnlyList<T> list) => list[0];
        }

        private static UnitCardData Unit(string id, Faction f)
        {
            var c = ScriptableObject.CreateInstance<UnitCardData>();
            c.id = id; c.cardName = id; c.faction = f; c.maxHp = 5;
            c.allowedSlots = Array.Empty<int>();
            c.attack = new UnitAttack(TargetMode.Frontmost, 1, 1);
            c.passiveEffects = new List<PassiveEffect>();
            c.drawWeight = 1;
            return c;
        }

        private static RunCatalog NewCatalog()
        {
            var cat = new RunCatalog();
            for (int i = 0; i < 8; i++)
            {
                cat.manif.Add(Unit($"m{i}", Faction.Manifestantes));
                cat.pol.Add(Unit($"p{i}", Faction.Policias));
            }
            cat.startManif.Add(Unit("mStart", Faction.Manifestantes));
            cat.startPol.Add(Unit("pStart", Faction.Policias));
            return cat;
        }

        /// <summary>Start(0) → Shop(1) → Boss(2).</summary>
        private static RunMap ShopMap()
        {
            var nodes = new List<MapNode>
            {
                new MapNode(0, MapNodeType.Start, "i").ConnectTo(1),
                new MapNode(1, MapNodeType.Shop, "tienda").ConnectTo(2),
                new MapNode(2, MapNodeType.Boss, "jefe"),
            };
            return new RunMap(nodes, 0);
        }

        private static RunManager NewRun(out RunConfig cfg)
        {
            cfg = new RunConfig();
            RunCatalog cat = NewCatalog();
            List<RelicData> relics = RelicLibrary.BuildPool(cat, Faction.Manifestantes);
            return new RunManager(cat, new GameConfig(), new ZeroRng(), Faction.Manifestantes,
                                  ShopMap(), cfg, null, relics);
        }

        [Test]
        public void EnterShop_GeneratesStock_AndBlocksNavigation()
        {
            RunManager rm = NewRun(out RunConfig cfg);
            rm.EnterShop(1);

            Assert.IsTrue(rm.ShopInProgress);
            Assert.AreEqual(cfg.shopCardCount, rm.CurrentShop.cards.Count, "stock de cartas");
            Assert.AreEqual(cfg.shopRelicCount, rm.CurrentShop.relics.Count, "stock de reliquias");
            Assert.AreEqual(cfg.shopCardPrice, rm.CurrentShop.cardPrice);
            Assert.AreEqual(0, rm.AvailableNodes().Count, "la tienda abierta bloquea la navegación");
        }

        [Test]
        public void BuyCard_AddsToDeck_DeductsGold_FailsIfBroke()
        {
            RunManager rm = NewRun(out RunConfig cfg);
            rm.State.gold = 100;
            rm.EnterShop(1);
            int deckBefore = rm.State.deck.Count;
            CardData card = rm.CurrentShop.cards[0];

            rm.BuyCard(card);
            Assert.AreEqual(deckBefore + 1, rm.State.deck.Count, "la carta entra al mazo");
            Assert.AreEqual(100 - cfg.shopCardPrice, rm.State.gold, "descuenta el precio");
            Assert.IsFalse(rm.CurrentShop.cards.Contains(card), "sale del stock");

            rm.State.gold = 0;
            Assert.Throws<InvalidOperationException>(() => rm.BuyCard(rm.CurrentShop.cards[0]), "sin oro no compra");
        }

        [Test]
        public void BuyRelic_AddsToRelics_DeductsGold()
        {
            RunManager rm = NewRun(out RunConfig cfg);
            rm.State.gold = 100;
            rm.EnterShop(1);
            RelicData relic = rm.CurrentShop.relics[0];

            rm.BuyRelic(relic);
            Assert.IsTrue(rm.State.relics.Contains(relic), "la reliquia se suma a la run");
            Assert.AreEqual(100 - cfg.shopRelicPrice, rm.State.gold);
            Assert.IsFalse(rm.CurrentShop.relics.Contains(relic));
        }

        [Test]
        public void BuyRemoval_RemovesFromDeck_RespectsMinDeck()
        {
            RunManager rm = NewRun(out RunConfig cfg);
            rm.State.gold = 100;
            rm.EnterShop(1);
            int deckBefore = rm.State.deck.Count;
            CardData card = rm.State.deck[0];

            rm.BuyRemoval(card);
            Assert.AreEqual(deckBefore - 1, rm.State.deck.Count, "saca la carta del mazo");
            Assert.AreEqual(100 - cfg.shopRemovalPrice, rm.State.gold);

            // Bajar el mazo al mínimo y verificar que no deja seguir quitando.
            var big = new RunManager(NewCatalog(), new GameConfig(), new ZeroRng(), Faction.Manifestantes,
                                     ShopMap(), new RunConfig { minDeckSize = 100 }, null, null);
            big.State.gold = 100;
            big.EnterShop(1);
            Assert.Throws<InvalidOperationException>(() => big.BuyRemoval(big.State.deck[0]), "no por debajo del mínimo");
        }

        [Test]
        public void LeaveShop_Advances()
        {
            RunManager rm = NewRun(out _);
            rm.EnterShop(1);
            rm.LeaveShop();

            Assert.IsFalse(rm.ShopInProgress);
            Assert.AreEqual(1, rm.State.currentNodeId, "avanzó al nodo de la tienda");
            Assert.IsTrue(rm.IsAvailable(2), "ahora el sucesor de la tienda");
        }
    }
}
