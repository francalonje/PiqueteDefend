using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Todos los parámetros de balance/reglas en un solo lugar (spec §12). Ajustables por balanceo.
    /// </summary>
    [Serializable]
    public class GameConfig
    {
        public int handSize = 6;
        public int maxSlots = 6;

        /// <summary>[FUTURO] tope de apilamiento; sin mecánica activa.</summary>
        public int maxStack = 5;

        public int initialDinero = 5;
        public int initialFuerza = 5;
        public int initialSocial = 5;

        // Producción base por turno: +1 de cada recurso (spec §3, validado §12).
        public int baseProdDinero = 1;
        public int baseProdFuerza = 1;
        public int baseProdSocial = 1;

        public int maxResource = 100;

        // Reglas de iniciativa (spec §3/§16, validadas por simulación): sin ellas el primer
        // jugador gana ~59%; con ambas, ~48% (parejo).
        public bool firstProducesTurn1 = true;    // el primer jugador SÍ produce en su turno 1
        public bool firstNoAttackTurn1 = true;     // el primer jugador NO ataca en su turno 1

        // Anti-stalemate (spec §5.1) — backstops, bien por encima de la duración ideal (~32).
        public int suddenDeathStart = 50;   // medio-turno desde el que pega muerte súbita
        public int suddenDeathDamage = 1;   // daño a todas las unidades al fin de turno
        public int maxTurns = 120;          // backstop duro (medios-turnos, uno por jugador)

        // Inflación (mecánica de juego, spec §3): a partir de inflationStartTurn (medio-turno)
        // las cartas cuestan inflationPercentPerTurn % más, acumulativo por medio-turno. Come el
        // excedente de recursos en partidas largas. 0 en start = desactivada.
        public int inflationStartTurn = 8;
        public int inflationPercentPerTurn = 5;

        public int BaseProduction(ResourceType r) => r switch
        {
            ResourceType.Dinero => baseProdDinero,
            ResourceType.Fuerza => baseProdFuerza,
            ResourceType.Social => baseProdSocial,
            _ => 0
        };
    }
}
