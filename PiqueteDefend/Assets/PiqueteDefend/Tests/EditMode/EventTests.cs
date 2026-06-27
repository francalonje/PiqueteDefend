using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de los eventos (spec §17.6): abrir bloquea la navegación; resolver una opción aplica sus
    /// resultados (oro / carta / reliquia) y avanza. Core puro.
    /// </summary>
    public class EventTests
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

        /// <summary>Start(0) → Event(1) → Boss(2).</summary>
        private static RunMap EventMap()
        {
            var nodes = new List<MapNode>
            {
                new MapNode(0, MapNodeType.Start, "i").ConnectTo(1),
                new MapNode(1, MapNodeType.Event, "evento").ConnectTo(2),
                new MapNode(2, MapNodeType.Boss, "jefe"),
            };
            return new RunMap(nodes, 0);
        }

        /// <summary>Evento de prueba con las tres clases de resultado, en orden conocido.</summary>
        private static List<EventDefinition> TestEvents() => new List<EventDefinition>
        {
            new EventDefinition("t", "Test", "cuerpo",
                new EventChoice("oro", new EventOutcome(EventOutcome.Kind.Gold, 30)),
                new EventChoice("carta", new EventOutcome(EventOutcome.Kind.AddRandomCard)),
                new EventChoice("reliquia", new EventOutcome(EventOutcome.Kind.Relic))),
        };

        private static RunManager NewRun()
        {
            RunCatalog cat = NewCatalog();
            List<RelicData> relics = RelicLibrary.BuildPool(cat, Faction.Manifestantes);
            return new RunManager(cat, new GameConfig(), new ZeroRng(), Faction.Manifestantes,
                                  EventMap(), null, null, relics, TestEvents());
        }

        [Test]
        public void EventLibrary_BuildsPool_NonEmpty()
        {
            Assert.Greater(EventLibrary.BuildActo1Pool().Count, 0);
        }

        [Test]
        public void EnterEvent_OpensAndBlocksNavigation()
        {
            RunManager rm = NewRun();
            EventDefinition ev = rm.EnterEvent(1);

            Assert.IsTrue(rm.EventInProgress);
            Assert.AreSame(ev, rm.CurrentEvent);
            Assert.Greater(ev.choices.Count, 0);
            Assert.AreEqual(0, rm.AvailableNodes().Count, "el evento abierto bloquea la navegación");
        }

        [Test]
        public void ResolveEvent_Gold_AddsGold_AndAdvances()
        {
            RunManager rm = NewRun();
            rm.EnterEvent(1);
            rm.ResolveEvent(0);   // opción "oro" (+30)

            Assert.AreEqual(30, rm.State.gold);
            Assert.IsFalse(rm.EventInProgress);
            Assert.AreEqual(1, rm.State.currentNodeId, "avanzó al nodo del evento");
            Assert.IsTrue(rm.IsAvailable(2));
        }

        [Test]
        public void ResolveEvent_AddCard_GrowsDeck()
        {
            RunManager rm = NewRun();
            int before = rm.State.deck.Count;
            rm.EnterEvent(1);
            rm.ResolveEvent(1);   // opción "carta"

            Assert.AreEqual(before + 1, rm.State.deck.Count);
        }

        [Test]
        public void ResolveEvent_Relic_AddsRelic()
        {
            RunManager rm = NewRun();
            rm.EnterEvent(1);
            rm.ResolveEvent(2);   // opción "reliquia"

            Assert.AreEqual(1, rm.State.relics.Count);
        }

        [Test]
        public void ResolveEvent_InvalidChoice_Throws()
        {
            RunManager rm = NewRun();
            rm.EnterEvent(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rm.ResolveEvent(99));
        }
    }
}
