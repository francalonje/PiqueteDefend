namespace PiqueteDefend.Core
{
    /// <summary>Tipo de acción planificada por un <see cref="IPlayerController"/> (spec §7.8/§17.5).</summary>
    public enum PlannedActionKind
    {
        PlayCard,
        Attack,
        EndTurn
    }

    /// <summary>
    /// Una acción concreta que un controlador propone ejecutar en su turno. Datos puros: el
    /// <see cref="GameEngine"/> la resuelve vía <see cref="Execute"/>. Diseñada para ejecutarse de a
    /// una (el llamador puede intercalar delays/animaciones entre acciones — turno de IA, spec §11.3/§17.5).
    /// </summary>
    public readonly struct PlannedAction
    {
        public readonly PlannedActionKind kind;
        public readonly int handIndex;
        public readonly int deploySlot;
        public readonly int effectTargetSlot;
        public readonly int effectTargetSlotB;
        public readonly int attackerSlot;

        /// <summary>Objetivos elegidos para un ataque snipe (Any). null para los modos anclados.</summary>
        public readonly int[] attackTargets;

        private PlannedAction(PlannedActionKind kind, int handIndex, int deploySlot,
                              int effectTargetSlot, int effectTargetSlotB,
                              int attackerSlot, int[] attackTargets)
        {
            this.kind = kind;
            this.handIndex = handIndex;
            this.deploySlot = deploySlot;
            this.effectTargetSlot = effectTargetSlot;
            this.effectTargetSlotB = effectTargetSlotB;
            this.attackerSlot = attackerSlot;
            this.attackTargets = attackTargets;
        }

        public static PlannedAction PlayCard(int handIndex, int deploySlot = -1,
            int effectTargetSlot = -1, int effectTargetSlotB = -1) =>
            new PlannedAction(PlannedActionKind.PlayCard, handIndex, deploySlot,
                              effectTargetSlot, effectTargetSlotB, -1, null);

        public static PlannedAction Attack(int attackerSlot, int[] targets = null) =>
            new PlannedAction(PlannedActionKind.Attack, -1, -1, -1, -1, attackerSlot, targets);

        public static PlannedAction EndTurn() =>
            new PlannedAction(PlannedActionKind.EndTurn, -1, -1, -1, -1, -1, null);

        public bool IsEndTurn => kind == PlannedActionKind.EndTurn;

        /// <summary>Ejecuta la acción sobre el motor y devuelve el resultado (EndTurn = no-op Success;
        /// el llamador decide cuándo llamar a <see cref="GameEngine.EndTurn"/>).</summary>
        public ActionResult Execute(GameEngine engine)
        {
            switch (kind)
            {
                case PlannedActionKind.PlayCard:
                    return engine.PlayCard(handIndex, deploySlot, effectTargetSlot, effectTargetSlotB);
                case PlannedActionKind.Attack:
                    return engine.AttackWithUnit(attackerSlot, attackTargets);
                default:
                    return ActionResult.Success;
            }
        }
    }

    /// <summary>
    /// Decide las acciones del jugador activo sin conocer la escena (spec §7.8/§17.5). El motor/UI
    /// le pide la próxima acción y la ejecuta; permite IA y, a futuro, multijugador online sin tocar
    /// el <see cref="GameEngine"/>. El humano se maneja por eventos de UI (no necesita esta interfaz).
    /// </summary>
    public interface IPlayerController
    {
        /// <summary>
        /// La próxima acción a ejecutar para el jugador activo, o <see cref="PlannedAction.EndTurn"/>
        /// cuando ya no conviene hacer nada más. Se llama repetidamente: cada llamada re-evalúa el
        /// estado actual del motor (greedy multi-acción, spec §6/§16.2).
        /// </summary>
        PlannedAction NextAction(GameEngine engine);
    }
}
