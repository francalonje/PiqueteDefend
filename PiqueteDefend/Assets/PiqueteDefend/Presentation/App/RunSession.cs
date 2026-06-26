using PiqueteDefend.Core;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Estado cross-escena de la run single-player (spec §17). Holder estático (espejo de
    /// <see cref="MatchConfig"/>) que mantiene vivo el <see cref="RunManager"/> entre las escenas
    /// Map ↔ Game ↔ Reward, y entrega el <see cref="GameEngine"/> prearmado por
    /// <see cref="RunManager.BeginCombat"/> a la escena Game.
    ///
    /// <para>El núcleo de la run (<see cref="RunManager"/>) es C# puro; esto es sólo el pegamento de
    /// presentación para que sobreviva a los <c>SceneManager.LoadScene</c>.</para>
    /// </summary>
    public static class RunSession
    {
        /// <summary>La run en curso, o null si no hay ninguna (modo hotseat).</summary>
        public static RunManager Manager { get; private set; }

        private static GameEngine _pendingCombat;

        /// <summary>True si hay una run en curso.</summary>
        public static bool IsActive => Manager != null;

        /// <summary>Hay un combate prearmado esperando que la escena Game lo consuma.</summary>
        public static bool HasPendingCombat => _pendingCombat != null;

        /// <summary>Arranca una run nueva (la dispara FactionSelect en modo Run).</summary>
        public static void Start(RunManager manager)
        {
            Manager = manager;
            _pendingCombat = null;
        }

        /// <summary>Guarda el motor de combate ya iniciado (lo arma el mapa vía BeginCombat).</summary>
        public static void SetPendingCombat(GameEngine engine) => _pendingCombat = engine;

        /// <summary>Entrega y limpia el combate pendiente (lo consume GameController en OnEnable).</summary>
        public static GameEngine TakePendingCombat()
        {
            GameEngine e = _pendingCombat;
            _pendingCombat = null;
            return e;
        }

        /// <summary>Cierra la run (al ganar/perder y volver al menú).</summary>
        public static void Clear()
        {
            Manager = null;
            _pendingCombat = null;
        }
    }
}
