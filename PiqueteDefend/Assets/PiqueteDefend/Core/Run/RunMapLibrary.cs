using System.Collections.Generic;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Fuente de verdad programática de los mapas de run (espejo del patrón de <c>CardLibrary</c>).
    /// El mapa es <b>data</b>: esto sólo construye el grafo por defecto del MVP. Valores y forma son
    /// <b>rough, a iterar por playtest</b> (spec §17.1; [[feedback-playtest-driven]]).
    /// </summary>
    public static class RunMapLibrary
    {
        // Ids del mapa default (estables para tests y presentación).
        public const int StartId = 0;
        public const int BossId = 7;

        // Acto 1 = Línea A del subte. Ids estables para presentación/tests.
        public const int Acto1StartId = 0;
        /// <summary>Id del jefe del acto 1 = última parada (cabecera Plaza de Mayo).</summary>
        public static int Acto1BossId => LineaA.Length - 1;

        /// <summary>Color de la Línea A (celeste).</summary>
        private const string LineaAColorHex = "#1CA9C9";

        /// <summary>Descriptor de una estación del esqueleto: nombre + combinaciones (flavor). El tipo de
        /// nodo NO va acá: lo estampa el generador por run.</summary>
        private readonly struct Station
        {
            public readonly string title;
            public readonly string[] combinations;
            public Station(string title, string[] combinations = null)
            {
                this.title = title;
                this.combinations = combinations;
            }
        }

        /// <summary>Esqueleto de la Línea A para el acto 1 (subconjunto curado, oeste→este: Primera Junta
        /// → Plaza de Mayo). Geografía y combinaciones reales (H en Plaza Miserere, C en Lima, D/E en
        /// Perú). El orden y el subconjunto son <b>tunables por playtest</b> ([[feedback-playtest-driven]]).</summary>
        private static readonly Station[] LineaA =
        {
            new Station("Primera Junta"),
            new Station("Acoyte"),
            new Station("Río de Janeiro"),
            new Station("Castro Barros"),
            new Station("Loria"),
            new Station("Plaza Miserere", new[] { "H" }),
            new Station("Congreso"),
            new Station("Sáenz Peña"),
            new Station("Lima", new[] { "C" }),
            new Station("Perú", new[] { "D", "E" }),
            new Station("Plaza de Mayo"),
        };

        /// <summary>
        /// Mapa default: grafo de puntos a elección con bifurcaciones (no carriles, spec §17.1).
        /// 3 anillos (d1, d2, jefe d3); la dificultad sube con la distancia al inicio. Adyacencia:
        /// <code>
        ///   START(d0) → 1, 2, 3        (anillo 1, combate)
        ///   1(d1)     → 4, 5
        ///   2(d1)     → 4, 5, 6
        ///   3(d1)     → 5, 6
        ///   4,5,6(d2) → BOSS           (anillo 2, combate)
        ///   BOSS(d3)  → (final)        (Casa Rosada)
        /// </code>
        /// Una run recorre 3 combates (uno por anillo, jefe incluido). 2-3 puntos por anillo dan
        /// elección de ruta; en el MVP todos son <see cref="MapNodeType.Combat"/>, así que la
        /// bifurcación es flavor + topología lista para meter Shop/Event después (spec §17.6).
        /// </summary>
        public static RunMap BuildDefaultMap()
        {
            // x = avance hacia el rival (izq→der), y = carril visual (sólo presentación, 0..1).
            var nodes = new List<MapNode>
            {
                new MapNode(StartId, MapNodeType.Start, "El barrio",                0.02f, 0.50f)
                    .ConnectTo(1, 2, 3),

                // Anillo 1 (d1) — la calle se calienta.
                new MapNode(1, MapNodeType.Combat, "Asamblea en la esquina",        0.28f, 0.82f)
                    .ConnectTo(4, 5),
                new MapNode(2, MapNodeType.Combat, "Corte en la avenida",           0.30f, 0.50f)
                    .ConnectTo(4, 5, 6),
                new MapNode(3, MapNodeType.Combat, "Batucada en el puente",         0.28f, 0.18f)
                    .ConnectTo(5, 6),

                // Anillo 2 (d2) — el centro.
                new MapNode(4, MapNodeType.Combat, "Choque en Tribunales",          0.62f, 0.78f)
                    .ConnectTo(BossId),
                new MapNode(5, MapNodeType.Combat, "Línea de escudos en el Congreso", 0.64f, 0.46f)
                    .ConnectTo(BossId),
                new MapNode(6, MapNodeType.Combat, "Avanzada por la 9 de Julio",     0.62f, 0.16f)
                    .ConnectTo(BossId),

                // Jefe (d3) — el final.
                new MapNode(BossId, MapNodeType.Boss, "Casa Rosada",                 0.96f, 0.50f),
            };

            return new RunMap(nodes, StartId);
        }

        /// <summary>
        /// Acto 1 = <b>Línea A del subte</b> (spec §17.1/§17.6): tira lineal de estaciones reales, de
        /// Primera Junta a Plaza de Mayo / Casa Rosada (cabecera = jefe). El jugador avanza
        /// <b>1 o 2 paradas</b> por turno (topología <c>i → i+1, i+2</c>); las salteadas quedan atrás
        /// (una sola pasada, spec §17.1). Versión sin RNG: tipos de nodo fijos (determinista, para tests).
        /// Soporta multi-acto sin rediseño: ver <see cref="RunState.actIndex"/>.
        /// </summary>
        public static RunMap BuildActo1() => BuildLineaA(DefaultActo1Types());

        /// <summary>
        /// Igual que <see cref="BuildActo1()"/> pero con los tipos de nodo <b>sorteados por run</b> (RNG
        /// inyectado, spec §17.6): el esqueleto de estaciones es fijo y cada partida reparte distinto los
        /// encuentros (combate/tienda/evento/tesoro/élite), garantizando variedad. Replayabilidad sin
        /// perder la geografía real de la línea. <b>Rough, a iterar por playtest</b> ([[feedback-playtest-driven]]).
        /// </summary>
        public static RunMap BuildActo1(IRandomProvider rng) => BuildLineaA(RollActo1Types(rng));

        /// <summary>Construye el grafo lineal de la Línea A con los tipos dados (uno por estación, en
        /// orden). Cada parada conecta a las próximas <c>maxJump</c> (salto 1 o 2); la cabecera es
        /// terminal. <paramref name="types"/> debe tener largo == cantidad de estaciones.</summary>
        private static RunMap BuildLineaA(IReadOnlyList<MapNodeType> types)
        {
            const int maxJump = 2;   // avanzar 1 o 2 paradas (líneas más largas podrán subir esto)
            int n = LineaA.Length;
            var nodes = new List<MapNode>(n);
            for (int i = 0; i < n; i++)
            {
                Station s = LineaA[i];
                // x = avance hacia Plaza de Mayo (oeste→este); y centrado (tira en una sola línea).
                float x = n <= 1 ? 0.5f : 0.05f + 0.90f * (i / (float)(n - 1));
                var node = new MapNode(i, types[i], s.title, x, 0.5f, s.combinations);
                for (int j = 1; j <= maxJump && i + j < n; j++)
                    node.ConnectTo(i + j);
                nodes.Add(node);
            }
            return new RunMap(nodes, Acto1StartId, "Línea A", LineaAColorHex);
        }

        /// <summary>Tipos fijos (determinista) para <see cref="BuildActo1()"/>: Start, Boss en la
        /// cabecera, Taller justo antes del jefe, y un reparto fijo con variedad.</summary>
        private static MapNodeType[] DefaultActo1Types()
        {
            int n = LineaA.Length;
            var t = new MapNodeType[n];
            t[0] = MapNodeType.Start;
            t[n - 1] = MapNodeType.Boss;
            t[n - 2] = MapNodeType.Workshop;
            // Estaciones 1..n-3: especiales + relleno de combate (reparto fijo).
            var mid = new[]
            {
                MapNodeType.Combat, MapNodeType.Treasure, MapNodeType.Combat, MapNodeType.Event,
                MapNodeType.Shop, MapNodeType.Combat, MapNodeType.Elite, MapNodeType.Combat,
            };
            for (int i = 1; i <= n - 3; i++) t[i] = mid[(i - 1) % mid.Length];
            return t;
        }

        /// <summary>Tipos sorteados (RNG) para <see cref="BuildActo1(IRandomProvider)"/>: Start y Boss
        /// fijos, Taller antes del jefe, y las demás estaciones reparten ≥1 de cada tipo no-combate
        /// (Shop/Elite/Treasure/Event) + combates de relleno, barajados con el RNG inyectado.</summary>
        private static MapNodeType[] RollActo1Types(IRandomProvider rng)
        {
            int n = LineaA.Length;
            var t = new MapNodeType[n];
            t[0] = MapNodeType.Start;
            t[n - 1] = MapNodeType.Boss;
            t[n - 2] = MapNodeType.Workshop;

            int mid = n - 3;   // estaciones 1..n-3
            var bag = new List<MapNodeType>(mid)
            {
                MapNodeType.Shop, MapNodeType.Elite, MapNodeType.Treasure, MapNodeType.Event,
            };
            while (bag.Count < mid) bag.Add(MapNodeType.Combat);
            // Fisher-Yates con el RNG inyectado.
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            for (int i = 1; i <= mid; i++) t[i] = bag[i - 1];
            return t;
        }
    }
}
