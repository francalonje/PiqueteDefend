using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests de reliquias (spec §17.4/§17.6): se traducen a bonos del <see cref="PlayerSetup"/> del
    /// humano al iniciar el combate (recursos, unidad extra, estado sembrado), sin tocar el motor; el
    /// humano sin reliquias es idéntico a la base (regresión). Core puro.
    /// </summary>
    public class RelicTests
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

        private static RelicData Relic(RelicEffectKind kind)
        {
            var r = ScriptableObject.CreateInstance<RelicData>();
            r.kind = kind;
            return r;
        }

        private static RunManager NewRun() =>
            new RunManager(NewCatalog(), new GameConfig(), new ZeroRng(),
                           Faction.Manifestantes, RunMapLibrary.BuildDefaultMap());

        [Test]
        public void Relic_BonusResource_AppliesToHuman()
        {
            RunManager rm = NewRun();
            RelicData r = Relic(RelicEffectKind.BonusResource);
            r.resource = ResourceType.Dinero; r.value = 3;
            rm.State.relics.Add(r);

            GameEngine engine = rm.BeginCombat(1, firstIndex: 0);   // d1: el handicap por distancia es de la IA
            PlayerState human = engine.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(new GameConfig().initialDinero + 3, human.dinero, "la reliquia suma al recurso del humano");
        }

        [Test]
        public void Relic_ExtraStartingUnit_DeploysForHuman()
        {
            RunManager rm = NewRun();
            RelicData r = Relic(RelicEffectKind.ExtraStartingUnit);
            r.unit = Unit("guardaespaldas", Faction.Manifestantes, hp: 12);
            rm.State.relics.Add(r);

            GameEngine engine = rm.BeginCombat(1, firstIndex: 0);
            PlayerState human = engine.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(2, human.AliveUnitCount(), "1 inicial del catálogo + 1 de la reliquia");
            int withRelicHp = 0;
            foreach (UnitSlot s in human.unitSlots) if (s != null && s.MaxHp == 12) withRelicHp++;
            Assert.AreEqual(1, withRelicHp);
        }

        [Test]
        public void Relic_InitialStatus_SeededForHuman()
        {
            RunManager rm = NewRun();
            RelicData r = Relic(RelicEffectKind.InitialStatus);
            r.status = new StatusEffect(StatusType.DoubleProduction, 2, 1);
            rm.State.relics.Add(r);

            GameEngine engine = rm.BeginCombat(1, firstIndex: 0);
            PlayerState human = engine.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(1, human.activeStatuses.Count, "el estado de la reliquia se siembra en el humano");
            Assert.AreEqual(StatusType.DoubleProduction, human.activeStatuses[0].statusType);
        }

        [Test]
        public void NoRelics_HumanUnchanged_RegressionBaseline()
        {
            RunManager rm = NewRun();
            GameEngine engine = rm.BeginCombat(1, firstIndex: 0);
            PlayerState human = engine.PlayerAt(rm.HumanIndex);
            Assert.AreEqual(new GameConfig().initialDinero, human.dinero, "sin reliquias, recursos base");
            Assert.AreEqual(1, human.AliveUnitCount(), "sin reliquias, sólo la unidad inicial del catálogo");
            Assert.IsEmpty(human.activeStatuses);
        }
    }
}
