using System.Collections.Generic;
using NUnit.Framework;
using PiqueteDefend.Core;

namespace PiqueteDefend.Tests
{
    /// <summary>
    /// Tests del <see cref="HeuristicAiController"/> (IA del single-player, spec §16/§17). Verifican
    /// que la IA juega una partida COMPLETA contra sí misma con el catálogo real (CardLibrary), de
    /// forma legal, determinista y terminando dentro del límite de turnos.
    /// </summary>
    public class HeuristicAiTests
    {
        /// <summary>Catálogo real (CardLibrary) en memoria: pool, mazo por drawWeight, unidades iniciales.</summary>
        private sealed class LibraryCatalog : ICardCatalog
        {
            private readonly Dictionary<Faction, List<CardData>> _pools = new Dictionary<Faction, List<CardData>>();

            public LibraryCatalog()
            {
                _pools[Faction.Manifestantes] = CardLibrary.BuildManifestantes();
                _pools[Faction.Policias] = CardLibrary.BuildPolicias();
            }

            public IReadOnlyList<CardData> GetPool(Faction f) => _pools[f];

            public IReadOnlyList<CardData> GetDeckList(Faction f)
            {
                var deck = new List<CardData>();
                foreach (CardData c in _pools[f])
                {
                    int copies = c.drawWeight > 0 ? c.drawWeight : 1;
                    for (int i = 0; i < copies; i++) deck.Add(c);
                }
                return deck;
            }

            public IReadOnlyList<UnitCardData> GetStartingUnits(Faction f)
            {
                var units = new List<UnitCardData>();
                foreach (string id in CardLibrary.StartingUnitIds(f))
                    foreach (CardData c in _pools[f])
                        if (c.id == id && c is UnitCardData u) { units.Add(u); break; }
                return units;
            }
        }

        /// <summary>Juega una partida entera IA vs IA (misma política) con una seed fija.</summary>
        private static GameEngine PlayAiGame(int seed)
        {
            var engine = new GameEngine(new GameConfig(), new SystemRandomProvider(seed), new LibraryCatalog());
            engine.StartGame(Faction.Manifestantes, Faction.Policias, firstIndex: seed % 2);
            var ai = new HeuristicAiController();

            int guard = 0;
            while (!engine.IsFinished && guard < 2000)
            {
                engine.BeginTurn();
                if (engine.IsFinished) break;

                int steps = 0;
                while (!engine.IsFinished && steps < 100)
                {
                    PlannedAction action = ai.NextAction(engine);
                    if (action.IsEndTurn) break;
                    ActionResult r = action.Execute(engine);
                    Assert.AreEqual(ActionResult.Success, r,
                        "la IA no debería proponer acciones ilegales");
                    steps++;
                }
                Assert.Less(steps, 100, "el turno de la IA no debería trabarse en bucle");

                if (engine.IsFinished) break;
                engine.EndTurn();
                guard++;
            }
            return engine;
        }

        [Test]
        public void Ai_PlaysFullGame_Legally_AndFinishes()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                GameEngine e = PlayAiGame(seed);
                Assert.IsTrue(e.IsFinished, $"seed {seed}: la partida debería terminar dentro del límite");
                Assert.Greater(e.HalfTurn, 1, $"seed {seed}: la IA debería jugar varios turnos");
            }
        }

        [Test]
        public void Ai_IsDeterministic_SameSeedSameOutcome()
        {
            GameEngine a = PlayAiGame(42);
            GameEngine b = PlayAiGame(42);
            Assert.AreEqual(a.Outcome.HasValue, b.Outcome.HasValue);
            Assert.AreEqual(a.Outcome.Value.Winner, b.Outcome.Value.Winner, "misma seed → mismo ganador");
            Assert.AreEqual(a.HalfTurn, b.HalfTurn, "misma seed → misma duración");
        }
    }
}
