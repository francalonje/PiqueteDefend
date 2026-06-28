using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Tipo de punto del mapa de la run (spec §17.1). El tipo es <b>data</b>, extensible sin tocar
    /// la estructura del grafo.
    /// </summary>
    public enum MapNodeType
    {
        /// <summary>Punto de partida. No tiene combate: es donde arranca el jugador (spec §17.1).</summary>
        Start,

        /// <summary>Combate normal contra la IA. La dificultad la da la distancia al inicio (spec §17.1).</summary>
        Combat,

        /// <summary>Combate de élite (spec §17.6): arquetipo más duro, mejor paga (oro/recompensa).
        /// Usa el mismo camino de combate que <see cref="Combat"/>.</summary>
        Elite,

        /// <summary>Combate final. Ganarlo gana el acto/run (spec §17.1/§17.6).</summary>
        Boss,

        /// <summary>Tienda (spec §17.6): gastar oro en cartas/reliquias/remoción. No es combate.</summary>
        Shop,

        /// <summary>Evento narrativo con decisión (spec §17.6). No es combate.</summary>
        Event,

        /// <summary>Taller de mazo (spec §17.6): mejorar (upgrade) o quitar cartas. No es combate.</summary>
        Workshop,

        /// <summary>Tesoro (spec §17.6): regala oro o una reliquia, sin combate.</summary>
        Treasure,

        /// <summary>Nodo misterioso (spec §17.6): resultado oculto hasta llegar; se resuelve a otro tipo.</summary>
        Mystery
    }

    /// <summary>
    /// Un punto del mapa (spec §17.1). Datos puros: el nodo conoce su tipo, sus salidas
    /// (<see cref="connections"/>) y un flavor temático. La <b>dificultad NO se guarda</b>: se deriva
    /// de la distancia al inicio (<see cref="RunMap.DistanceOf"/>), así las conexiones son la única
    /// fuente de verdad de la topología.
    /// </summary>
    public sealed class MapNode
    {
        public readonly int id;
        public readonly MapNodeType type;

        /// <summary>Nombre temático del punto (flavor argento). Sólo presentación.</summary>
        public readonly string title;

        /// <summary>Ids de los puntos a los que se puede AVANZAR desde acá (aristas dirigidas
        /// "hacia adelante"). La elección de a cuál ir es del jugador; las otras salidas quedan
        /// salteadas (una sola pasada, spec §17.1).</summary>
        public readonly List<int> connections = new List<int>();

        /// <summary>Posición en el diorama (0..1). Metadata <b>inerte de presentación</b>: el núcleo
        /// nunca la lee, sólo la transporta para que Fase 4 ubique el punto en el mapa.</summary>
        public readonly float x;
        public readonly float y;

        /// <summary>Combinaciones (líneas que cruzan en esta estación, ej. ["H"] o ["D","E"]). Metadata
        /// <b>inerte de presentación</b>: el núcleo no la lee; la UI dibuja el badge de combinación.
        /// Hoy es decoración (la decisión de ruta es cuántas paradas avanzar); el fork/desvío por
        /// combinación es candidato de playtest.</summary>
        public readonly IReadOnlyList<string> combinations;

        /// <summary>Clave de fondo específico de la parada (ej. "estacion-congreso"). Metadata
        /// <b>inerte de presentación</b>: extension point para que cada parada tenga su background
        /// (mapa y/o combate de esa parada). <c>null</c>/"" = fondo por defecto.</summary>
        public readonly string backgroundKey;

        public MapNode(int id, MapNodeType type, string title, float x = 0f, float y = 0f,
                       IReadOnlyList<string> combinations = null, string backgroundKey = null)
        {
            this.id = id;
            this.type = type;
            this.title = title;
            this.x = x;
            this.y = y;
            this.combinations = combinations ?? System.Array.Empty<string>();
            this.backgroundKey = backgroundKey;
        }

        /// <summary>Agrega salidas (encadenable, para autorar el mapa de forma legible).</summary>
        public MapNode ConnectTo(params int[] targets)
        {
            connections.AddRange(targets);
            return this;
        }
    }

    /// <summary>
    /// Mapa de la run como grafo dirigido de puntos a elección (spec §17.1): NO carriles. Desde el
    /// punto actual se avanza a uno de sus sucesores; la ruta no tomada queda atrás (una sola pasada).
    /// La <b>dificultad = distancia al inicio</b> (cantidad de saltos por el camino), derivada con BFS.
    /// Es <b>data</b>, autorable sin tocar el motor.
    /// </summary>
    public sealed class RunMap
    {
        public readonly int startNodeId;

        /// <summary>Nombre de la línea/acto (ej. "Línea A"). Metadata <b>inerte de presentación</b>:
        /// el núcleo no la lee; la UI titula y themea el mapa. Escala a multi-acto vía
        /// <see cref="RunState.actIndex"/>.</summary>
        public readonly string lineName;

        /// <summary>Color de la línea en hex (ej. "#1CA9C9" celeste para la A). Metadata <b>inerte de
        /// presentación</b>: la UI lo usa para la barra/anillos de estación. <c>null</c> = color por
        /// defecto de la UI.</summary>
        public readonly string lineColorHex;

        private readonly List<MapNode> _nodes;
        private readonly Dictionary<int, MapNode> _byId;
        private readonly Dictionary<int, int> _distance;
        private readonly int _bossNodeId;

        public RunMap(IReadOnlyList<MapNode> nodes, int startNodeId,
                      string lineName = null, string lineColorHex = null)
        {
            this.lineName = lineName;
            this.lineColorHex = lineColorHex;

            _nodes = new List<MapNode>(nodes);
            _byId = new Dictionary<int, MapNode>(_nodes.Count);
            foreach (MapNode n in _nodes)
            {
                if (_byId.ContainsKey(n.id))
                    throw new System.ArgumentException($"Id de nodo duplicado: {n.id}.", nameof(nodes));
                _byId[n.id] = n;
            }

            if (!_byId.ContainsKey(startNodeId))
                throw new System.ArgumentException($"startNodeId {startNodeId} no existe.", nameof(startNodeId));
            this.startNodeId = startNodeId;

            // Validación de aristas: toda conexión debe apuntar a un nodo existente.
            foreach (MapNode n in _nodes)
                foreach (int c in n.connections)
                    if (!_byId.ContainsKey(c))
                        throw new System.ArgumentException($"El nodo {n.id} conecta a {c}, que no existe.", nameof(nodes));

            _distance = ComputeDistances();

            // El jefe (si lo hay): el primer nodo de tipo Boss. Es el objetivo de la run (spec §17.1).
            _bossNodeId = -1;
            foreach (MapNode n in _nodes)
                if (n.type == MapNodeType.Boss) { _bossNodeId = n.id; break; }
        }

        public IReadOnlyList<MapNode> Nodes => _nodes;

        public MapNode NodeById(int id) =>
            _byId.TryGetValue(id, out MapNode n) ? n
            : throw new System.ArgumentException($"No existe el nodo {id}.", nameof(id));

        public bool HasNode(int id) => _byId.ContainsKey(id);

        public MapNode StartNode => _byId[startNodeId];

        /// <summary>Id del nodo jefe, o -1 si el mapa no tiene jefe.</summary>
        public int BossNodeId => _bossNodeId;

        /// <summary>Puntos a los que se puede avanzar desde <paramref name="nodeId"/>.</summary>
        public IReadOnlyList<MapNode> Successors(int nodeId)
        {
            MapNode node = NodeById(nodeId);
            var result = new List<MapNode>(node.connections.Count);
            foreach (int c in node.connections) result.Add(_byId[c]);
            return result;
        }

        /// <summary>Distancia (saltos) al inicio = dial de dificultad (spec §17.1). El inicio es 0;
        /// nodos inalcanzables dan -1.</summary>
        public int DistanceOf(int nodeId) =>
            _distance.TryGetValue(nodeId, out int d) ? d : -1;

        private Dictionary<int, int> ComputeDistances()
        {
            var dist = new Dictionary<int, int> { [startNodeId] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(startNodeId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int next in _byId[cur].connections)
                {
                    if (!dist.ContainsKey(next))
                    {
                        dist[next] = dist[cur] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return dist;
        }
    }
}
