using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>Modo elegido en el menú: hotseat 2-jugadores o run single-player (spec §17).</summary>
    public enum GameMode
    {
        Hotseat,
        Run
    }

    /// <summary>
    /// Configuración de la partida elegida en la pantalla de selección, leída por la pantalla
    /// de juego. Holder estático simple para pasar datos entre escenas (suficiente para hotseat).
    /// La run usa <see cref="RunSession"/> para su estado cross-escena.
    /// </summary>
    public static class MatchConfig
    {
        /// <summary>Modo elegido en el menú principal. Default hotseat (2-jugadores).</summary>
        public static GameMode Mode = GameMode.Hotseat;

        /// <summary>Lado izquierdo, fijo (por ahora): Manifestantes.</summary>
        public static Faction Player0 = Faction.Manifestantes;

        /// <summary>Lado derecho, fijo (por ahora): Policías.</summary>
        public static Faction Player1 = Faction.Policias;

        /// <summary>
        /// Facción que juega primero (sólo hotseat). Es lo único que decide la pantalla de selección;
        /// los lados no cambian. Default: arrancan los Manifestantes.
        /// </summary>
        public static Faction StartingFaction = Faction.Manifestantes;
    }
}
