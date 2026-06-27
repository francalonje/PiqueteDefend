using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de los arquetipos de enemigo curados (spec §17.6): la IA de un combate se arma desde un
    /// <see cref="EncounterDefinition"/> del pool (mazo/unidades/handicap/líder de jefe), sin repetir
    /// dentro de la run, y otorga oro al ganar. Sin pool, la run cae al comportamiento default (probado
    /// en <see cref="RunTests"/>). Core puro: no abren escena.
    /// </summary>
    public class EncounterTests
    {
        // ── Infraestructura (espejo de RunTests) ───────────────────────────────

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

        private sealed class ZeroRng : IRandomProvider
        {
            public int Next(int maxExclusive) => 0;
            public T Choice<T>(IReadOnlyList<T> list) => list[0];
        }

        private static UnitCardData Unit(string id, Faction f, int hp = 5)
        {
            var c = ScriptableObject.CreateInstance<UnitCardData>();
            c.id = id; c.cardName = id; c.faction = f; c.maxHp = hp;
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

        private static EncounterDefinition Enc(string id, Faction f, int difficulty, bool boss = false)
        {
            var e = ScriptableObject.CreateInstance<EncounterDefinition>();
            e.id = id; e.title = id; e.faction = f; e.difficulty = difficulty; e.isBoss = boss;
            e.deck = new List<CardData>();
            e.startingUnits = new List<UnitCardData>();
            e.aiInitialStatuses = new List<StatusEffect>();
            e.playerInitialStatuses = new List<StatusEffect>();
            return e;
        }

        private static RunManager NewRun(RunCatalog cat, IReadOnlyList<EncounterDefinition> encounters,
                                         Faction human = Faction.Manifestantes) =>
            new RunManager(cat, new GameConfig(), new ZeroRng(), human,
                           RunMapLibrary.BuildDefaultMap(), null, encounters);

        private static GameOutcome HumanWin(RunManager rm) =>
            GameOutcome.Win(rm.State.faction, WinCondition.KO, 10);

        // ── La IA se arma desde el arquetipo ───────────────────────────────────

        [Test]
        public void Encounter_BuildsAi_FromArchetypeDeckUnitsAndBonus()
        {
            var cat = NewCatalog();
            EncounterDefinition enc = Enc("patota", Faction.Policias, difficulty: 1);
            for (int i = 0; i < 5; i++) enc.deck.Add(Unit($"e{i}", Faction.Policias));
            enc.startingUnits.Add(Unit("lider_patota", Faction.Policias, hp: 9));
            enc.bonusDinero = 3;

            RunManager rm = NewRun(cat, new[] { enc });
            GameEngine engine = rm.BeginCombat(2, firstIndex: 0);   // nodo d1
            PlayerState ai = engine.PlayerAt(rm.AiIndex);

            // Mazo del arquetipo inyectado (no el default de la facción).
            Assert.AreEqual(5, ai.deck.Count + ai.hand.Count, "el combate roba del mazo del arquetipo");
            // Unidad inicial del arquetipo desplegada (hp distintivo).
            int withLeaderHp = 0;
            foreach (UnitSlot s in ai.unitSlots) if (s != null && s.MaxHp == 9) withLeaderHp++;
            Assert.AreEqual(1, withLeaderHp, "se despliega la unidad inicial del arquetipo");
            // Handicap por distancia (d1 → +2) + bonus del arquetipo (+3) = +5.
            Assert.AreEqual(new GameConfig().initialDinero + 5, ai.dinero, "handicap + bonus del arquetipo");

            Assert.IsTrue(rm.State.usedEncounterIds.Contains("patota"), "el arquetipo queda marcado como usado");
        }

        [Test]
        public void Encounter_NotRepeated_WithinRun()
        {
            var cat = NewCatalog();
            var encs = new[]
            {
                Enc("a", Faction.Policias, difficulty: 1),
                Enc("b", Faction.Policias, difficulty: 1),
            };
            RunManager rm = NewRun(cat, encs);

            rm.BeginCombat(2, firstIndex: 0);                 // d1
            rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();
            rm.BeginCombat(5, firstIndex: 0);                 // d2

            Assert.AreEqual(2, rm.State.usedEncounterIds.Count,
                "dos combates → dos arquetipos distintos (sin repetir mientras haya pool)");
        }

        [Test]
        public void Boss_InjectsUniqueLeaderUnit()
        {
            var cat = NewCatalog();
            EncounterDefinition bossEnc = Enc("casa_rosada", Faction.Policias, difficulty: 3, boss: true);
            bossEnc.startingUnits.Add(Unit("guardia", Faction.Policias, hp: 5));
            bossEnc.leaderUnit = Unit("el_jefe", Faction.Policias, hp: 30);   // hp único = identificable

            RunManager rm = NewRun(cat, new[] { bossEnc });
            // Ruta al jefe: 2 (d1, fallback) → 5 (d2, fallback) → jefe.
            rm.BeginCombat(2, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();
            rm.BeginCombat(5, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();

            GameEngine boss = rm.BeginCombat(RunMapLibrary.BossId, firstIndex: 0);
            PlayerState ai = boss.PlayerAt(rm.AiIndex);

            int leaders = 0;
            foreach (UnitSlot s in ai.unitSlots) if (s != null && s.MaxHp == 30) leaders++;
            Assert.AreEqual(1, leaders, "el jefe despliega su unidad-líder única como extra");
            Assert.AreEqual(2, ai.AliveUnitCount(), "guardia base + líder");
        }

        [Test]
        public void Boss_PassiveSeedsStatusOnHuman()
        {
            var cat = NewCatalog();
            EncounterDefinition bossEnc = Enc("apriete", Faction.Policias, difficulty: 3, boss: true);
            bossEnc.playerInitialStatuses.Add(new StatusEffect(StatusType.Desmoralizar, 2, 3));

            RunManager rm = NewRun(cat, new[] { bossEnc });
            rm.BeginCombat(2, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();
            rm.BeginCombat(5, firstIndex: 0); rm.ResolveCombat(HumanWin(rm)); rm.SkipReward();

            GameEngine boss = rm.BeginCombat(RunMapLibrary.BossId, firstIndex: 0);
            PlayerState human = boss.PlayerAt(rm.HumanIndex);

            int demoralized = 0;
            foreach (UnitSlot s in human.unitSlots)
                if (s != null && s.StatusValue(StatusType.Desmoralizar) == 2) demoralized++;
            Assert.Greater(demoralized, 0, "la pasiva de jefe siembra el estado en las unidades del humano");
        }

        // ── Oro ────────────────────────────────────────────────────────────────

        [Test]
        public void Gold_AwardedOnCombatWin()
        {
            var cat = NewCatalog();
            RunManager rm = NewRun(cat, null);   // sin pool: el oro no depende del arquetipo
            Assert.AreEqual(0, rm.State.gold, "arranca sin oro");

            rm.BeginCombat(1, firstIndex: 0);
            rm.ResolveCombat(HumanWin(rm));

            Assert.AreEqual(new RunConfig().combatGoldReward, rm.State.gold, "ganar un combate otorga oro");
        }
    }
}
