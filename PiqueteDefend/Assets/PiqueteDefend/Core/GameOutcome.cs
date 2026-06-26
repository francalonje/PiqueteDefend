namespace PiqueteDefend.Core
{
    /// <summary>Resultado final de una partida. <see cref="Winner"/> es null en empate (spec §5).</summary>
    public readonly struct GameOutcome
    {
        public readonly Faction? Winner;
        public readonly WinCondition Condition;
        public readonly int HalfTurn;

        public GameOutcome(Faction? winner, WinCondition condition, int halfTurn)
        {
            Winner = winner;
            Condition = condition;
            HalfTurn = halfTurn;
        }

        public bool IsDraw => !Winner.HasValue;

        public static GameOutcome Win(Faction winner, WinCondition condition, int halfTurn)
            => new GameOutcome(winner, condition, halfTurn);

        public static GameOutcome Draw(int halfTurn)
            => new GameOutcome(null, WinCondition.Draw, halfTurn);
    }

    /// <summary>Fase actual del motor. Le dice a la presentación qué se espera a continuación.</summary>
    public enum GamePhase
    {
        NotStarted,
        AwaitingTurnStart,  // turno listo para comenzar: llamar BeginTurn()
        AwaitingAction,     // producción hecha: el jugador puede jugar/descartar, atacar y terminar turno
        Finished
    }

    /// <summary>Resultado de intentar una acción. Permite a la UI reaccionar (pedir un target, etc.).</summary>
    public enum ActionResult
    {
        Success,
        WrongPhase,
        IndexOutOfRange,
        CannotAfford,
        AlreadyPlayedCard,   // ya jugó o descartó una carta este turno
        AlreadyAttacked,     // ya atacó este turno
        NeedsDeploySlot,     // unidad: falta elegir en qué slot desplegar/reemplazar
        NeedsEffectTarget,   // acción con efecto sobre unidad: falta elegir slot objetivo
        NeedsAttackTarget,   // ataque a elección: falta elegir slot(s) objetivo
        NeedsSecondSlot,     // efecto con dos slots (MoveUnit/SwapUnits): falta el segundo
        NoUnitInSlot,        // no hay unidad propia en el slot atacante
        UnitStunned,         // la unidad está aturdida: no puede actuar este turno
        CannotAttackFirstTurn, // el primer jugador no puede atacar en su turno 1 (spec §3/§16)
        InvalidTarget,
        DiscardLimitReached    // ya descartó una carta este turno (máx 1 por turno, spec §6)
    }
}
