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

            /// <summary>Mazo = pool expandido por drawWeight (igual que CardCatalog en producción).</summary>
            public IReadOnlyList<CardData> GetDeckList(Faction faction)
            {
                var deck = new List<CardData>();
                foreach (CardData c in pool)
                {
                    int copies = c.drawWeight > 0 ? c.drawWeight : 1;
                    for (int i = 0; i < copies; i++) deck.Add(c);
                }
                return deck;
            }
        }

        /// <summary>RNG determinista: Next siempre 0, Choice toma el primero.</summary>
        private sealed class ZeroRng : IRandomProvider
        {
            public int Next(int maxExclusive) => 0;
            public T Choice<T>(IReadOnlyList<T> list) => list[0];
        }

        /// <summary>Ataque melee: pega a la unidad enemiga más adelantada (Frontmost, alcance 1).</summary>
        private static UnitAttack Duel(int dmg) =>
            new UnitAttack(TargetMode.Frontmost, 1, dmg);

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
            mode = TargetMode.Adjacent, count = 0
        };

        private static PassiveEffect Retaliate(int v) =>
            new PassiveEffect { passiveType = PassiveType.Retaliate, value = v, target = PassiveTarget.Self };

        private static GameConfig PlainCfg() => new GameConfig
        {
            firstNoAttackTurn1 = false, firstProducesTurn1 = false,
            suddenDeathStart = 999, maxTurns = 999,
            baseProdDinero = 0, baseProdFuerza = 0, baseProdSocial = 0,
            attackFuerzaCost = 0   // tests de mecánica de combate: sin fricción de ⚡ (se testea aparte)
        };

        private static GameEngine NewEngine(GameConfig cfg, out TestCatalog cat, params UnitCardData[] starting)
        {
            cat = new TestCatalog();
            for (int i = 0; i < 8; i++) cat.pool.Add(U(1));   // dummies para llenar la mano (mazo finito)
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
        public void ResolveTargets_Frontmost_AnchorsAtForemostOccupied()
        {
            var board = new UnitSlot[6];
            board[1] = new UnitSlot(U(10));
            board[4] = new UnitSlot(U(10));   // foremost = índice 4 (mayor = más cerca del rival)
            CollectionAssert.AreEqual(new[] { 4 },
                GameEngine.ResolveTargets(TargetMode.Frontmost, 1, board, 0));
            // count 2 incluye la posición de atrás (3) aunque esté vacía (whiff aguas abajo)
            CollectionAssert.AreEqual(new[] { 4, 3 },
                GameEngine.ResolveTargets(TargetMode.Frontmost, 2, board, 0));
        }

        [Test]
        public void ResolveTargets_Backmost_AnchorsAtRearmostOccupied()
        {
            var board = new UnitSlot[6];
            board[1] = new UnitSlot(U(10));
            board[4] = new UnitSlot(U(10));
            CollectionAssert.AreEqual(new[] { 1 },
                GameEngine.ResolveTargets(TargetMode.Backmost, 1, board, 0));
        }

        [Test]
        public void ResolveTargets_All_ReturnsAllOccupied()
        {
            var board = new UnitSlot[6];
            board[1] = new UnitSlot(U(10));
            board[4] = new UnitSlot(U(10));
            CollectionAssert.AreEquivalent(new[] { 1, 4 },
                GameEngine.ResolveTargets(TargetMode.All, 0, board, 0));
        }

        [Test]
        public void ResolveTargets_Adjacent_ReturnsNeighbors()
        {
            var board = new UnitSlot[6];
            CollectionAssert.AreEquivalent(new[] { 1, 3 },
                GameEngine.ResolveTargets(TargetMode.Adjacent, 0, board, 2));
            CollectionAssert.AreEquivalent(new[] { 1 },
                GameEngine.ResolveTargets(TargetMode.Adjacent, 0, board, 0));  // borde: sólo el vecino válido
        }

        // ── Invariante anti-deadlock (regresión del bug raíz) ────────────────────

        [Test]
        public void Frontmost_AlwaysConnects_FromAnyPosition_NoDeadlock()
        {
            // Antes: atacante atrás vs. defensor adelante que no se tocaban → la partida se trababa.
            // Ahora Frontmost siempre golpea a la unidad más adelantada del rival, esté donde esté
            // el atacante. Para cualquier combinación de posiciones, el ataque conecta.
            for (int atkPos = 0; atkPos < 6; atkPos++)
                for (int defPos = 0; defPos < 6; defPos++)
                {
                    var e = Combat(out _);
                    e.PlayerAt(0).unitSlots[atkPos] = new UnitSlot(U(10, Duel(4)));
                    e.PlayerAt(1).unitSlots[defPos] = new UnitSlot(U(10));
                    e.BeginTurn();
                    Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(atkPos),
                        $"atk={atkPos} def={defPos}");
                    Assert.AreEqual(6, e.PlayerAt(1).unitSlots[defPos].currentHp,
                        $"atk={atkPos} def={defPos}");
                }
        }

        // ── Ataque ───────────────────────────────────────────────────────────

        [Test]
        public void Attack_Frontmost_HitsForemostEnemy()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10, Duel(4)));  // atacante al fondo
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[5] = new UnitSlot(U(10));  // foremost = 5 (más cerca del rival)
            e.BeginTurn();

            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(0));
            Assert.AreEqual(6, e.PlayerAt(1).unitSlots[5].currentHp);   // pegó al más adelantado
            Assert.AreEqual(10, e.PlayerAt(1).unitSlots[2].currentHp);  // el de atrás, intacto
        }

        [Test]
        public void Attack_Frontmost_ConnectsToForemost_NoDeadlock()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(4)));
            e.PlayerAt(1).unitSlots[5] = new UnitSlot(U(10));  // enfrentado (2) vacío, pero hay foremost en 5
            e.BeginTurn();

            // Antes esto se trababa (whiff sin objetivo). Ahora Frontmost pega al más adelantado (5).
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
            Assert.AreEqual(6, e.PlayerAt(1).unitSlots[5].currentHp);
            Assert.IsTrue(e.PlayerAt(0).unitSlots[2].attackedThisTurn);  // la unidad consumió su ataque
        }

        [Test]
        public void Attack_Penetrate_HitsForemost_WhiffsEmptyDepth()
        {
            var e = Combat(out _);
            var pierce = new UnitAttack(TargetMode.Frontmost, 2, 5);  // penetra a los 2 de adelante
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, pierce));
            e.PlayerAt(1).unitSlots[4] = new UnitSlot(U(20));  // foremost = 4; el de atrás (3) vacío → whiff
            e.BeginTurn();

            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));  // pega al foremost, whiff en el vacío
            Assert.AreEqual(15, e.PlayerAt(1).unitSlots[4].currentHp);
            Assert.IsTrue(e.PlayerAt(0).unitSlots[2].attackedThisTurn);  // la unidad consumió su ataque
        }

        [Test]
        public void Attack_CostsFuerza_DeductedAndBlockedWhenInsufficient()
        {
            var cfg = PlainCfg();
            cfg.attackFuerzaCost = 2;   // cada ataque cuesta 2 ⚡ (spec §3/§6)
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            var p0 = e.PlayerAt(0);
            p0.unitSlots[2] = new UnitSlot(U(10, Duel(4)));
            p0.unitSlots[1] = new UnitSlot(U(10, Duel(4)));
            e.PlayerAt(1).unitSlots[5] = new UnitSlot(U(100));  // tanque keepalive
            e.BeginTurn();
            p0.SetResource(ResourceType.Fuerza, 3, cfg.maxResource);  // alcanza para UN ataque (cuesta 2)

            Assert.IsTrue(e.UnitCanAttack(2));
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
            Assert.AreEqual(1, p0.GetResource(ResourceType.Fuerza), "se descuenta el costo de ataque");

            // Con 1 de ⚡ (< 2) la otra unidad NO puede atacar.
            Assert.IsFalse(e.UnitCanAttack(1));
            Assert.AreEqual(ActionResult.CannotAfford, e.AttackWithUnit(1));
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
        public void Attack_Snipe_RequiresExactChoice()
        {
            var e = Combat(out _);
            var snipe = new UnitAttack(TargetMode.Any, 1, 5);
            e.PlayerAt(0).unitSlots[3] = new UnitSlot(U(10, snipe, new[] { 3 }));
            e.PlayerAt(1).unitSlots[1] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[4] = new UnitSlot(U(10));
            e.BeginTurn();

            Assert.AreEqual(ActionResult.NeedsAttackTarget, e.AttackWithUnit(3));          // falta elegir
            Assert.AreEqual(ActionResult.InvalidTarget, e.AttackWithUnit(3, new[] { 0 }));  // slot 0 vacío
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(3, new[] { 1 }));        // snipe al slot 1
            Assert.AreEqual(5, e.PlayerAt(1).unitSlots[1].currentHp);
            Assert.AreEqual(10, e.PlayerAt(1).unitSlots[4].currentHp);  // el otro, intacto
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
            var healAtk = new UnitAttack(TargetMode.All, 0, 6, AttackEffect.HealAllies);
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

        [Test]
        public void Timeout_Tiebreak_EqualUnits_MoreHpWins()
        {
            var cfg = PlainCfg(); cfg.maxTurns = 1;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();
            e.EndTurn();
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);  // 20 > 10 HP
        }

        [Test]
        public void Timeout_Tiebreak_FullTie_NotFirstWins()
        {
            var cfg = PlainCfg(); cfg.maxTurns = 1;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();
            e.EndTurn();
            // empate de unidades y HP → gana el que NO fue primero (firstIndex 0 → gana p1)
            Assert.AreEqual(Faction.Policias, e.Outcome.Value.Winner);
        }

        // ── Interacciones de daño efectivo (integradas, no sólo el cálculo) ────

        [Test]
        public void Attack_AppliesEffectiveDamage_AuraFuriaEquipMinusDesmoralizar()
        {
            var e = Combat(out _);
            var attacker = new UnitSlot(U(10, Duel(5)));
            attacker.activeStatuses.Add(new StatusEffect(StatusType.Furia, 3, 2));
            attacker.activeStatuses.Add(new StatusEffect(StatusType.Desmoralizar, 1, 2));
            attacker.Attach(EquipDamage(2));
            e.PlayerAt(0).unitSlots[2] = attacker;
            e.PlayerAt(0).unitSlots[1] = new UnitSlot(U(10, Duel(0), null, Aura(4)));  // aura cubre slot 2
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(50));
            e.BeginTurn();

            e.AttackWithUnit(2);
            // 5 base + 2 equipo + 3 furia − 1 desmoralizar + 4 aura = 13
            Assert.AreEqual(50 - 13, e.PlayerAt(1).unitSlots[2].currentHp);
        }

        [Test]
        public void Desmoralizar_FloorsEffectiveDamageAtZero()
        {
            var e = Combat(out _);
            var attacker = new UnitSlot(U(10, Duel(3)));
            attacker.activeStatuses.Add(new StatusEffect(StatusType.Desmoralizar, 10, 2));
            e.PlayerAt(0).unitSlots[2] = attacker;
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10));
            e.BeginTurn();

            e.AttackWithUnit(2);
            Assert.AreEqual(10, e.PlayerAt(1).unitSlots[2].currentHp);  // daño 3−10 → 0
        }

        [Test]
        public void Retaliate_Lethal_KillsAttacker_OpponentWins()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(2, Duel(50)));        // único de p0, frágil
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10, Duel(0), null, Retaliate(5)));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));                 // keepalive de p1
            e.BeginTurn();

            e.AttackWithUnit(2);
            Assert.IsNull(e.PlayerAt(1).unitSlots[2]);   // defensor muere por el golpe
            Assert.IsNull(e.PlayerAt(0).unitSlots[2]);   // atacante muere por Espinas
            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Policias, e.Outcome.Value.Winner);  // p0 se quedó sin unidades
        }

        [Test]
        public void Equipment_GrantedAura_AddsToNeighborEffectiveDamage()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(5)));
            var auraUnit = new UnitSlot(U(10));
            var eq = ScriptableObject.CreateInstance<EquipmentCardData>();
            eq.grantedPassives = new List<PassiveEffect> { Aura(3) };
            auraUnit.Attach(eq);
            e.PlayerAt(0).unitSlots[1] = auraUnit;  // aura desde slot 1 cubre slot 2

            Assert.AreEqual(8, e.EffectiveAttackDamage(e.PlayerAt(0).unitSlots, 2));  // 5 + 3
        }

        // ── Curación: whiff / cap ──────────────────────────────────────────────

        [Test]
        public void Heal_NoHealableAlly_IsCanceled_NoConsume()
        {
            var e = Combat(out _);
            var healer = new UnitSlot(U(10, new UnitAttack(TargetMode.All, 0, 6, AttackEffect.HealAllies)));
            e.PlayerAt(0).unitSlots[2] = healer;  // sólo el healer, a full HP → nadie a quien curar
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            Assert.AreEqual(ActionResult.InvalidTarget, e.AttackWithUnit(2));
            Assert.IsFalse(e.PlayerAt(0).unitSlots[2].attackedThisTurn);  // se cancela sin gastar (spec §6)
        }

        [Test]
        public void Passive_TurnDamage_Frontmost_HitsOnlyForemost()
        {
            var e = Combat(out _);
            var emisor = new UnitSlot(U(10, Duel(0), null, new PassiveEffect
            {
                passiveType = PassiveType.TurnDamage, value = 3, target = PassiveTarget.Enemies,
                mode = TargetMode.Frontmost, count = 1
            }));
            e.PlayerAt(0).unitSlots[0] = emisor;
            var a = new UnitSlot(U(20)); var b = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[3] = a;
            e.PlayerAt(1).unitSlots[4] = b;  // foremost = 4
            e.BeginTurn();  // TurnDamage Frontmost count 1 pega sólo al más adelantado

            Assert.AreEqual(20, a.currentHp);  // intacto
            Assert.AreEqual(17, b.currentHp);  // 20 − 3 (foremost)
        }

        // ── Pasivas de inicio de turno ─────────────────────────────────────────

        [Test]
        public void Regeneration_HealsAtTurnStart_CapsAtMaxHp()
        {
            var e = Combat(out _);
            var u = new UnitSlot(U(10, Duel(0), null, Regen(5)));
            u.currentHp = 8;
            e.PlayerAt(0).unitSlots[0] = u;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();
            Assert.AreEqual(10, e.PlayerAt(0).unitSlots[0].currentHp);  // 8 + 5 capado a 10
        }

        [Test]
        public void TurnDamage_KillsEnemyAtTurnStart_Wins()
        {
            var e = Combat(out _);
            // Emisor con Humo: TurnDamage a la vanguardia enemiga {3,4,5}
            var emisor = new UnitSlot(U(10, Duel(0), null, TurnDmg(50, 3)));
            e.PlayerAt(0).unitSlots[0] = emisor;
            e.PlayerAt(1).unitSlots[4] = new UnitSlot(U(10));  // única de p1, en vanguardia
            e.BeginTurn();
            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
        }

        [Test]
        public void TurnStatus_GasPoisonsEnemyFront_AtTurnStart()
        {
            var e = Combat(out _);
            var gasero = new UnitSlot(U(10, Duel(0), null, GasPoison(3)));
            e.PlayerAt(0).unitSlots[0] = gasero;
            var enemy = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[4] = enemy;  // vanguardia enemiga
            e.BeginTurn();
            Assert.IsTrue(enemy.HasStatus(StatusType.Poison));
            Assert.AreEqual(20, enemy.currentHp);  // el veneno daña en EL turno del enemigo, no ahora
        }

        // ── Death-rattle (OnDeath, spec §7.3) ──────────────────────────────────

        [Test]
        public void OnDeath_Furia_BuffsAdjacentAllies_WhenKilledByAttack()
        {
            var e = Combat(out _);
            // Jubilado mártir frágil en la vanguardia (slot 4 = foremost de p0): al morir, Furia a vecinos.
            e.PlayerAt(0).unitSlots[4] = new UnitSlot(U(5, Duel(0), null, OnDeathFuria(4, 2)));
            var adj = new UnitSlot(U(20, Duel(5))); e.PlayerAt(0).unitSlots[3] = adj;   // adyacente
            var far = new UnitSlot(U(20, Duel(5))); e.PlayerAt(0).unitSlots[1] = far;   // no adyacente
            e.PlayerAt(1).unitSlots[4] = new UnitSlot(U(20, Duel(50)));                 // lo mata

            e.BeginTurn();                 // p0 (ht1)
            e.EndTurn(); e.BeginTurn();    // p1 (ht2): ataca al foremost de p0 = slot 4 (Jubilado)
            e.AttackWithUnit(4);

            Assert.IsNull(e.PlayerAt(0).unitSlots[4]);       // murió
            Assert.IsTrue(adj.HasStatus(StatusType.Furia));  // vecino: envalentonado
            Assert.IsFalse(far.HasStatus(StatusType.Furia)); // lejano: no
        }

        [Test]
        public void OnDeath_FiresOnPoisonDeath()
        {
            var e = Combat(out _);
            var jub = new UnitSlot(U(3, Duel(0), null, OnDeathFuria(4, 2)));
            jub.activeStatuses.Add(new StatusEffect(StatusType.Poison, 5, 2));  // lo mata en EFECTOS
            e.PlayerAt(0).unitSlots[4] = jub;
            var adj = new UnitSlot(U(20, Duel(5))); e.PlayerAt(0).unitSlots[3] = adj;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(20));  // keepalive

            e.BeginTurn();  // p0: veneno 3→-2 → muere → OnDeath dispara aunque la fuente no sea un ataque
            Assert.IsNull(e.PlayerAt(0).unitSlots[4]);
            Assert.IsTrue(adj.HasStatus(StatusType.Furia));
        }

        [Test]
        public void OnDeath_FiresOnSuddenDeath()
        {
            var cfg = PlainCfg(); cfg.suddenDeathStart = 1; cfg.suddenDeathDamage = 5;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[4] = new UnitSlot(U(3, Duel(0), null, OnDeathFuria(4, 2)));
            var adj = new UnitSlot(U(20, Duel(5))); e.PlayerAt(0).unitSlots[3] = adj;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(20));  // keepalive (sobrevive el −5)

            e.BeginTurn();
            e.EndTurn();  // muerte súbita: Jubilado 3→-2 muere; el aliado 20→15 sobrevive
            Assert.IsNull(e.PlayerAt(0).unitSlots[4]);
            Assert.IsTrue(adj.HasStatus(StatusType.Furia));  // el death-rattle disparó en muerte súbita
        }

        [Test]
        public void OnDeath_Explosion_DamagesEnemyFront()
        {
            var e = Combat(out _);
            var attacker = new UnitSlot(U(40, Duel(50)));  // tanque: aguanta la explosión
            e.PlayerAt(0).unitSlots[2] = attacker;         // único de p0 → foremost de p0 = slot 2
            // Bomba: al morir, daño Frontmost a enemigos (= al foremost de p0, el atacante).
            var bomber = new UnitSlot(U(5, Duel(0), null, OnDeathExplosion(20, TargetMode.Frontmost, 1)));
            e.PlayerAt(1).unitSlots[5] = bomber;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(20));  // keepalive de p1 (no KO)
            e.BeginTurn();

            e.AttackWithUnit(2);  // mata a la bomba (5hp) → explota Frontmost sobre p0 → atacante 40−20
            Assert.IsNull(e.PlayerAt(1).unitSlots[5]);
            Assert.AreEqual(20, e.PlayerAt(0).unitSlots[2].currentHp);
        }

        [Test]
        public void OnDeath_Chains_ThroughAlliedDeathrattles()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(40, Duel(50)));  // mata al foremost del rival
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(20));            // keepalive de p0
            // BombaA en la vanguardia de p1: al morir daña a TODOS sus propios aliados (encadena).
            var bombA = new UnitSlot(U(5, Duel(0), null, OnDeathDamageAllies(50, TargetMode.All, 0)));
            e.PlayerAt(1).unitSlots[5] = bombA;
            var bombB = new UnitSlot(U(4));  // frágil; muere por la explosión de A
            e.PlayerAt(1).unitSlots[1] = bombB;
            e.BeginTurn();

            e.AttackWithUnit(2);  // mata a A → A daña a sus aliados (All) → B muere (encadenado) → p1 sin unidades
            Assert.IsNull(e.PlayerAt(1).unitSlots[5]);
            Assert.IsNull(e.PlayerAt(1).unitSlots[1]);
            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
        }

        // ── Blindaje / Chorro (mecánicas nuevas de Policías, spec §7.3) ────────

        [Test]
        public void Armor_ReducesAttackDamage()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(10)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(20, Duel(0), null, Armor(4)));
            e.BeginTurn();
            e.AttackWithUnit(2);
            Assert.AreEqual(20 - 6, e.PlayerAt(1).unitSlots[2].currentHp);  // 10 − 4 de blindaje
        }

        [Test]
        public void Armor_FloorsDamageAtZero()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10, Duel(3)));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(20, Duel(0), null, Armor(5)));
            e.BeginTurn();
            e.AttackWithUnit(2);
            Assert.AreEqual(20, e.PlayerAt(1).unitSlots[2].currentHp);  // 3 − 5 → 0 (no cura)
        }

        [Test]
        public void Armor_DoesNotMitigateSuddenDeath()
        {
            var cfg = PlainCfg(); cfg.suddenDeathStart = 1; cfg.suddenDeathDamage = 3;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10, Duel(0), null, Armor(5)));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();
            e.EndTurn();  // muerte súbita ignora defensas (spec §5.1)
            Assert.AreEqual(7, e.PlayerAt(0).unitSlots[0].currentHp);  // 10 − 3 completo
        }

        [Test]
        public void PushBack_ShovesTargetToRearmostFreeSlot()
        {
            var e = Combat(out _);
            var carro = new UnitSlot(U(10, new UnitAttack(TargetMode.Frontmost, 1, 3), null, PushBack()));
            e.PlayerAt(0).unitSlots[2] = carro;
            var victim = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[5] = victim;  // foremost del rival; 0..4 libres
            e.BeginTurn();
            e.AttackWithUnit(2);
            Assert.IsNull(e.PlayerAt(1).unitSlots[5]);          // ya no está adelante
            Assert.AreSame(victim, e.PlayerAt(1).unitSlots[0]); // empujado al fondo (slot 0)
            Assert.AreEqual(17, victim.currentHp);              // recibió el daño igual
        }

        [Test]
        public void PushBack_AlreadyAtBack_IsNoOp()
        {
            var e = Combat(out _);
            var carro = new UnitSlot(U(10, new UnitAttack(TargetMode.Frontmost, 1, 3), null, PushBack()));
            e.PlayerAt(0).unitSlots[2] = carro;
            var victim = new UnitSlot(U(20));
            e.PlayerAt(1).unitSlots[0] = victim;  // ya en el fondo; foremost = slot 0 (no hay lugar atrás)
            e.BeginTurn();
            e.AttackWithUnit(2);
            Assert.AreSame(victim, e.PlayerAt(1).unitSlots[0]);  // no se mueve
            Assert.AreEqual(17, victim.currentHp);
        }

        // ── Estados: producción y combinaciones ────────────────────────────────

        [Test]
        public void DoubleAndSkipProduction_SkipWins()
        {
            var cfg = PlainCfg();
            cfg.baseProdDinero = 2; cfg.firstProducesTurn1 = true;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(0).activeStatuses.Add(new StatusEffect(StatusType.DoubleProduction, 2, 1));
            e.PlayerAt(0).activeStatuses.Add(new StatusEffect(StatusType.SkipProduction, 0, 1));
            int before = e.PlayerAt(0).dinero;
            e.BeginTurn();
            Assert.AreEqual(before, e.PlayerAt(0).dinero);  // skip gana: no produce nada
        }

        [Test]
        public void Poison_AndSuddenDeath_BothApplySameTurn()
        {
            var cfg = PlainCfg(); cfg.suddenDeathStart = 1; cfg.suddenDeathDamage = 1;
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            var u = new UnitSlot(U(10));
            u.activeStatuses.Add(new StatusEffect(StatusType.Poison, 3, 2));
            e.PlayerAt(0).unitSlots[0] = u;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();                   // veneno: 10 → 7
            Assert.AreEqual(7, u.currentHp);
            e.EndTurn();                     // muerte súbita (ht1≥1): ambos −1
            Assert.AreEqual(6, e.PlayerAt(0).unitSlots[0].currentHp);
            Assert.AreEqual(9, e.PlayerAt(1).unitSlots[0].currentHp);
        }

        // ── Acciones: efectos sobre recursos / unidades ────────────────────────

        [Test]
        public void ModifyResource_ClampsAtZeroAndMax()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            e.BeginTurn();

            e.PlayerAt(0).dinero = 2;
            e.PlayerAt(0).hand[0] = ActionCard(
                new CardEffect(CardEffectType.ModifyResource, TargetType.Self, ResourceType.Dinero, -10));
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));
            Assert.AreEqual(0, e.PlayerAt(0).dinero);  // no baja de 0

            e.PlayerAt(0).fuerza = e.Config.maxResource - 1;
            // segundo turno para volver a poder jugar carta
            e.EndTurn(); e.BeginTurn(); e.EndTurn(); e.BeginTurn();
            e.PlayerAt(0).hand[0] = ActionCard(
                new CardEffect(CardEffectType.ModifyResource, TargetType.Self, ResourceType.Fuerza, 50));
            e.PlayCard(0);
            Assert.AreEqual(e.Config.maxResource, e.PlayerAt(0).fuerza);  // capado al máximo
        }

        [Test]
        public void ApplyStatus_RoutesPlayerStatusToPlayer_UnitStatusToUnit()
        {
            var e = Combat(out _);
            var enemy = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[2] = enemy;
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));  // keepalive activo
            e.BeginTurn();

            // SkipProduction (estado de jugador) sobre el oponente → va al jugador
            e.PlayerAt(0).hand[0] = ActionCard(new CardEffect(CardEffectType.ApplyStatus,
                TargetType.Opponent, status: new StatusEffect(StatusType.SkipProduction, 0, 1)));
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));
            Assert.AreEqual(1, e.PlayerAt(1).activeStatuses.Count);
            Assert.IsFalse(enemy.HasStatus(StatusType.Poison));

            // Stun (estado de unidad) sobre una unidad enemiga → va a la unidad
            e.EndTurn(); e.BeginTurn(); e.EndTurn(); e.BeginTurn();  // de nuevo turno de p0
            e.PlayerAt(0).hand[0] = ActionCard(new CardEffect(CardEffectType.ApplyStatus,
                TargetType.Opponent, status: new StatusEffect(StatusType.Stun, 0, 1), targetSlot: 2));
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0, effectTargetSlot: 2));
            Assert.IsTrue(enemy.IsStunned);
        }

        // ── Despliegue / reemplazo ──────────────────────────────────────────────

        [Test]
        public void Deploy_NoFreeAllowedSlot_IsInvalid_NoReplacement()
        {
            var e = Combat(out _);
            var standing = new UnitSlot(U(10));
            e.PlayerAt(0).unitSlots[3] = standing;             // ocupa el único slot permitido
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            var unit = U(10, Duel(0), new[] { 3 });  // sólo puede ir al slot 3 (ocupado)
            e.PlayerAt(0).hand[0] = unit;
            Assert.AreEqual(ActionResult.InvalidTarget, e.PlayCard(0));               // auto: no hay libre
            Assert.AreEqual(ActionResult.InvalidTarget, e.PlayCard(0, deploySlot: 3)); // ocupado: sin reemplazo
            Assert.AreSame(standing, e.PlayerAt(0).unitSlots[3]);                     // la original queda intacta
        }

        [Test]
        public void Deploy_OntoOccupiedSlot_IsRejected_NoResourcesSpent()
        {
            var e = Combat(out _);
            var veteran = new UnitSlot(U(10));
            e.PlayerAt(0).unitSlots[0] = veteran;
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            var fresh = U(10, Duel(0));  // cualquiera, pero el slot 0 está ocupado
            e.PlayerAt(0).hand[0] = fresh;
            Assert.AreEqual(ActionResult.InvalidTarget, e.PlayCard(0, deploySlot: 0));
            Assert.AreSame(veteran, e.PlayerAt(0).unitSlots[0]);  // no se reemplaza
            Assert.AreSame(fresh, e.PlayerAt(0).hand[0]);         // la carta sigue en la mano (no se jugó)
        }

        [Test]
        public void Deploy_AutoPicksFirstFreeAllowedSlot()
        {
            var e = Combat(out _);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));  // slot 0 ocupado
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            var unit = U(10, Duel(0));  // cualquiera
            e.PlayerAt(0).hand[0] = unit;
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));     // auto → primer libre = slot 1
            Assert.AreSame(unit, e.PlayerAt(0).unitSlots[1].unit);
            Assert.IsNotNull(e.PlayerAt(0).unitSlots[0]);             // el slot 0 intacto
        }

        [Test]
        public void MoveUnit_ToOccupiedSlot_IsNoOp()
        {
            var e = Combat(out _);
            var mover = new UnitSlot(U(10));
            e.PlayerAt(0).unitSlots[0] = mover;
            e.PlayerAt(0).unitSlots[4] = new UnitSlot(U(10));  // destino ocupado
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));  // keepalive
            e.BeginTurn();

            e.PlayerAt(0).hand[0] = ActionCard(new CardEffect(CardEffectType.MoveUnit, TargetType.Self));
            e.PlayCard(0, effectTargetSlot: 0, effectTargetSlotB: 4);
            Assert.AreSame(mover, e.PlayerAt(0).unitSlots[0]);  // no se movió
        }

        [Test]
        public void SwapUnits_WithEmptySlot_MovesUnit()
        {
            var e = Combat(out _);
            var u = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[0] = u;
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10));  // keepalive activo
            e.BeginTurn();

            e.PlayerAt(0).hand[0] = ActionCard(new CardEffect(CardEffectType.SwapUnits, TargetType.Opponent));
            e.PlayCard(0, effectTargetSlot: 0, effectTargetSlotB: 3);
            Assert.IsNull(e.PlayerAt(1).unitSlots[0]);
            Assert.AreSame(u, e.PlayerAt(1).unitSlots[3]);
        }

        // ── Reglas de iniciativa ────────────────────────────────────────────────

        [Test]
        public void SecondPlayer_CanAttackOnItsFirstTurn()
        {
            var cfg = new GameConfig { suddenDeathStart = 999, maxTurns = 999 };  // reglas ON
            var e = NewEngine(cfg, out _);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[2] = new UnitSlot(U(10));
            e.PlayerAt(1).unitSlots[2] = new UnitSlot(U(10, Duel(5)));

            e.BeginTurn();                 // p0, half-turn 1: no puede atacar
            Assert.IsFalse(e.CanAttackThisTurn);
            e.EndTurn();
            e.BeginTurn();                 // p1, half-turn 2: SÍ puede
            Assert.IsTrue(e.CanAttackThisTurn);
            Assert.AreEqual(ActionResult.Success, e.AttackWithUnit(2));
            Assert.AreEqual(5, e.PlayerAt(0).unitSlots[2].currentHp);
        }

        // ── Mazo de robo (deck/discard, spec §8.1) ──────────────────────────────

        [Test]
        public void Deck_DrawsWithoutReplacement_HandPlusDeckHoldsEveryCard()
        {
            // 10 cartas distintas, 1 copia c/u; mano de 4 → mano ∪ mazo = las 10, sin repetición.
            var cat = new TestCatalog();
            for (int i = 0; i < 10; i++) { var c = U(1); c.id = "c" + i; cat.pool.Add(c); }
            var cfg = PlainCfg(); cfg.handSize = 4;
            var e = new GameEngine(cfg, new ZeroRng(), cat);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            var p = e.PlayerAt(0);

            Assert.AreEqual(4, p.hand.Count);
            Assert.AreEqual(6, p.deck.Count);
            var all = new HashSet<CardData>(p.hand);
            all.UnionWith(p.deck);
            Assert.AreEqual(10, all.Count, "mano ∪ mazo = las 10 cartas, sin repetición (sniper siempre disponible)");
        }

        [Test]
        public void Deck_DrawWeightControlsCopies()
        {
            var cat = new TestCatalog();
            var heavy = U(1); heavy.id = "H"; heavy.drawWeight = 3;
            var light = U(1); light.id = "L"; light.drawWeight = 1;
            cat.pool.Add(heavy); cat.pool.Add(light);
            var cfg = PlainCfg(); cfg.handSize = 0;          // no robar: inspeccionar el mazo completo
            var e = new GameEngine(cfg, new ZeroRng(), cat);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            var p = e.PlayerAt(0);

            Assert.AreEqual(4, p.deck.Count);                                  // 3 + 1
            Assert.AreEqual(3, p.deck.FindAll(c => c.id == "H").Count);
            Assert.AreEqual(1, p.deck.FindAll(c => c.id == "L").Count);
        }

        [Test]
        public void Deck_ReshufflesDiscardWhenDeckEmpties()
        {
            var cat = new TestCatalog();
            for (int i = 0; i < 3; i++) { var c = U(1); c.id = "d" + i; cat.pool.Add(c); }  // mazo de 3
            var cfg = PlainCfg(); cfg.handSize = 2;
            var e = new GameEngine(cfg, new ZeroRng(), cat);
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);
            e.PlayerAt(0).unitSlots[0] = new UnitSlot(U(10));   // keepalive: que la partida no termine
            e.PlayerAt(1).unitSlots[0] = new UnitSlot(U(10));
            var p = e.PlayerAt(0);

            // Varios turnos de p0 descartando: vacía el mazo y fuerza rebarajar el descarte.
            for (int t = 0; t < 6; t++)
            {
                e.BeginTurn();
                Assert.AreEqual(ActionResult.Success, e.DiscardCard(0));
                Assert.AreEqual(1, p.hand.Count, "tras descartar, la mano no se repone hasta el fin del turno");
                e.EndTurn();                                   // REPONER MANO acá
                Assert.AreEqual(2, p.hand.Count, "la mano se repone a handSize al fin del turno");
                foreach (CardData c in p.hand) Assert.IsNotNull(c, "nunca queda un hueco null en la mano");
                e.BeginTurn(); e.EndTurn();   // turno de p1
            }
            // Ninguna carta se perdió: las 3 distintas siguen en circulación.
            var all = new HashSet<CardData>(p.hand);
            all.UnionWith(p.deck); all.UnionWith(p.discard);
            Assert.AreEqual(3, all.Count);
        }

        // ── Helpers adicionales ─────────────────────────────────────────────────

        private static PassiveEffect Produce(ResourceType r, int v) =>
            new PassiveEffect(PassiveType.ProduceResource, r, v);

        private static PassiveEffect Regen(int v) =>
            new PassiveEffect { passiveType = PassiveType.Regeneration, value = v, target = PassiveTarget.Self };

        private static PassiveEffect TurnDmg(int v, int count) => new PassiveEffect
        {
            passiveType = PassiveType.TurnDamage, value = v, target = PassiveTarget.Enemies,
            mode = TargetMode.Frontmost, count = count
        };

        private static PassiveEffect GasPoison(int v) => new PassiveEffect
        {
            passiveType = PassiveType.TurnStatus, status = new StatusEffect(StatusType.Poison, v, 1),
            target = PassiveTarget.Enemies, mode = TargetMode.Frontmost, count = 1
        };

        /// <summary>Jubilado mártir: al morir, Furia (+v daño, c turnos) a los aliados adyacentes.</summary>
        private static PassiveEffect OnDeathFuria(int v, int c) => new PassiveEffect
        {
            passiveType = PassiveType.OnDeath, status = new StatusEffect(StatusType.Furia, v, c),
            target = PassiveTarget.Allies, mode = TargetMode.Adjacent, count = 0
        };

        /// <summary>Explosión: al morir, v de daño directo a enemigos según mode/count.</summary>
        private static PassiveEffect OnDeathExplosion(int v, TargetMode mode, int count) => new PassiveEffect
        {
            passiveType = PassiveType.OnDeath, value = v,
            target = PassiveTarget.Enemies, mode = mode, count = count
        };

        /// <summary>Variante de prueba: al morir, v de daño a los propios aliados (encadena death-rattles).</summary>
        private static PassiveEffect OnDeathDamageAllies(int v, TargetMode mode, int count) => new PassiveEffect
        {
            passiveType = PassiveType.OnDeath, value = v,
            target = PassiveTarget.Allies, mode = mode, count = count
        };

        /// <summary>Blindaje: reduce en v el daño de cada ataque de unidad recibido.</summary>
        private static PassiveEffect Armor(int v) =>
            new PassiveEffect { passiveType = PassiveType.Armor, value = v, target = PassiveTarget.Self };

        /// <summary>Chorro: al atacar, empuja al objetivo al fondo del rival.</summary>
        private static PassiveEffect PushBack() =>
            new PassiveEffect { passiveType = PassiveType.PushBack, target = PassiveTarget.Self };

        private static EquipmentCardData EquipDamage(int v)
        {
            var eq = ScriptableObject.CreateInstance<EquipmentCardData>();
            eq.statModifiers = new List<StatModifier> { new StatModifier(StatType.Damage, v) };
            return eq;
        }

        private static EquipmentCardData EquipMaxHp(int v)
        {
            var eq = ScriptableObject.CreateInstance<EquipmentCardData>();
            eq.statModifiers = new List<StatModifier> { new StatModifier(StatType.MaxHp, v) };
            return eq;
        }

        private static ActionCardData ActionCard(params CardEffect[] effects)
        {
            var a = ScriptableObject.CreateInstance<ActionCardData>();
            a.id = "act"; a.costs = new List<ResourceCost>();
            a.effects = new List<CardEffect>(effects);
            return a;
        }

        // ── Inflación (spec §3) ────────────────────────────────────────────────

        [Test]
        public void InflatedAmount_RoundsUp_AndIsIdentityWithoutInflation()
        {
            Assert.AreEqual(6, PlayerState.InflatedAmount(6, 0));    // sin inflación: identidad
            Assert.AreEqual(9, PlayerState.InflatedAmount(6, 50));   // ceil(6·1.5) = 9
            Assert.AreEqual(6, PlayerState.InflatedAmount(5, 8));    // ceil(5.4) = 6 (siempre redondea ↑)
            Assert.AreEqual(12, PlayerState.InflatedAmount(10, 20)); // ceil(12.0) = 12
            Assert.AreEqual(2, PlayerState.InflatedAmount(1, 8));    // ceil(1.08) = 2 (muerde aun el costo 1)
        }

        [Test]
        public void InflationPercent_KicksInAtStartTurn_AndScalesPerHalfTurn()
        {
            var cfg = PlainCfg();
            cfg.inflationStartTurn = 3;
            cfg.inflationPercentPerTurn = 10;
            var e = NewEngine(cfg, out _, U(10));   // unidades iniciales en ambos lados (keepalive)
            e.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: 0);

            e.BeginTurn();                       // HalfTurn 1
            Assert.AreEqual(0, e.InflationPercent);
            Assert.IsFalse(e.InflationActive);
            e.EndTurn(); e.BeginTurn();          // HalfTurn 2
            Assert.AreEqual(0, e.InflationPercent);
            e.EndTurn(); e.BeginTurn();          // HalfTurn 3 = arranca
            Assert.AreEqual(10, e.InflationPercent);
            Assert.IsTrue(e.InflationActive);
            e.EndTurn(); e.BeginTurn();          // HalfTurn 4
            Assert.AreEqual(20, e.InflationPercent);
        }

        [Test]
        public void CanAfford_AndPay_RespectInflation()
        {
            var p = new PlayerState(6) { dinero = 6, fuerza = 0, social = 0 };
            var card = ScriptableObject.CreateInstance<ActionCardData>();
            card.costs = new List<ResourceCost> { new ResourceCost(ResourceType.Dinero, 5) };
            card.effects = new List<CardEffect>();

            Assert.IsTrue(p.CanAfford(card, 0));    // 5 ≤ 6
            Assert.IsTrue(p.CanAfford(card, 8));    // ceil(5·1.08)=6 ≤ 6
            Assert.IsFalse(p.CanAfford(card, 30));  // ceil(5·1.3)=7 > 6

            p.Pay(card, 20);                        // ceil(5·1.2)=6
            Assert.AreEqual(0, p.dinero);
        }
    }
}
