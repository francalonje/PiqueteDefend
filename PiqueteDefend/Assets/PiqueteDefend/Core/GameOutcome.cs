namespace PiqueteDefend.Core
{
    /// <summary>Resultado final de una partida.</summary>
    public readonly struct GameOutcome
    {
        public readonly Faction Winner;
        public readonly WinCondition Condition;
        public readonly int HalfTurn;

        public GameOutcome(Faction winner, WinCondition condition, int halfTurn)
        {
            Winner = winner;
            Condition = condition;
            HalfTurn = halfTurn;
        }
    }

    /// <summary>Fase actual del motor. Le dice a la presentación qué se espera a continuación.</summary>
    public enum GamePhase
    {
        NotStarted,
        AwaitingTurnStart,  // turno listo para comenzar: llamar BeginTurn()
        AwaitingAction,     // producción hecha: el jugador activo debe jugar o descartar
        Finished
    }

    /// <summary>Resultado de intentar una acción (jugar/descartar). Permite a la UI reaccionar.</summary>
    public enum ActionResult
    {
        Success,
        WrongPhase,
        IndexOutOfRange,
        CannotAfford,
        NeedsUnitSlotChoice,  // unidad nueva con slots llenos: falta elegir slot a reemplazar
        NeedsRemoveTarget     // carta con RemoveUnit y el oponente tiene unidades: falta elegir slot
    }
}
