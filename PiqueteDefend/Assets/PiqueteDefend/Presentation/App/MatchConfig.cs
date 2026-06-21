using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Configuración de la partida elegida en la pantalla de selección, leída por la pantalla
    /// de juego. Holder estático simple para pasar datos entre escenas (suficiente para hotseat).
    /// </summary>
    public static class MatchConfig
    {
        /// <summary>Lado izquierdo, fijo (por ahora): Manifestantes.</summary>
        public static Faction Player0 = Faction.Manifestantes;

        /// <summary>Lado derecho, fijo (por ahora): Policías.</summary>
        public static Faction Player1 = Faction.Policias;

        /// <summary>
        /// Facción que juega primero. Es lo único que decide la pantalla de selección;
        /// los lados no cambian. Default: arrancan los Manifestantes.
        /// </summary>
        public static Faction StartingFaction = Faction.Manifestantes;
    }
}
