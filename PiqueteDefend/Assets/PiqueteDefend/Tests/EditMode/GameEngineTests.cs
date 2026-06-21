using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests del núcleo determinista (spec §6/§7). Validan la semántica implementada en
    /// <see cref="GameEngine"/> contra la del simulador de balance (`sim/`).
    ///
    /// Patrón: el motor declara la partida terminada apenas un lado se queda sin unidades, así que
    /// los tableros se arman ANTES de <see cref="GameEngine.BeginTurn"/>, y los escenarios de un solo
    /// lado agregan una unidad "keepalive" en el otro para que la partida no termine al arrancar.
    /// </summary>
    public class GameEngineTests
    {
        // ── Infraestructura ──────────────────────────────────────────────────

        private sealed class TestCatalog : ICardCatalog
        {
            public readonly List<CardData> pool = new List<CardData>();
            public readonly List<UnitCardData> starting = new List<UnitCardData>();
            public IReadOnlyList<CardData> GetPool(Faction faction) => pool;
            public IReadOnlyList<UnitCardData> GetStartingUnits(Faction faction) => starting;
        }

        /// <summary>RNG determinista: Next siempre 0, Choice toma el primero.</summary>
        private sealed class ZeroRng : IRandomProvider
        {
            public int Next(int maxExclusive) => 0;
            public T Choice<T>(IReadOnlyList<T> list) => list[0];
        }

        private static UnitAttack Duel(int dmg) =>
            new UnitAttack(AttackReference.Relative, new[] { 0 }, 0, dmg);

        private static UnitCardData U(int hp, UnitAttack atk = null, int[] allowed = null,
                                     params PassiveEffect[] passives)
        {
            var c = ScriptableObject.CreateInstance<UnitCardData>();
            c.id = "u"; c.cardName = "U"; c.maxHp = hp;
            c.allowedSlots = allowed ?? Array.Empty<int>();
            c.attack = atk ?? Duel(0);
            c.passiveEffects = new List<PassiveEffect>(passives);
            return c;
        }

        private static PassiveEffect Aura(int v) => new PassiveEffect
        {
            passiveType = PassiveType.AuraDamage, value = v, target = PassiveTarget.Allies,
            reference = AttackReference.Relative, pattern = new[] { -1, 1 }
        };

        private static PassiveEffect Retaliate(int v) =>
            new PassiveEffect { passiveType = PassiveType.Retaliate, value = v, target = PassiveTarget.Self };

        private static GameConfig PlainCfg() => new GameConfig
        {
            firstNoAttackTurn1 = false, firstProducesTurn1 = false,
            suddenDeathStart = 999, maxTurns = 999,
            baseProdDinero = 0, baseProdFuerza = 0, baseProdSocial = 0
        };

        private static GameEngine NewEngine(GameConfig cfg, out TestCatalog cat, params UnitCardData[] starting)
        {
            cat = new TestCatalog();
            cat.pool.Add(U(1));                  // carta dummy para poblar la mano
            cat.starting.AddRange(starting);
            return new GameEngine(cfg, new ZeroRng(), cat);
        }

        /// <summary>Arranca con tableros vacíos en AwaitingTurnStart; el test coloca unidades y llama BeginTurn.</summary>
        private static GameEngine Combat(out TestCatalog cat)
        {
            var e = NewEngine(PlainCfg(), out cat);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            return e;
        }

        // ── Targeting ────────────────────────────────────────────────────────

        [Test]
        public void ResolveSlots_Relative_OffsetsFromOrigin()
        {
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 },
                GameEngine.ResolveSlots(AttackReference.Relative, new[] { -1, 0, 1 }, 2));
            CollectionAssert.AreEquivalent(new[] { 0, 1 },
                GameEngine.ResolveSlots(AttackReference.Relative, new[] { -1, 0, 1 }, 0));
        }

        [Test]
        public void ResolveSlots_Absolute_FixedSlots()
        {
            CollectionAssert.AreEquivalent(new[] { 3, 4, 5 },
                GameEngine.ResolveSlots(AttackReference.Absolute, new[] { 3, 4, 5 }, 99));
        }

        // ── Ataque ───────────────────────────────────────────────────────────

        [Test]
        public void Attack_Relative_HitsFacingSlot()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(4)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            e.BeginTurn();

            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
            Assert.AreEqual(6, e.PlayerAt(1).unitSlots[2].currentHp);
        }

        [Test]
        public void Attack_EmptySlot_Whiffs()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(4)));
            e.PlayerAt(1).unitSlots[5] = new UnitSlot(U(10));  // keepalive; slot enfrentado (2) vacío
            e.BeginTurn();

            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
            Assert.IsNull(e.PlayerAt(1).unitSlots[2]);
        }

        [Test]
        public void Attack_Kill_FreesSlot_AndWins()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(50)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            e.BeginTurn();

            e.AttackWithUnit(2);
            Assert.IsNull(e.PlayerAt(1).unitSlots[2]);
            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
            Assert.AreEqual(WinCondition.KO, e.Outcome.Value.Condition);
        }

        [Test]
        public void Attack_Pick_RequiresExactChoice()
        {
            var e = Combat(out _);
            var atk = new UnitAttack(AttackReference.Absolute, new[] { 0, 1, 2 }, 1, 5);
            e.PlayerAt(0).unitSlots[3] = new UnitSlot(U(10, atk, new[] { 3 }));
            e.PlayerAt(1).unitSlots[1] = new UnitSlot(U(10));
            e.BeginTurn();

            Assert.AreEqual(ActionResult.NeedsAttackTarget, e.AttackWithUnit(3));
            Assert.AreEqual(ActionResult.InvalidTarget, e.AttackWithUnit(3, new[] { 4 }));
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(3, new[] { 1 }));
            Assert.AreEqual(5, e.PlayerAt(1).unitSlots[1].currentHp);
        }

        // ── Daño efectivo ─────────────────────────────────────────────────────

        [Test]
        public void EffectiveDamage_AddsFuriaAuraEquip_SubtractsDesmoralizar()
        {
            var e = Combat(out _);  // cálculo puro: no requiere BeginTurn
            var attacker = new UnitSlot(U(10, Duel(5)));
            attacker.activeStatuses.Add(new StatusEffect(StatusType.Furia, 3, 2));
            attacker.activeStatuses.Add(new StatusEffect(StatusType.Desmoralizar, 1, 2));
            var eqDmg = ScriptableObject.CreateInstance<EquipmentCardData>();
            eqDmg.statModifiers = new List<StatModifier> { new StatModifier(StatType.Damage, 2) };
            attacker.Attach(eqDmg);
            e.PlayerAt(0).unitSlots[2] = attacker;
            e.PlayerAt(0).unitSlots[1] = new UnitSlot(U(10, Duel(0), null, Aura(4)));

            // 5 base + 2 equipo + 3 furia − 1 desmoralizar + 4 aura = 13
            Assert.AreEqual(13, e.EffectiveAttackDamage(e.PlayerAt(0).unitSlots, 2));
        }

        [Test]
        public void Retaliate_HitsAttacker_AndFiresEvenOnDefenderDeath()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(50)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10, Duel(0), null, Retaliate(3)));
            e.BeginTurn();

            e.AttackWithUnit(2);
            Assert.IsNull(e.PlayerAt(1).unitSlots[2]);              // defensor muere
            Assert.AreEqual(7, e.PlayerAt(0).unitSlots[2].currentHp);  // Espinas pega igual
        }

        // ── Curación ──────────────────────────────────────────────────────────

        [Test]
        public void Heal_RestoresAllies_CapsAtMax()
        {
            var e = Combat(out _);
            var healAtk = new UnitAttack(AttackReference.Relative, new[] { 1 }, 0, 6, AttackEffect.HealAllies);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, healAtk));
            var ally = new UnitSlot(U(10)); ally.currentHp = 2;
            e.PlayerAt(0).unitSlots[3] = ally;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            e.AttackWithUnit(2);
            Assert.AreEqual(8, e.PlayerAt(0).unitSlots[3].currentHp);  // 2 + 6
        }

        // ── Estados por unidad ────────────────────────────────────────────────

        [Test]
        public void Poison_DamagesAtTurnStart_DecrementsAtTurnEnd()
        {
            var e = NewEngine(PlainCfg(), out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            var u = new UnitSlot(U(20));
            u.activeStatuses.Add(new StatusEffect(StatusType.Poison, 5, 2));
            e.PlayerAt(0).unitSlots[0] = u;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(20));

            e.BeginTurn();                    // turno 1 (p0): veneno 20→15
            Assert.AreEqual(15, e.PlayerAt(0).unitSlots[0].currentHp);
            e.EndTurn();                      // tick: counter 2→1
            e.BeginTurn(); e.EndTurn();        // turno 2 (p1)
            e.BeginTurn();                    // turno 3 (p0): veneno 15→10
            Assert.AreEqual(10, e.PlayerAt(0).unitSlots[0].currentHp);
            e.EndTurn();                      // tick: counter 1→0, se elimina
            e.BeginTurn(); e.EndTurn();
            e.BeginTurn();                    // turno 5 (p0): ya sin veneno
            Assert.AreEqual(10, e.PlayerAt(0).unitSlots[0].currentHp);
        }

        [Test]
        public void Stun_BlocksAttack_ExpiresAfterOwnerTurn()
        {
            var e = Combat(out _);
            var u = new UnitSlot(U(10, Duel(5)));
            u.activeStatuses.Add(new StatusEffect(StatusType.Stun, 0, 1));
            e.PlayerAt(0).unitSlots[2] = u;
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            e.BeginTurn();

            Assert.AreEqual(ActionResult.UnitStunned, e.AttackWithUnit(2));
            e.EndTurn();                      // tick: stun expira
            e.BeginTurn(); e.EndTurn();        // turno p1
            e.BeginTurn();                    // p0 de nuevo
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
        }

        // ── Equipo ────────────────────────────────────────────────────────────

        [Test]
        public void Equipment_MaxHp_RaisesCurrentHp()
        {
            var slot = new UnitSlot(U(10));
            var eq = ScriptableObject.CreateInstance<EquipmentCardData>();
            eq.statModifiers = new List<StatModifier> { new StatModifier(StatType.MaxHp, 5) };
            slot.Attach(eq);
            Assert.AreEqual(15, slot.MaxHp);
            Assert.AreEqual(15, slot.currentHp);
        }

        [Test]
        public void Equipment_GrantedPassive_CountsInAllPassives()
        {
            var slot = new UnitSlot(U(10));
            var eq = ScriptableObject.CreateInstance<EquipmentCardData>();
            eq.grantedPassives = new List<PassiveEffect> { Retaliate(2) };
            slot.Attach(eq);
            int retaliate = 0;
            foreach (var p in slot.AllPassives())
                if (p.passiveType == PassiveType.Retaliate) retaliate += p.value;
            Assert.AreEqual(2, retaliate);
        }

        // ── MoveUnit / SwapUnits ───────────────────────────────────────────────

        [Test]
        public void MoveUnit_MovesToFreeAllowedSlot()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            var action = ScriptableObject.CreateInstance<ActionCardData>();
            action.id = "mv"; action.costs = new List<ResourceCost>();
            action.effects = new List<CardEffect> { new CardEffect(CardEffectType.MoveUnit, TargetType.Self) };
            e.PlayerAt(0).hand[0] = action;

            Assert.AreEqual(ActionResult.Success, e.PlayCard(0, effectTargetSlot: 0, effectTargetSlotB: 4));
            Assert.IsNull(e.PlayerAt(0).unitSlots[0]);
            Assert.IsNotNull(e.PlayerAt(0).unitSlots[4]);
        }

        [Test]
        public void SwapUnits_SwapsEnemySlots()
        {
            var e = Combat(out _);
            var a = new UnitSlot(U(10)); var b = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[0] = a;
            e.PlayerAt(1).unitSlots[5] = b;
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10));  // keepalive (jugador activo)
            e.BeginTurn();

            var action = ScriptableObject.CreateInstance<ActionCardData>();
            action.id = "sw"; action.costs = new List<ResourceCost>();
            action.effects = new List<CardEffect> { new CardEffect(CardEffectType.SwapUnits, TargetType.Opponent) };
            e.PlayerAt(0).hand[0] = action;

            Assert.AreEqual(ActionResult.Success, e.PlayCard(0, effectTargetSlot: 0, effectTargetSlotB: 5));
            Assert.AreSame(b, e.PlayerAt(1).unitSlots[0]);
            Assert.AreSame(a, e.PlayerAt(1).unitSlots[5]);
        }

        // ── Producción y reglas de inicio ──────────────────────────────────────

        [Test]
        public void Production_BasePlusProducer()
        {
            var cfg = PlainCfg();
            cfg.baseProdDinero = 1; cfg.baseProdFuerza = 1; cfg.baseProdSocial = 1;
            cfg.firstProducesTurn1 = true;
            var e = NewEngine(cfg, out _,
                U(10, Duel(0), new[] { 0 }, new PassiveEffect(PassiveType.ProduceResource, ResourceType.Dinero, 2)));
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            int before = e.PlayerAt(0).dinero;
            e.BeginTurn();
            Assert.AreEqual(before + 3, e.PlayerAt(0).dinero);  // base +1 + productora +2
            Assert.AreEqual(before + 1, e.PlayerAt(0).social);  // sólo base
        }

        [Test]
        public void FirstPlayer_CannotAttackTurn1_ButProduces()
        {
            var cfg = new GameConfig { suddenDeathStart = 999, maxTurns = 999 };  // reglas de inicio ON (default)
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(5)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            int dineroBefore = e.PlayerAt(0).dinero;

            e.BeginTurn();
            Assert.AreEqual(dineroBefore + 1, e.PlayerAt(0).dinero);   // SÍ produce
            Assert.IsFalse(e.CanAttackThisTurn);
            Assert.AreEqual(ActionResult.CannotAttackFirstTurn, e.AttackWithUnit(2));
        }

        [Test]
        public void SkipAndDoubleProduction_Work()
        {
            var cfg = PlainCfg();
            cfg.baseProdDinero = 2; cfg.firstProducesTurn1 = true;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(0).activeStatuses.Add(new StatusEffect(StatusType.DoubleProduction, 2, 1));
            int before = e.PlayerAt(0).dinero;
            e.BeginTurn();
            Assert.AreEqual(before + 4, e.PlayerAt(0).dinero);  // base 2 × doble
        }

        // ── Muerte súbita / desempate / empate ─────────────────────────────────

        [Test]
        public void SuddenDeath_DamagesAllUnits()
        {
            var cfg = PlainCfg(); cfg.suddenDeathStart = 1; cfg.suddenDeathDamage = 1;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(3));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(3));
            e.BeginTurn();
            e.EndTurn();  // half_turn 1 >= 1 → ambos 3→2
            Assert.AreEqual(2, e.PlayerAt(0).unitSlots[0].currentHp);
            Assert.AreEqual(2, e.PlayerAt(1).unitSlots[0].currentHp);
        }

        [Test]
        public void SuddenDeath_SimultaneousDeath_IsDraw()
        {
            var cfg = PlainCfg(); cfg.suddenDeathStart = 1; cfg.suddenDeathDamage = 5;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(3));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(3));
            e.BeginTurn();
            e.EndTurn();
            Assert.IsTrue(e.IsFinished);
            Assert.IsTrue(e.Outcome.Value.IsDraw);
            Assert.AreEqual(WinCondition.Draw, e.Outcome.Value.Condition);
        }

        [Test]
        public void Timeout_Tiebreak_MoreUnitsWins()
        {
            var cfg = PlainCfg(); cfg.maxTurns = 1;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(0).unitSlots[1] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();
            e.EndTurn();
            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(WinCondition.Timeout, e.Outcome.Value.Condition);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);  // 2 > 1
        }
    }
}
