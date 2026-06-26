using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>Estado de una run (spec §17.1): en curso, ganada (se venció al jefe) o perdida
    /// (permadeath: se perdió un combate).</summary>
    public enum RunStatus
    {
        InProgress,
        Won,
        Lost
    }

    /// <summary>
    /// Estado persistente de una run roguelike-deckbuilder (spec §17.5). Datos puros, sin escena:
    /// el mapa de puntos, dónde está el jugador, el mazo que evoluciona y el desenlace. La lógica
    /// (avanzar, resolver combate, recompensas) vive en <see cref="RunManager"/>.
    /// </summary>
    public sealed class RunState
    {
        /// <summary>Mapa de puntos a elección (spec §17.1). Inmutable durante la run.</summary>
        public readonly RunMap map;

        /// <summary>Facción del jugador HUMANO. La IA toma la otra (spec §11.2/§17.5).</summary>
        public readonly Faction faction;

        /// <summary>Punto donde está el jugador. Arranca en el inicio; avanza al ganar un combate.</summary>
        public int currentNodeId;

        public RunStatus status = RunStatus.InProgress;

        /// <summary>
        /// Mazo persistente de la run (spec §17.2/§17.3): arranca como el starter de la facción y
        /// crece con las recompensas (1-de-3). Se inyecta en el motor al iniciar cada combate
        /// (<see cref="GameEngine.StartGame(PlayerSetup,PlayerSetup,int)"/>), en vez de derivarlo
        /// de la facción.
        /// </summary>
        public readonly List<CardData> deck = new List<CardData>();

        /// <summary>Combates ya ganados (ids de nodo). Para presentación y para la una-sola-pasada
        /// (spec §17.1): el camino recorrido.</summary>
        public readonly HashSet<int> clearedNodeIds = new HashSet<int>();

        // ── [EXTENSIÓN, spec §17.4] Reliquias ────────────────────────────────────
        // Las reliquias persistentes de la run van acá como List<RelicData> (lista, no campos
        // sueltos — [[feedback-buenas-practicas]]) cuando se implementen. La capa de run las
        // traducirá a bonos de PlayerSetup del humano (mismo seam que el handicap de la IA), sin
        // tocar el motor. Diferidas a propósito (decisión 2026-06-26): este MVP no las usa.

        public RunState(RunMap map, Faction faction)
        {
            this.map = map;
            this.faction = faction;
            currentNodeId = map.startNodeId;
        }

        public MapNode CurrentNode => map.NodeById(currentNodeId);
        public bool IsCleared(int nodeId) => clearedNodeIds.Contains(nodeId);

        /// <summary>Dificultad del punto actual = su distancia al inicio (spec §17.1).</summary>
        public int CurrentDifficulty => map.DistanceOf(currentNodeId);
    }
}
