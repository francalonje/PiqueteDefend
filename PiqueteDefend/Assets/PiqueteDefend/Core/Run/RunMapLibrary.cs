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
        public const int Acto1BossId = 6;

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
        /// Mayo / Casa Rosada (cabecera = jefe). Mezcla tipos de nodo para ejercitar todo el loop:
        /// combates con arquetipos curados, un tesoro y una élite en ramas paralelas, y el jefe al final.
        /// <code>
        ///   START Primera Junta (d0) → 1, 2
        ///   1 Combat  Plaza Miserere (d1) → 3
        ///   2 Combat  Congreso       (d1) → 3, 4
        ///   3 Treasure Sáenz Peña    (d2) → 5
        ///   4 Elite    Lima          (d2) → 5
        ///   5 Combat   Perú          (d3) → BOSS
        ///   BOSS Plaza de Mayo       (d4)
        /// </code>
        /// Cada pasada pelea 3-4 veces según la ruta (rama del tesoro vs rama de la élite). El modelo
        /// soporta multi-acto sin rediseño: ver <see cref="RunState.actIndex"/>. <b>Rough, a iterar por
        /// playtest</b> ([[feedback-playtest-driven]]).
        /// </summary>
        public static RunMap BuildActo1()
        {
            // x = avance hacia Plaza de Mayo (oeste→este), y = rama visual (sólo presentación, 0..1).
            var nodes = new List<MapNode>
            {
                new MapNode(Acto1StartId, MapNodeType.Start, "Primera Junta", 0.02f, 0.50f)
                    .ConnectTo(1, 2),

                new MapNode(1, MapNodeType.Combat,   "Plaza Miserere", 0.25f, 0.72f).ConnectTo(3),
                new MapNode(2, MapNodeType.Combat,   "Congreso",       0.25f, 0.28f).ConnectTo(3, 4),

                new MapNode(3, MapNodeType.Treasure, "Sáenz Peña",     0.50f, 0.72f).ConnectTo(5),
                new MapNode(4, MapNodeType.Elite,    "Lima",           0.50f, 0.28f).ConnectTo(5),

                new MapNode(5, MapNodeType.Combat,   "Perú",           0.74f, 0.50f).ConnectTo(Acto1BossId),

                new MapNode(Acto1BossId, MapNodeType.Boss, "Plaza de Mayo", 0.97f, 0.50f),
            };

            return new RunMap(nodes, Acto1StartId);
        }
    }
}
