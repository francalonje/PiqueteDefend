using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Configuración de la partida elegida en la pantalla de selección, leída por la pantalla
    /// de juego. Holder estático simple para pasar datos entre escenas (suficiente para hotseat).
    /// </summary>
    public static class MatchConfig
    {
        public static Faction Player0 = Faction.Manifestantes;
        public static Faction Player1 = Faction.Policias;
    }
}
