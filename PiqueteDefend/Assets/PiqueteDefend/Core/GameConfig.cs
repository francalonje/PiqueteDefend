using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Todos los parámetros de balance/reglas en un solo lugar (spec §12).
    /// Defaults validados con la simulación. Ajustables por balanceo.
    /// </summary>
    [Serializable]
    public class GameConfig
    {
        public int handSize = 6;
        public int maxSlots = 3;
        public int maxStack = 5;

        public int hpInitial = 100;

        public int initialDinero = 3;
        public int initialFuerza = 2;
        public int initialSocial = 1;

        public int baseProdDinero = 5;
        public int baseProdFuerza = 3;
        public int baseProdSocial = 2;

        public int maxResource = 100;

        public int socialThreshold = 70;   // Hegemonía Social
        public int dineroThreshold = 100;  // Poder Económico

        public int suddenDeathStart = 40;
        public int maxTurns = 100;          // contado en medios-turnos (uno por jugador)

        public int BaseProduction(ResourceType r) => r switch
        {
            ResourceType.Dinero => baseProdDinero,
            ResourceType.Fuerza => baseProdFuerza,
            ResourceType.Social => baseProdSocial,
            _ => 0
        };
    }
}
