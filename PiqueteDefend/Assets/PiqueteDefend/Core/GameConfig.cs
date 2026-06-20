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

        // Producción base por turno: 0 (sólo se produce vía unidades/cartas). Spec §3.
        public int baseProdDinero = 0;
        public int baseProdFuerza = 0;
        public int baseProdSocial = 0;

        public int maxResource = 100;

        // Anti-stalemate (spec §5.1)
        public int suddenDeathStart = 30;   // medio-turno desde el que pega muerte súbita
        public int suddenDeathDamage = 1;   // daño a todas las unidades al fin de turno
        public int maxTurns = 100;          // backstop duro (medios-turnos, uno por jugador)

        public int BaseProduction(ResourceType r) => r switch
        {
            ResourceType.Dinero => baseProdDinero,
            ResourceType.Fuerza => baseProdFuerza,
            ResourceType.Social => baseProdSocial,
            _ => 0
        };
    }
}
