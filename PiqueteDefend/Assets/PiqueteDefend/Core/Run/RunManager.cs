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

        /// <summary>Oro otorgado al ganar un combate normal (spec §17.6).</summary>
        public int combatGoldReward = 10;

        /// <summary>Oro otorgado al ganar un combate de élite (más duro, mejor paga, spec §17.6).</summary>
        public int eliteGoldReward = 20;

        /// <summary>Oro otorgado por un nodo de tesoro (spec §17.6). En §17.6 un tesoro puede dar
        /// reliquia en vez de oro; este es el monto cuando da oro.</summary>
        public int treasureGoldReward = 15;

        /// <summary>Tamaño mínimo del mazo de la run: el taller (spec §17.6) no deja quitar cartas por
        /// debajo de esto, para no romper el robo.</summary>
        public int minDeckSize = 5;
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
        private readonly IReadOnlyList<EncounterDefinition> _encounters;

        private int _pendingNodeId = -1;          // combate en curso (entre BeginCombat y ResolveCombat)
        private GameEngine _combat;                // el motor del combate en curso
        private List<CardData> _pendingReward;     // recompensa sin resolver (Choose/Skip)
        private int _pendingWorkshopNodeId = -1;   // taller abierto (entre EnterWorkshop y Leave/Remove)

        public RunState State { get; }

        public RunManager(ICardCatalog catalog, GameConfig config, IRandomProvider rng,
                          Faction humanFaction, RunMap map = null, RunConfig runConfig = null,
                          IReadOnlyList<EncounterDefinition> encounters = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _runConfig = runConfig ?? new RunConfig();
            _encounters = encounters;   // null = sin arquetipos curados → fallback al mazo default opuesto

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
            if (State.status != RunStatus.InProgress || _pendingReward != null || WorkshopInProgress)
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
            if (!IsCombatNode(node.type))
                throw new InvalidOperationException($"El punto {nodeId} ({node.type}) no es un combate.");

            int difficulty = State.map.DistanceOf(nodeId);
            bool boss = node.type == MapNodeType.Boss;

            // Humano: mazo de la run inyectado (spec §17.2). El motor copia la lista, no la muta.
            var human = new PlayerSetup(State.faction) { deck = State.deck };

            // Handicap por distancia (spec §17.1): +recursos iniciales a la IA, escala con la profundidad.
            int bonus = Math.Max(0, difficulty) * _runConfig.aiResourceBonusPerLevel;

            // La IA sale de un arquetipo curado del pool (spec §17.6); sin pool, fallback al default opuesto.
            EncounterDefinition enc = PickEncounter(difficulty, boss);
            PlayerSetup ai;
            if (enc != null)
            {
                State.usedEncounterIds.Add(enc.id);
                ai = new PlayerSetup(enc.faction)
                {
                    deck = enc.deck != null && enc.deck.Count > 0 ? enc.deck : null,
                    startingUnits = enc.startingUnits != null && enc.startingUnits.Count > 0 ? enc.startingUnits : null,
                    bonusDinero = bonus + enc.bonusDinero,
                    bonusFuerza = bonus + enc.bonusFuerza,
                    bonusSocial = bonus + enc.bonusSocial,
                    extraStartingUnits = boss && enc.leaderUnit != null ? new[] { enc.leaderUnit } : null,
                    initialStatuses = enc.aiInitialStatuses != null && enc.aiInitialStatuses.Count > 0
                        ? enc.aiInitialStatuses : null,
                };
                // Pasiva de jefe que castiga al jugador (spec §17.6): estados sembrados en el humano.
                if (enc.playerInitialStatuses != null && enc.playerInitialStatuses.Count > 0)
                    human.initialStatuses = enc.playerInitialStatuses;
            }
            else
            {
                ai = new PlayerSetup(AiFaction)
                {
                    bonusDinero = bonus,
                    bonusFuerza = bonus,
                    bonusSocial = bonus,
                    extraStartingUnits = boss ? BossExtraUnits() : null
                };
            }

            // Reliquias de la run (spec §17.4/§17.6): bonos al humano, mismo seam que el handicap.
            ApplyRelics(human);

            var setups = new PlayerSetup[2];
            setups[HumanIndex] = human;
            setups[AiIndex] = ai;

            _combat = new GameEngine(_config, _rng, _catalog);
            _combat.StartGame(setups[0], setups[1], firstIndex);
            _pendingNodeId = nodeId;
            return _combat;
        }

        /// <summary>
        /// Sortea un arquetipo de enemigo del pool (spec §17.6): del tipo correcto (jefe vs combate),
        /// de la facción de la IA y no usado en la run. Prefiere el tier de dificultad del nodo; si el
        /// pool se agotó (todos usados), permite repetir. Devuelve <c>null</c> si no hay pool o ningún
        /// candidato — el llamador cae al mazo default opuesto.
        /// </summary>
        private EncounterDefinition PickEncounter(int difficulty, bool boss)
        {
            if (_encounters == null || _encounters.Count == 0) return null;

            var candidates = new List<EncounterDefinition>();
            foreach (EncounterDefinition e in _encounters)
                if (e != null && e.isBoss == boss && e.faction == AiFaction && !State.usedEncounterIds.Contains(e.id))
                    candidates.Add(e);

            // Pool agotado: permitir repetir antes que quedarse sin enemigo.
            if (candidates.Count == 0)
                foreach (EncounterDefinition e in _encounters)
                    if (e != null && e.isBoss == boss && e.faction == AiFaction)
                        candidates.Add(e);

            if (candidates.Count == 0) return null;

            // Preferir el tier exacto del nodo; si no hay, cualquiera del tipo.
            var tier = new List<EncounterDefinition>();
            foreach (EncounterDefinition e in candidates)
                if (e.difficulty == difficulty) tier.Add(e);
            List<EncounterDefinition> bag = tier.Count > 0 ? tier : candidates;

            return bag[_rng.Next(bag.Count)];
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
        /// Vuelca las reliquias de la run (spec §17.4/§17.6) sobre el <see cref="PlayerSetup"/> del
        /// humano: recursos extra, unidad(es) inicial(es) extra y estados sembrados. Acumula sobre lo
        /// que ya traiga el setup (ej. una pasiva de jefe que afecta al humano), sin pisarlo.
        /// </summary>
        private void ApplyRelics(PlayerSetup human)
        {
            if (State.relics == null || State.relics.Count == 0) return;

            List<UnitCardData> extraUnits = null;
            List<StatusEffect> statuses = null;

            foreach (RelicData r in State.relics)
            {
                if (r == null) continue;
                switch (r.kind)
                {
                    case RelicEffectKind.BonusResource:
                        if (r.resource == ResourceType.Dinero) human.bonusDinero += r.value;
                        else if (r.resource == ResourceType.Fuerza) human.bonusFuerza += r.value;
                        else if (r.resource == ResourceType.Social) human.bonusSocial += r.value;
                        break;

                    case RelicEffectKind.ExtraStartingUnit:
                        if (r.unit != null)
                        {
                            if (extraUnits == null) extraUnits = new List<UnitCardData>();
                            extraUnits.Add(r.unit);
                        }
                        break;

                    case RelicEffectKind.InitialStatus:
                        if (r.status != null)
                        {
                            if (statuses == null) statuses = new List<StatusEffect>();
                            statuses.Add(r.status);
                        }
                        break;
                }
            }

            if (extraUnits != null)
            {
                if (human.extraStartingUnits != null) extraUnits.InsertRange(0, human.extraStartingUnits);
                human.extraStartingUnits = extraUnits;
            }
            if (statuses != null)
            {
                if (human.initialStatuses != null) statuses.InsertRange(0, human.initialStatuses);
                human.initialStatuses = statuses;
            }
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

            AdvanceTo(node);

            if (node.type == MapNodeType.Boss)
            {
                // [SEAM multi-acto, spec §17.6] Con varios actos: si NO es el último, acá iría
                // State.actIndex++ y cargar el mapa del próximo acto en vez de ganar. Hoy hay un acto.
                State.status = RunStatus.Won;     // se llegó al extremo del mapa (spec §17.1)
                return RewardOffer.None;
            }

            // Oro por ganar el combate (spec §17.6) — la élite paga más. Economía meta para la tienda.
            State.gold += node.type == MapNodeType.Elite ? _runConfig.eliteGoldReward : _runConfig.combatGoldReward;

            _pendingReward = GenerateReward();
            return new RewardOffer(_pendingReward);
        }

        // ── Nodos no-combate (spec §17.6) ───────────────────────────────────────────

        /// <summary>
        /// Resuelve un nodo de <see cref="MapNodeType.Treasure"/>: otorga oro (en §17.6 también puede
        /// dar reliquia, ver paso 5) y avanza. Atómico: no deja interacción pendiente.
        /// </summary>
        public void EnterTreasure(int nodeId)
        {
            if (State.status != RunStatus.InProgress)
                throw new InvalidOperationException($"La run no está en curso ({State.status}).");
            if (_pendingReward != null)
                throw new InvalidOperationException("Hay una recompensa sin resolver.");
            if (CombatInProgress)
                throw new InvalidOperationException("Hay un combate en curso.");
            if (!IsAvailable(nodeId))
                throw new InvalidOperationException($"El punto {nodeId} no es alcanzable desde {State.currentNodeId}.");

            MapNode node = State.map.NodeById(nodeId);
            if (node.type != MapNodeType.Treasure)
                throw new InvalidOperationException($"El punto {nodeId} ({node.type}) no es un tesoro.");

            State.gold += _runConfig.treasureGoldReward;
            AdvanceTo(node);
        }

        // ── Taller de mazo (spec §17.6): remoción de cartas ─────────────────────────

        /// <summary>True si hay un taller abierto (bloquea la navegación, como la recompensa).</summary>
        public bool WorkshopInProgress => _pendingWorkshopNodeId >= 0;

        /// <summary>Abre el taller del nodo (spec §17.6): hasta cerrarlo (quitar una carta o salir) no
        /// se puede navegar. No es combate ni deja recompensa pendiente.</summary>
        public void EnterWorkshop(int nodeId)
        {
            if (State.status != RunStatus.InProgress)
                throw new InvalidOperationException($"La run no está en curso ({State.status}).");
            if (_pendingReward != null)
                throw new InvalidOperationException("Hay una recompensa sin resolver.");
            if (CombatInProgress)
                throw new InvalidOperationException("Hay un combate en curso.");
            if (WorkshopInProgress)
                throw new InvalidOperationException("Ya hay un taller abierto.");
            if (!IsAvailable(nodeId))
                throw new InvalidOperationException($"El punto {nodeId} no es alcanzable desde {State.currentNodeId}.");

            MapNode node = State.map.NodeById(nodeId);
            if (node.type != MapNodeType.Workshop)
                throw new InvalidOperationException($"El punto {nodeId} ({node.type}) no es un taller.");

            _pendingWorkshopNodeId = nodeId;
        }

        /// <summary>Cartas del mazo que se pueden quitar en el taller abierto.</summary>
        public IReadOnlyList<CardData> WorkshopCards => State.deck;

        /// <summary>True si todavía se puede quitar una carta (el mazo está por encima del mínimo).</summary>
        public bool CanRemoveCard => WorkshopInProgress && State.deck.Count > _runConfig.minDeckSize;

        /// <summary>Quita la carta del mazo y cierra el taller (avanza). Una remoción por taller.</summary>
        public void RemoveCardAndLeave(CardData card)
        {
            if (!WorkshopInProgress)
                throw new InvalidOperationException("No hay taller abierto.");
            if (card == null || !State.deck.Contains(card))
                throw new ArgumentException("La carta no está en el mazo de la run.", nameof(card));
            if (State.deck.Count <= _runConfig.minDeckSize)
                throw new InvalidOperationException($"El mazo está en el mínimo ({_runConfig.minDeckSize}): no se puede quitar.");

            State.deck.Remove(card);
            CloseWorkshop();
        }

        /// <summary>Sale del taller sin quitar nada (avanza).</summary>
        public void LeaveWorkshop()
        {
            if (!WorkshopInProgress)
                throw new InvalidOperationException("No hay taller abierto.");
            CloseWorkshop();
        }

        private void CloseWorkshop()
        {
            MapNode node = State.map.NodeById(_pendingWorkshopNodeId);
            _pendingWorkshopNodeId = -1;
            AdvanceTo(node);
        }

        // ── Avance del mapa (compartido combate / no-combate, spec §17.1/§17.6) ──────

        /// <summary>Marca el punto como recorrido y mueve al jugador ahí (una sola pasada, spec §17.1).
        /// Punto único de avance: lo llaman tanto el combate ganado como los nodos no-combate.</summary>
        private void AdvanceTo(MapNode node)
        {
            State.clearedNodeIds.Add(node.id);
            State.currentNodeId = node.id;
        }

        /// <summary>True si el tipo de nodo se resuelve por combate (Combat/Elite/Boss).</summary>
        private static bool IsCombatNode(MapNodeType t) =>
            t == MapNodeType.Combat || t == MapNodeType.Elite || t == MapNodeType.Boss;

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
