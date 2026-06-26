using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de la capa de run (spec §17): topología del mapa (distancia=dificultad), avance por
    /// puntos a elección (una sola pasada), inyección del mazo de la run en el motor, recompensa
    /// 1-de-N que persiste en el mazo, handicap de IA por distancia y fin por derrota (permadeath).
    /// Core puro: no abren escena.
    /// </summary>
    public class RunTests
    {
        // ── Infraestructura ──────────────────────────────────────────────────

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
                foreach (CardData c in Pool(faction))
                {
                    int copies = c.drawWeight > 0 ? c.drawWeight : 1;
                    for (int i = 0; i < copies; i++) deck.Add(c);
                }
                return deck;
            }

            public IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction) =>
                faction == Faction.Manifestantes ? startManif : startPol;
        }

        /// <summary>RNG determinista: Next siempre 0, Choice toma el primero.</summary>
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

        /// <summary>Catálogo de prueba: 8 cartas distintas por facción + 1 unidad inicial por facción.</summary>
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

        private static RunManager NewRun(out RunCatalog cat, Faction human = Faction.Manifestantes,
                                         RunConfig runCfg = null)
        {
            cat = NewCatalog();
            return new RunManager(cat, new GameConfig(), new ZeroRng(), human,
                                  RunMapLibrary.BuildDefaultMap(), runCfg);
        }

        private static GameOutcome HumanWin(RunManager rm) =>
            GameOutcome.Win(rm.State.faction, WinCondition.KO, 10);

        private static GameOutcome AiWin(RunManager rm) =>
            GameOutcome.Win(rm.AiFaction, WinCondition.KO, 10);

        // ── Mapa: distancia = dificultad (BFS) ─────────────────────────────────

        [Test]
        public void Map_Distance_IsHopsFromStart()
        {
            RunMap map = RunMapLibrary.BuildDefaultMap();
            Assert.AreEqual(0, map.DistanceOf(RunMapLibrary.StartId), "el inicio está a distancia 0");
            Assert.AreEqual(1, map.DistanceOf(1), "anillo 1");
            Assert.AreEqual(1, map.DistanceOf(2));
            Assert.AreEqual(2, map.DistanceOf(5), "anillo 2");
            Assert.AreEqual(3, map.DistanceOf(RunMapLibrary.BossId), "el jefe es el más lejano");
        }

        [Test]
        public void Map_StartHasSuccessors_BossIsTerminal()
        {
            RunMap map = RunMapLibrary.BuildDefaultMap();
            Assert.AreEqual(3, map.Successors(RunMapLibrary.StartId).Count, "el inicio bifurca a 3 puntos");
            Assert.AreEqual(0, map.Successors(RunMapLibrary.BossId).Count, "el jefe no tiene salida");
            Assert.AreEqual(RunMapLibrary.BossId, map.BossNodeId);
            Assert.AreEqual(MapNodeType.Boss, map.NodeById(RunMapLibrary.BossId).type);
        }

        [Test]
        public void Map_RejectsDanglingEdge_DuplicateId_MissingStart()
        {
            var dangling = new List<MapNode> { new MapNode(0, MapNodeType.Start, "s").ConnectTo(99) };
            Assert.Throws<ArgumentException>(() => new RunMap(dangling, 0), "arista a nodo inexistente");

            var dup = new List<MapNode>
            {
                new MapNode(0, MapNodeType.Start, "s"),
                new MapNode(0, MapNodeType.Combat, "c"),
            };
            Assert.Throws<ArgumentException>(() => new RunMap(dup, 0), "id duplicado");

            var noStart = new List<MapNode> { new MapNode(0, MapNodeType.Start, "s") };
            Assert.Throws<ArgumentException>(() => new RunMap(noStart, 42), "startNodeId inexistente");
        }

        // ── Avance por puntos: sólo sucesores, una sola pasada ─────────────────

        [Test]
        public void Available_AreSuccessorsOfCurrent()
        {
            RunManager rm = NewRun(out _);
            IReadOnlyList<MapNode> avail = rm.AvailableNodes();
            var ids = new List<int>();
            foreach (MapNode n in avail) ids.Add(n.id);
            ids.Sort();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, ids, "desde el inicio: los 3 del anillo 1");
            Assert.IsTrue(rm.IsAvailable(2));
            Assert.IsFalse(rm.IsAvailable(5), "un punto del anillo 2 no es alcanzable todavía");
            Assert.IsFalse(rm.IsAvailable(RunMapLibrary.BossId));
        }

        [Test]
        public void WinningCombat_AdvancesAndSkipsSiblings_OnePass()
        {
            RunManager rm = NewRun(out _);
            rm.BeginCombat(2, firstIndex: 0);
            RewardOffer reward = rm.ResolveCombat(HumanWin(rm));

            Assert.AreEqual(2, rm.State.currentNodeId, "avanzó al punto ganado");
            Assert.IsTrue(rm.State.IsCleared(2));
            Assert.IsTrue(reward.HasReward, "tras ganar un combate normal se ofrece recompensa");

            // Con recompensa sin resolver, no se puede navegar.
            Assert.AreEqual(0, rm.AvailableNodes().Count);
            rm.SkipReward();

            // Ahora sólo los sucesores de 2; los hermanos 1 y 3 (y todo lo de atrás) quedaron salteados.
            Assert.IsTrue(rm.IsAvailable(4));
            Assert.IsTrue(rm.IsAvailable(5));
            Assert.IsTrue(rm.IsAvailable(6));
            Assert.IsFalse(rm.IsAvailable(1), "una sola pasada: no se vuelve a los hermanos");
            Assert.IsFalse(rm.IsAvailable(3));
        }

        [Test]
        public void BeginCombat_RejectsUnreachableNode()
        {
            RunManager rm = NewRun(out _);
            Assert.Throws<InvalidOperationException>(() => rm.BeginCombat(RunMapLibrary.BossId),
                "no se puede saltar directo al jefe");
        }

        // ── Mazo: starter, persistencia, recompensa ────────────────────────────

        [Test]
        public void StarterDeck_IsFactionDefault()
        {
            RunManager rm = NewRun(out RunCatalog cat);
            Assert.AreEqual(cat.GetDeckList(Faction.Manifestantes).Count, rm.State.deck.Count);
        }

        [Test]
        public void Reward_OffersDistinctCards_AndChosenJoinsDeck()
        {
            RunManager rm = NewRun(out _);
            int before = rm.State.deck.Count;

            rm.BeginCombat(1, firstIndex: 0);
            RewardOffer reward = rm.ResolveCombat(HumanWin(rm));

            Assert.AreEqual(3, reward.cards.Count, "1-de-3 por default");
            // Distintas entre sí.
            CollectionAssert.AllItemsAreUnique(reward.cards);

            CardData chosen = reward.cards[1];
            rm.ChooseReward(chosen);

            Assert.AreEqual(before + 1, rm.State.deck.Count, "la elegida suma al mazo");
            Assert.IsTrue(rm.State.deck.Contains(chosen));
        }

        [Test]
        public void RunDeck_IsInjectedIntoCombat()
        {
            RunManager rm = NewRun(out _);

            // Ganamos y sumamos una recompensa para que el mazo de la run difiera del starter.
            rm.BeginCombat(1, firstIndex: 0);
            RewardOffer reward = rm.ResolveCombat(HumanWin(rm));
            rm.ChooseReward(reward.cards[0]);
            int runDeckSize = rm.State.deck.Count;

            // El próximo combate debe robar del mazo de la run (deck + mano del humano == mazo de run).
            GameEngine engine = rm.BeginCombat(4, firstIndex: 0);
            PlayerState human = engine.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(runDeckSize, human.deck.Count + human.hand.Count,
                "el combate roba del mazo de la run inyectado, no del default de la facción");
        }

        // ── Handicap de IA por distancia (spec §17.1) ──────────────────────────

        [Test]
        public void AiHandicap_ScalesWithDistance_HumanUnaffected()
        {
            var cfg = new GameConfig();
            RunManager rm = NewRun(out _, Faction.Manifestantes);
            int baseRes = cfg.initialDinero;   // 5 por default

            // d1: IA +2 a cada recurso; el humano sin bonus.
            GameEngine d1 = rm.BeginCombat(2, firstIndex: 0);
            PlayerState ai1 = d1.PlayerAt(rm.AiIndex);
            PlayerState hu1 = d1.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(baseRes + 2, ai1.dinero, "d1 → IA +2 $");
            Assert.AreEqual(baseRes + 2, ai1.fuerza);
            Assert.AreEqual(baseRes + 2, ai1.social);
            Assert.AreEqual(baseRes, hu1.dinero, "el humano no recibe handicap");
            rm.ResolveCombat(HumanWin(rm));
            rm.SkipReward();

            // d2: IA +4.
            GameEngine d2 = rm.BeginCombat(5, firstIndex: 0);
            Assert.AreEqual(baseRes + 4, d2.PlayerAt(rm.AiIndex).dinero, "d2 → IA +4 $");
            rm.ResolveCombat(HumanWin(rm));
            rm.SkipReward();

            // d3 (jefe): IA +6 y una unidad inicial extra.
            GameEngine boss = rm.BeginCombat(RunMapLibrary.BossId, firstIndex: 0);
            PlayerState aiBoss = boss.PlayerAt(rm.AiIndex);
            Assert.AreEqual(baseRes + 6, aiBoss.dinero, "jefe → IA +6 $");
            Assert.AreEqual(2, aiBoss.AliveUnitCount(), "jefe → IA con 1 unidad inicial extra (1 base + 1)");
        }

        // ── Desenlaces ─────────────────────────────────────────────────────────

        [Test]
        public void Defeat_EndsRun_Permadeath()
        {
            RunManager rm = NewRun(out _);
            rm.BeginCombat(1, firstIndex: 0);
            RewardOffer reward = rm.ResolveCombat(AiWin(rm));

            Assert.AreEqual(RunStatus.Lost, rm.State.status);
            Assert.IsFalse(reward.HasReward);
            Assert.AreEqual(0, rm.AvailableNodes().Count, "run terminada: no hay a dónde ir");
            Assert.Throws<InvalidOperationException>(() => rm.BeginCombat(2), "no se sigue tras perder");
        }

        [Test]
        public void Draw_EndsRunAsLost()
        {
            RunManager rm = NewRun(out _);
            rm.BeginCombat(1, firstIndex: 0);
            rm.ResolveCombat(GameOutcome.Draw(20));
            Assert.AreEqual(RunStatus.Lost, rm.State.status, "el empate no es victoria: corta la run");
        }

        [Test]
        public void BeatingBoss_WinsRun_NoReward()
        {
            RunManager rm = NewRun(out _);

            // Ruta al jefe: inicio → 2 (d1) → 5 (d2) → jefe (d3).
            rm.BeginCombat(2, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();
            rm.BeginCombat(5, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();

            rm.BeginCombat(RunMapLibrary.BossId, firstIndex: 0);
            RewardOffer reward = rm.ResolveCombat(HumanWin(rm));

            Assert.AreEqual(RunStatus.Won, rm.State.status, "vencer al jefe gana la run");
            Assert.IsFalse(reward.HasReward, "el jefe no da recompensa: la run terminó");
            Assert.AreEqual(0, rm.AvailableNodes().Count);
        }
    }
}
