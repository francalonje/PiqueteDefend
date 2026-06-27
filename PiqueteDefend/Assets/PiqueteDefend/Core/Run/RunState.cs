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

        /// <summary>Acto actual (spec §17.6): el acto 1 (Línea A del subte) es 0. Seam de multi-acto:
        /// al vencer el jefe de un acto que NO es el último, en vez de ganar la run se incrementa
        /// <see cref="actIndex"/> y se carga el mapa del próximo acto (línea del subte). Hoy hay un solo
        /// acto, así que vencer al jefe gana la run.</summary>
        public int actIndex;

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

        /// <summary>Arquetipos de enemigo ya enfrentados (ids), para no repetir dentro de la run
        /// (spec §17.6). El pool los excluye al sortear el próximo combate.</summary>
        public readonly HashSet<string> usedEncounterIds = new HashSet<string>();

        /// <summary>Oro de la run (spec §17.6): economía meta — se gana en combates/eventos y se gasta
        /// en la tienda. NO es un <see cref="ResourceType"/> de combate; el motor no lo conoce.</summary>
        public int gold;

        /// <summary>Reliquias persistentes de la run (spec §17.4/§17.6): modificadores pasivos que
        /// duran toda la run. <see cref="RunManager"/> las traduce a bonos del <see cref="PlayerSetup"/>
        /// del humano al iniciar cada combate (mismo seam que el handicap de la IA), sin tocar el motor.</summary>
        public readonly List<RelicData> relics = new List<RelicData>();

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
