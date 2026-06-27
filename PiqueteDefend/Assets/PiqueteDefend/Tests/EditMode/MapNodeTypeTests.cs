using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de los tipos de nodo no-combate y el avance compartido (spec §17.6): tesoro otorga oro y
    /// avanza por el mismo camino (<c>AdvanceTo</c>) que un combate ganado; la élite paga más oro que un
    /// combate normal; los guardas rechazan nodos del tipo equivocado o inalcanzables. Core puro.
    /// </summary>
    public class MapNodeTypeTests
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

        /// <summary>Mapa de prueba con tipos de nodo variados:
        /// Start(0) → Combat(1), Elite(2), Treasure(3), Workshop(5); cada uno → Boss(4).</summary>
        private static RunMap TestMap()
        {
            var nodes = new List<MapNode>
            {
                new MapNode(0, MapNodeType.Start, "inicio").ConnectTo(1, 2, 3, 5),
                new MapNode(1, MapNodeType.Combat, "combate").ConnectTo(4),
                new MapNode(2, MapNodeType.Elite, "elite").ConnectTo(4),
                new MapNode(3, MapNodeType.Treasure, "tesoro").ConnectTo(4),
                new MapNode(5, MapNodeType.Workshop, "taller").ConnectTo(4),
                new MapNode(4, MapNodeType.Boss, "jefe"),
            };
            return new RunMap(nodes, 0);
        }

        private static RunManager NewRun(out RunConfig cfg)
        {
            cfg = new RunConfig();
            return new RunManager(NewCatalog(), new GameConfig(), new ZeroRng(),
                                  Faction.Manifestantes, TestMap(), cfg);
        }

        private static GameOutcome HumanWin(RunManager rm) =>
            GameOutcome.Win(rm.State.faction, WinCondition.KO, 10);

        // ── Tesoro ───────────────────────────────────────────────────────────────

        [Test]
        public void Treasure_GrantsGold_AndAdvances()
        {
            RunManager rm = NewRun(out RunConfig cfg);
            rm.EnterTreasure(3);

            Assert.AreEqual(cfg.treasureGoldReward, rm.State.gold, "el tesoro otorga oro");
            Assert.AreEqual(3, rm.State.currentNodeId, "avanza al nodo del tesoro");
            Assert.IsTrue(rm.State.IsCleared(3));
            Assert.IsTrue(rm.IsAvailable(4), "ahora sólo el sucesor del tesoro");
            Assert.IsFalse(rm.IsAvailable(1), "una sola pasada: los hermanos quedan atrás");
        }

        [Test]
        public void Treasure_RejectsWrongTypeAndUnreachable()
        {
            RunManager rm = NewRun(out _);
            Assert.Throws<InvalidOperationException>(() => rm.EnterTreasure(1), "1 es combate, no tesoro");
            Assert.Throws<InvalidOperationException>(() => rm.EnterTreasure(4), "el jefe no es alcanzable aún");
        }

        // ── Élite vs combate: oro ──────────────────────────────────────────────────

        [Test]
        public void Elite_PaysMoreGoldThanCombat()
        {
            RunManager elite = NewRun(out RunConfig cfg);
            elite.BeginCombat(2, firstIndex: 0);
            elite.ResolveCombat(HumanWin(elite));
            Assert.AreEqual(cfg.eliteGoldReward, elite.State.gold, "la élite paga el oro de élite");

            RunManager combat = NewRun(out RunConfig cfg2);
            combat.BeginCombat(1, firstIndex: 0);
            combat.ResolveCombat(HumanWin(combat));
            Assert.AreEqual(cfg2.combatGoldReward, combat.State.gold, "el combate normal paga menos");

            Assert.Greater(cfg.eliteGoldReward, cfg2.combatGoldReward);
        }

        [Test]
        public void Elite_IsACombatNode_BeginCombatAccepts()
        {
            RunManager rm = NewRun(out _);
            Assert.DoesNotThrow(() => rm.BeginCombat(2, firstIndex: 0), "la élite se entra como combate");
            Assert.IsTrue(rm.CombatInProgress);
        }

        // ── Taller de mazo (remoción) ──────────────────────────────────────────────

        [Test]
        public void Workshop_RemovesCard_AndAdvances()
        {
            RunManager rm = NewRun(out _);
            int before = rm.State.deck.Count;
            CardData toRemove = rm.State.deck[0];

            rm.EnterWorkshop(5);
            Assert.IsTrue(rm.WorkshopInProgress);
            Assert.AreEqual(0, rm.AvailableNodes().Count, "el taller abierto bloquea la navegación");

            rm.RemoveCardAndLeave(toRemove);

            Assert.IsFalse(rm.WorkshopInProgress, "quitar cierra el taller");
            Assert.AreEqual(before - 1, rm.State.deck.Count, "el mazo perdió una carta");
            Assert.AreEqual(5, rm.State.currentNodeId, "avanzó al nodo del taller");
            Assert.IsTrue(rm.IsAvailable(4), "ahora el sucesor del taller");
        }

        [Test]
        public void Workshop_LeaveWithoutRemoving_Advances()
        {
            RunManager rm = NewRun(out _);
            int before = rm.State.deck.Count;
            rm.EnterWorkshop(5);
            rm.LeaveWorkshop();

            Assert.IsFalse(rm.WorkshopInProgress);
            Assert.AreEqual(before, rm.State.deck.Count, "salir sin tocar no cambia el mazo");
            Assert.AreEqual(5, rm.State.currentNodeId);
        }

        [Test]
        public void Workshop_RespectsMinDeckSize()
        {
            var rm = new RunManager(NewCatalog(), new GameConfig(), new ZeroRng(),
                                    Faction.Manifestantes, TestMap(), new RunConfig { minDeckSize = 100 });
            CardData any = rm.State.deck[0];
            rm.EnterWorkshop(5);
            Assert.IsFalse(rm.CanRemoveCard, "con el mazo en el mínimo no se puede quitar");
            Assert.Throws<InvalidOperationException>(() => rm.RemoveCardAndLeave(any));
        }

        // ── Acto 1 (Línea A del subte) ─────────────────────────────────────────────

        [Test]
        public void Acto1_HasVariedNodeTypes_AndReachableBoss()
        {
            RunMap map = RunMapLibrary.BuildActo1();

            Assert.AreEqual(MapNodeType.Start, map.StartNode.type);
            Assert.Greater(map.Successors(map.startNodeId).Count, 1, "el inicio bifurca (elección de ruta)");
            Assert.AreEqual(RunMapLibrary.Acto1BossId, map.BossNodeId, "la cabecera es el jefe");
            Assert.AreEqual(0, map.Successors(RunMapLibrary.Acto1BossId).Count, "el jefe es terminal");

            // Hay variedad: al menos un tesoro y una élite además de combates.
            bool hasTreasure = false, hasElite = false, hasCombat = false;
            foreach (MapNode n in map.Nodes)
            {
                if (n.type == MapNodeType.Treasure) hasTreasure = true;
                else if (n.type == MapNodeType.Elite) hasElite = true;
                else if (n.type == MapNodeType.Combat) hasCombat = true;
            }
            Assert.IsTrue(hasTreasure && hasElite && hasCombat, "el acto mezcla combate, élite y tesoro");

            // El jefe es alcanzable (distancia válida vía BFS).
            Assert.Greater(map.DistanceOf(RunMapLibrary.Acto1BossId), 0, "el jefe es alcanzable desde el inicio");
        }
    }
}
