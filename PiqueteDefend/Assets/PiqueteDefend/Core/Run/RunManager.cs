using System;
using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Parámetros de la run (spec §17.1). El handicap de dificultad escala con la distancia al
    /// inicio. <b>Rough, a iterar por playtest</b> ([[feedback-playtest-driven]]).
    /// </summary>
    public sealed class RunConfig
    {
        /// <summary>+N a cada recurso inicial de la IA por nivel de distancia (d1→+2, d2→+4, d3→+6).</summary>
        public int aiResourceBonusPerLevel = 2;

        /// <summary>Unidades iniciales EXTRA para la IA en el combate del jefe (toma las primeras de
        /// su lista de unidades iniciales).</summary>
        public int bossExtraStartingUnits = 1;

        /// <summary>Cartas ofrecidas como recompensa tras ganar un combate (1-de-N, spec §17.2).</summary>
        public int rewardCount = 3;
    }

    /// <summary>Oferta de recompensa tras ganar un combate (1-de-N, spec §17.2). Vacía si no hay
    /// recompensa (p. ej. al vencer al jefe: la run ya está ganada).</summary>
    public readonly struct RewardOffer
    {
        public readonly IReadOnlyList<CardData> cards;
        public RewardOffer(IReadOnlyList<CardData> cards) => this.cards = cards;
        public bool HasReward => cards != null && cards.Count > 0;
        public static RewardOffer None => new RewardOffer(null);
    }

    /// <summary>
    /// Orquesta una run (spec §17.5), C# puro y testeable sin escena. Mantiene el <see cref="RunState"/>
    /// y arma cada combate: inyecta el mazo de la run para el humano y aplica el handicap de dificultad
    /// a la IA. La presentación (Fase 4) corre el loop del <see cref="GameEngine"/> que devuelve
    /// <see cref="BeginCombat"/> (con delays y la IA vía <see cref="IPlayerController"/>) y, al terminar,
    /// llama a <see cref="ResolveCombat"/> con el resultado.
    ///
    /// <para>Lados fijos (decisión de proyecto): Manifestantes = índice 0, Policías = índice 1. El
    /// humano ocupa el índice de su facción; la IA, el otro.</para>
    /// </summary>
    public sealed class RunManager
    {
        private readonly ICardCatalog _catalog;
        private readonly GameConfig _config;
        private readonly IRandomProvider _rng;
        private readonly RunConfig _runConfig;

        private int _pendingNodeId = -1;          // combate en curso (entre BeginCombat y ResolveCombat)
        private GameEngine _combat;                // el motor del combate en curso
        private List<CardData> _pendingReward;     // recompensa sin resolver (Choose/Skip)

        public RunState State { get; }

        public RunManager(ICardCatalog catalog, GameConfig config, IRandomProvider rng,
                          Faction humanFaction, RunMap map = null, RunConfig runConfig = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _runConfig = runConfig ?? new RunConfig();

            State = new RunState(map ?? RunMapLibrary.BuildDefaultMap(), humanFaction);
            // Mazo starter = el default de la facción (spec §17.2). [EXTENSIÓN] armado pre-run.
            State.deck.AddRange(_catalog.GetDeckList(humanFaction));
        }

        // ── Lados (sides fijos) ──────────────────────────────────────────────────

        /// <summary>Índice del jugador humano según su facción (Manifestantes=0, Policías=1).</summary>
        public int HumanIndex => State.faction == Faction.Manifestantes ? 0 : 1;
        public int AiIndex => 1 - HumanIndex;
        public Faction AiFaction => State.faction == Faction.Manifestantes ? Faction.Policias : Faction.Manifestantes;

        // ── Navegación del mapa ───────────────────────────────────────────────────

        /// <summary>Puntos a los que se puede avanzar desde el actual (spec §17.1). Vacío si la run
        /// terminó o hay una recompensa sin resolver.</summary>
        public IReadOnlyList<MapNode> AvailableNodes()
        {
            if (State.status != RunStatus.InProgress || _pendingReward != null)
                return Array.Empty<MapNode>();
            return State.map.Successors(State.currentNodeId);
        }

        public bool IsAvailable(int nodeId)
        {
            foreach (MapNode n in AvailableNodes())
                if (n.id == nodeId) return true;
            return false;
        }

        // ── Combate ────────────────────────────────────────────────────────────────

        /// <summary>True si hay un combate iniciado pendiente de resolución.</summary>
        public bool CombatInProgress => _pendingNodeId >= 0;

        /// <summary>
        /// Arma e inicia el combate del punto <paramref name="nodeId"/> (debe ser un sucesor del
        /// actual). Inyecta el mazo de la run para el humano y el handicap por distancia para la IA.
        /// Devuelve el <see cref="GameEngine"/> ya en <c>AwaitingTurnStart</c>: el llamador corre el
        /// loop y luego invoca <see cref="ResolveCombat"/>.
        /// </summary>
        public GameEngine BeginCombat(int nodeId, int firstIndex = -1)
        {
            if (State.status != RunStatus.InProgress)
                throw new InvalidOperationException($"La run no está en curso ({State.status}).");
            if (_pendingReward != null)
                throw new InvalidOperationException("Hay una recompensa sin resolver: elegí o salteá antes de seguir.");
            if (CombatInProgress)
                throw new InvalidOperationException("Ya hay un combate en curso.");
            if (!IsAvailable(nodeId))
                throw new InvalidOperationException($"El punto {nodeId} no es alcanzable desde {State.currentNodeId}.");

            MapNode node = State.map.NodeById(nodeId);
            if (node.type != MapNodeType.Combat && node.type != MapNodeType.Boss)
                throw new InvalidOperationException($"El punto {nodeId} ({node.type}) no es un combate.");

            int difficulty = State.map.DistanceOf(nodeId);

            // Humano: mazo de la run inyectado (spec §17.2). El motor copia la lista, no la muta.
            var human = new PlayerSetup(State.faction) { deck = State.deck };

            // IA: handicap por distancia (spec §17.1) — +recursos iniciales, y +unidad(es) en el jefe.
            int bonus = Math.Max(0, difficulty) * _runConfig.aiResourceBonusPerLevel;
            var ai = new PlayerSetup(AiFaction)
            {
                bonusDinero = bonus,
                bonusFuerza = bonus,
                bonusSocial = bonus,
                extraStartingUnits = node.type == MapNodeType.Boss ? BossExtraUnits() : null
            };

            var setups = new PlayerSetup[2];
            setups[HumanIndex] = human;
            setups[AiIndex] = ai;

            _combat = new GameEngine(_config, _rng, _catalog);
            _combat.StartGame(setups[0], setups[1], firstIndex);
            _pendingNodeId = nodeId;
            return _combat;
        }

        /// <summary>Unidades iniciales extra para la IA en el jefe: las primeras de su lista del catálogo.</summary>
        private IReadOnlyList<UnitCardData> BossExtraUnits()
        {
            IReadOnlyList<UnitCardData> baseUnits = _catalog.GetStartingUnits(AiFaction);
            int n = Math.Min(_runConfig.bossExtraStartingUnits, baseUnits.Count);
            var extra = new List<UnitCardData>(n);
            for (int i = 0; i < n; i++) extra.Add(baseUnits[i]);
            return extra;
        }

        /// <summary>
        /// Aplica el resultado del combate en curso al estado de la run (spec §17.1/§17.2):
        /// <list type="bullet">
        /// <item>Gana el humano un combate normal → marca el punto, avanza, y ofrece recompensa (1-de-N).</item>
        /// <item>Gana el humano el jefe → run <see cref="RunStatus.Won"/> (sin recompensa).</item>
        /// <item>Pierde o empata → run <see cref="RunStatus.Lost"/> (permadeath).</item>
        /// </list>
        /// </summary>
        public RewardOffer ResolveCombat(GameOutcome outcome)
        {
            if (!CombatInProgress)
                throw new InvalidOperationException("No hay combate en curso para resolver.");

            MapNode node = State.map.NodeById(_pendingNodeId);
            bool humanWon = outcome.Winner.HasValue && outcome.Winner.Value == State.faction;

            _pendingNodeId = -1;
            _combat = null;

            if (!humanWon)
            {
                // Derrota o empate = fin de la run (permadeath, spec §17.1).
                State.status = RunStatus.Lost;
                return RewardOffer.None;
            }

            State.clearedNodeIds.Add(node.id);
            State.currentNodeId = node.id;

            if (node.type == MapNodeType.Boss)
            {
                State.status = RunStatus.Won;     // se llegó al extremo del mapa (spec §17.1)
                return RewardOffer.None;
            }

            _pendingReward = GenerateReward();
            return new RewardOffer(_pendingReward);
        }

        // ── Recompensa (1-de-N, spec §17.2) ────────────────────────────────────────

        /// <summary>La recompensa ofrecida sin resolver, o vacía si no hay ninguna.</summary>
        public RewardOffer PendingReward =>
            _pendingReward != null ? new RewardOffer(_pendingReward) : RewardOffer.None;

        /// <summary>Suma la carta elegida al mazo de la run (spec §17.2) y cierra la oferta.</summary>
        public void ChooseReward(CardData card)
        {
            if (_pendingReward == null)
                throw new InvalidOperationException("No hay recompensa pendiente.");
            if (!_pendingReward.Contains(card))
                throw new ArgumentException("La carta no está en la oferta de recompensa.", nameof(card));
            State.deck.Add(card);
            _pendingReward = null;
        }

        /// <summary>Descarta la recompensa sin sumar nada (StS permite saltearla).</summary>
        public void SkipReward()
        {
            if (_pendingReward == null)
                throw new InvalidOperationException("No hay recompensa pendiente.");
            _pendingReward = null;
        }

        /// <summary>Elige <see cref="RunConfig.rewardCount"/> cartas DISTINTAS del pool de la facción
        /// del humano (spec §17.2). Si el pool es más chico, ofrece todas.</summary>
        private List<CardData> GenerateReward()
        {
            var bag = new List<CardData>(_catalog.GetPool(State.faction));
            int take = Math.Min(_runConfig.rewardCount, bag.Count);
            var picked = new List<CardData>(take);
            for (int i = 0; i < take; i++)
            {
                int j = _rng.Next(bag.Count);
                picked.Add(bag[j]);
                bag.RemoveAt(j);
            }
            return picked;
        }
    }
}
