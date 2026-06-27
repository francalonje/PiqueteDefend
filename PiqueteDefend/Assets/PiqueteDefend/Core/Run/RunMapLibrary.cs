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

        // Ids del acto 1 (Línea A del subte). Estables para presentación/tests.
        public const int Acto1StartId = 0;
        public const int Acto1BossId = 9;

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
        /// Acto 1 = <b>Línea A del subte</b> (spec §17.6), de Primera Junta (el barrio) hasta Plaza de
        /// Mayo / Casa Rosada (cabecera = jefe). Mezcla TODOS los tipos de nodo en anillos por distancia,
        /// con ramas paralelas para que la ruta sea una decisión. El taller cae sobre todas las rutas.
        /// <code>
        ///   START Primera Junta (d0) → 1, 2
        ///   d1: 1 Combat Acoyte → 3,4   · 2 Combat Río de Janeiro → 4,5
        ///   d2: 3 Treasure Castro Barros → 6 · 4 Event Loria → 6,7 · 5 Shop Plaza Miserere → 7
        ///   d3: 6 Combat Congreso → 8   · 7 Elite Sáenz Peña → 8
        ///   d4: 8 Workshop Lima → BOSS
        ///   d5: BOSS Plaza de Mayo
        /// </code>
        /// Cada pasada pelea 3 veces (2 combates/élite + jefe) y pasa por 1-2 nodos no-combate + el taller.
        /// Soporta multi-acto sin rediseño: ver <see cref="RunState.actIndex"/>. <b>Rough, a iterar por
        /// playtest</b> ([[feedback-playtest-driven]]).
        /// </summary>
        public static RunMap BuildActo1()
        {
            // x = avance hacia Plaza de Mayo (oeste→este), y = rama visual (sólo presentación, 0..1).
            var nodes = new List<MapNode>
            {
                new MapNode(Acto1StartId, MapNodeType.Start, "Primera Junta", 0.02f, 0.50f)
                    .ConnectTo(1, 2),

                // Anillo 1 (d1) — la calle se calienta.
                new MapNode(1, MapNodeType.Combat,   "Acoyte",         0.18f, 0.70f).ConnectTo(3, 4),
                new MapNode(2, MapNodeType.Combat,   "Río de Janeiro", 0.18f, 0.30f).ConnectTo(4, 5),

                // Anillo 2 (d2) — variedad: tesoro / evento / tienda.
                new MapNode(3, MapNodeType.Treasure, "Castro Barros",  0.36f, 0.82f).ConnectTo(6),
                new MapNode(4, MapNodeType.Event,    "Loria",          0.36f, 0.50f).ConnectTo(6, 7),
                new MapNode(5, MapNodeType.Shop,     "Plaza Miserere", 0.36f, 0.18f).ConnectTo(7),

                // Anillo 3 (d3) — el centro pega más fuerte.
                new MapNode(6, MapNodeType.Combat,   "Congreso",       0.56f, 0.66f).ConnectTo(8),
                new MapNode(7, MapNodeType.Elite,    "Sáenz Peña",     0.56f, 0.34f).ConnectTo(8),

                // Anillo 4 (d4) — taller antes de la cabecera (sobre toda ruta).
                new MapNode(8, MapNodeType.Workshop, "Lima",           0.76f, 0.50f).ConnectTo(Acto1BossId),

                // Jefe (d5) — el final.
                new MapNode(Acto1BossId, MapNodeType.Boss, "Plaza de Mayo", 0.96f, 0.50f),
            };

            return new RunMap(nodes, Acto1StartId);
        }
    }
}
