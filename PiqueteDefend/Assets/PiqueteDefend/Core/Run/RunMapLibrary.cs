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
    }
}
