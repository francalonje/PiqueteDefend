using System.Collections.Generic;
using NUnit.Framework;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests del motor derivados de los escenarios del spec y del simulador.
    /// player0 = Manifestantes, player1 = Policías. Con FakeRandom(0) arranca player0.
    /// </summary>
    public class GameEngineTests
    {
        private List<CardData> _man;
        private List<CardData> _pol;

        private GameEngine NewEngine(GameConfig config = null, int coin = 0)
        {
            (_man, _pol) = TestCards.BuildAll();
            var catalog = new TestCatalog(_man, _pol);
            var engine = new GameEngine(config ?? new GameConfig(), new FakeRandom(coin), catalog);
            engine.StartGame(Faction.Manifestantes, Faction.Policias);
            return engine;
        }

        private CardData Man(string id) => TestCards.Find(_man, id);
        private CardData Pol(string id) => TestCards.Find(_pol, id);

        // ── Setup ─────────────────────────────────────────────────────────────

        [Test]
        public void StartGame_InitialState()
        {
            var e = NewEngine();
            var p0 = e.PlayerAt(0);

            Assert.AreEqual(100, p0.hp);
            Assert.AreEqual(3, p0.dinero);
            Assert.AreEqual(2, p0.fuerza);
            Assert.AreEqual(1, p0.social);
            Assert.AreEqual(6, p0.hand.Count);
            Assert.IsEmpty(p0.unitSlots);
            Assert.IsEmpty(p0.activeStatuses);

            Assert.AreEqual(GamePhase.AwaitingTurnStart, e.Phase);
            Assert.AreEqual(0, e.HalfTurn);
            Assert.AreEqual(0, e.ActiveIndex);
        }

        [Test]
        public void Coinflip_RespectsRng()
        {
            var e = NewEngine(coin: 1);
            Assert.AreEqual(1, e.ActiveIndex);
            Assert.AreEqual(1, e.FirstIndex);
            Assert.AreEqual(Faction.Policias, e.FirstFaction);
        }

        // ── Producción ─────────────────────────────────────────────────────────

        [Test]
        public void BeginTurn_AppliesBaseProduction()
        {
            var cfg = new GameConfig();
            var e = NewEngine(cfg);
            e.BeginTurn();
            var p0 = e.PlayerAt(0);

            Assert.AreEqual(cfg.initialDinero + cfg.baseProdDinero, p0.dinero);
            Assert.AreEqual(cfg.initialFuerza + cfg.baseProdFuerza, p0.fuerza);
            Assert.AreEqual(cfg.initialSocial + cfg.baseProdSocial, p0.social);
            Assert.AreEqual(1, e.HalfTurn);
            Assert.AreEqual(GamePhase.AwaitingAction, e.Phase);
        }

        [Test]
        public void Production_NetDamage_DefenseAbsorbsFirst()
        {
            var e = NewEngine();
            // Oponente (player1) con 4 atacantes; activo (player0) con 2 defensivas.
            e.PlayerAt(1).unitSlots.Add(new UnitSlot(Pol("patrullero"), 4));
            e.PlayerAt(0).unitSlots.Add(new UnitSlot(Man("jubilado"), 2));

            e.BeginTurn();

            Assert.AreEqual(100 - 2, e.PlayerAt(0).hp);  // net = 4 - 2
        }

        // ── Acción ───────────────────────────────────────────────────────────────

        [Test]
        public void PlayCard_PaysCostAndReplacesCard()
        {
            var e = NewEngine();
            e.BeginTurn();
            var p0 = e.PlayerAt(0);
            p0.hand[0] = Man("colecta");  // 3 social → +6 dinero

            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));

            Assert.AreEqual(6 + 6, p0.dinero);   // base dinero 3: 3+3=6, +6 colecta
            Assert.AreEqual(3 - 3, p0.social);
            Assert.AreEqual("piquetero", p0.hand[0].id);   // repuesta con pool[0]
            Assert.AreEqual(GamePhase.AwaitingTurnStart, e.Phase);  // turno pasó al oponente
            Assert.AreEqual(1, e.ActiveIndex);
        }

        [Test]
        public void DiscardCard_ReplacesAndPassesTurn()
        {
            var e = NewEngine();
            e.BeginTurn();
            Assert.AreEqual(ActionResult.Success, e.DiscardCard(2));
            Assert.AreEqual("piquetero", e.PlayerAt(0).hand[2].id);
            Assert.AreEqual(1, e.ActiveIndex);
        }

        // ── Status temporizados (el corazón del timing) ──────────────────────────

        [Test]
        public void CorteDeRuta_SkipsOpponentProductionNextTurn()
        {
            var e = NewEngine();
            e.BeginTurn();                       // player0
            e.PlayerAt(0).hand[0] = Man("corte_ruta");  // ApplyStatus Skip al oponente
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));

            var p1 = e.PlayerAt(1);
            Assert.AreEqual(1, p1.activeStatuses.Count);

            e.BeginTurn();                       // player1: status dispara, producción omitida
            Assert.AreEqual(3, p1.dinero);       // sigue en inicial, NO 3+3
            Assert.AreEqual(2, p1.fuerza);
            Assert.AreEqual(1, p1.social);
            Assert.IsEmpty(p1.activeStatuses);   // status consumido
        }

        [Test]
        public void AsambleaPopular_DoublesOwnNextProduction()
        {
            var e = NewEngine();
            e.BeginTurn();                       // player0: dinero 6 (3+3)
            var p0 = e.PlayerAt(0);
            p0.social += 6;                      // alcanza para asamblea_popular (6 social)
            p0.hand[0] = Man("asamblea_popular");
            Assert.AreEqual(ActionResult.Success, e.PlayCard(0));

            e.BeginTurn();                       // player1
            e.DiscardCard(0);

            e.BeginTurn();                       // player0: DoubleProduction dispara
            Assert.AreEqual(6 + 3 * 2, p0.dinero);   // producción base (3) duplicada
        }

        // ── Victoria ─────────────────────────────────────────────────────────────

        [Test]
        public void Victory_KO_OnLethalDamage()
        {
            var e = NewEngine();
            e.BeginTurn();
            e.PlayerAt(1).hp = 10;
            e.PlayerAt(0).hand[0] = Man("paro_general");  // -14
            e.PlayCard(0);

            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
            Assert.AreEqual(WinCondition.KO, e.Outcome.Value.Condition);
            Assert.AreEqual(GamePhase.Finished, e.Phase);
        }

        [Test]
        public void Victory_HegemoniaSocial_AtThreshold()
        {
            var e = NewEngine();
            e.BeginTurn();
            var p0 = e.PlayerAt(0);
            p0.social = 69;
            p0.hand[0] = Man("viral_redes");  // +7 social
            e.PlayCard(0);

            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
            Assert.AreEqual(WinCondition.HegemoniaSocial, e.Outcome.Value.Condition);
        }

        [Test]
        public void Victory_PoderEconomico_AtThreshold()
        {
            // Con el cap subido a 100, el Dinero puede alcanzar el umbral económico.
            var e = NewEngine();
            e.BeginTurn();
            var p0 = e.PlayerAt(0);
            p0.dinero = 95;
            p0.hand[0] = Man("colecta");  // +6 → recortado a 100 ≥ 100

            e.PlayCard(0);

            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(WinCondition.PoderEconomico, e.Outcome.Value.Condition);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);
        }

        // ── Recursos: piso y techo ────────────────────────────────────────────────

        [Test]
        public void Resources_FloorAtZero()
        {
            var e = NewEngine();
            e.BeginTurn();
            e.PlayerAt(1).dinero = 1;
            e.PlayerAt(0).hand[0] = Man("saqueo");  // oponente -3 dinero
            e.PlayCard(0);

            Assert.AreEqual(0, e.PlayerAt(1).dinero);  // no baja de 0
        }

        [Test]
        public void Resources_CapAtMax()
        {
            // Fuerza no tiene umbral de victoria, así que sirve para observar el tope sin terminar la partida.
            var e = NewEngine();
            e.BeginTurn();
            var p0 = e.PlayerAt(0);
            p0.fuerza = 98;
            p0.hand[0] = Man("fernet");  // +3 fuerza → recortado a 100
            e.PlayCard(0);

            Assert.AreEqual(100, p0.fuerza);
        }

        // ── Unidades ─────────────────────────────────────────────────────────────

        [Test]
        public void RemoveUnit_DecrementsAndClearsAtZero()
        {
            var e = NewEngine();
            e.BeginTurn();
            var p1 = e.PlayerAt(1);
            p1.unitSlots.Add(new UnitSlot(Pol("patrullero"), 1));
            e.PlayerAt(0).hand[0] = Man("romper_marcha");  // RemoveUnit -1

            Assert.AreEqual(ActionResult.Success, e.PlayCard(0, removeTargetSlot: 0));
            Assert.AreEqual(0, p1.unitSlots.Count);  // contador 1→0 → slot liberado
        }

        [Test]
        public void RemoveUnit_RequiresTarget_WhenOpponentHasUnits()
        {
            var e = NewEngine();
            e.BeginTurn();
            e.PlayerAt(1).unitSlots.Add(new UnitSlot(Pol("patrullero"), 2));
            var p0 = e.PlayerAt(0);
            p0.hand[0] = Man("romper_marcha");
            int fuerzaBefore = p0.fuerza;

            Assert.AreEqual(ActionResult.NeedsRemoveTarget, e.PlayCard(0));  // sin target
            Assert.AreEqual(fuerzaBefore, p0.fuerza);  // no se cobró el costo
        }

        [Test]
        public void DeployUnit_StacksUpToMax()
        {
            var e = NewEngine();
            var piquetero = Man("piquetero");

            for (int i = 0; i < 6; i++)  // 6 despliegues, tope en x5
            {
                e.BeginTurn();
                var p0 = e.PlayerAt(0);
                p0.dinero = 50; p0.fuerza = 50; p0.social = 50;
                p0.hand[0] = piquetero;
                Assert.AreEqual(ActionResult.Success, e.PlayCard(0));
                // turno del oponente
                e.BeginTurn();
                e.DiscardCard(0);
            }

            var slot = e.PlayerAt(0).SlotFor("piquetero");
            Assert.IsNotNull(slot);
            Assert.AreEqual(5, slot.count);
        }

        [Test]
        public void DeployUnit_FullSlots_NeedsChoiceThenReplaces()
        {
            var e = NewEngine();
            e.BeginTurn();
            var p0 = e.PlayerAt(0);
            p0.unitSlots.Add(new UnitSlot(Man("piquetero"), 1));
            p0.unitSlots.Add(new UnitSlot(Man("jubilado"), 1));
            p0.unitSlots.Add(new UnitSlot(Man("olla_popular"), 1));
            p0.dinero = 50; p0.fuerza = 50; p0.social = 50;
            p0.hand[0] = Man("tuitero");  // 4ta unidad nueva

            Assert.AreEqual(ActionResult.NeedsUnitSlotChoice, e.PlayCard(0));
            Assert.AreEqual(50, p0.dinero);  // no se cobró

            Assert.AreEqual(ActionResult.Success, e.PlayCard(0, unitSlotToReplace: 0));
            Assert.AreEqual(3, p0.unitSlots.Count);
            Assert.AreEqual("tuitero", p0.unitSlots[0].unitData.id);
            Assert.AreEqual(1, p0.unitSlots[0].count);
        }

        // ── Terminación garantizada ────────────────────────────────────────────────

        [Test]
        public void SuddenDeath_StartsAndEscalates()
        {
            var e = NewEngine(new GameConfig { suddenDeathStart = 2 });

            e.BeginTurn(); e.DiscardCard(0);   // turno 1: sin daño
            Assert.AreEqual(100, e.PlayerAt(0).hp);

            e.BeginTurn(); e.DiscardCard(0);   // turno 2: daño 1 a ambos
            Assert.AreEqual(99, e.PlayerAt(0).hp);
            Assert.AreEqual(99, e.PlayerAt(1).hp);

            e.BeginTurn(); e.DiscardCard(0);   // turno 3: daño 2 a ambos
            Assert.AreEqual(97, e.PlayerAt(0).hp);
            Assert.AreEqual(97, e.PlayerAt(1).hp);
        }

        [Test]
        public void Timeout_HigherHpWins()
        {
            var e = NewEngine(new GameConfig { maxTurns = 2, suddenDeathStart = 1000 });
            e.PlayerAt(1).hp = 50;

            e.BeginTurn(); e.DiscardCard(0);   // turno 1
            e.BeginTurn(); e.DiscardCard(0);   // turno 2 → timeout

            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(WinCondition.Timeout, e.Outcome.Value.Condition);
            Assert.AreEqual(Faction.Manifestantes, e.Outcome.Value.Winner);  // 100 > 50
        }

        [Test]
        public void Timeout_NonFirstPlayerWinsOnFullTie()
        {
            var e = NewEngine(new GameConfig { maxTurns = 2, suddenDeathStart = 1000 }, coin: 0);

            e.BeginTurn(); e.DiscardCard(0);   // turno 1 (player0)
            e.BeginTurn(); e.DiscardCard(0);   // turno 2 (player1) → timeout, empate total

            Assert.IsTrue(e.IsFinished);
            Assert.AreEqual(WinCondition.Timeout, e.Outcome.Value.Condition);
            Assert.AreEqual(Faction.Policias, e.Outcome.Value.Winner);  // gana el no-primero
        }
    }
}
