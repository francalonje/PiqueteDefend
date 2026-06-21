using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Buff/debuff temporizado que vive en el <c>activeStatuses</c> de un jugador.
    /// Se procesa en la fase EFECTOS al inicio de cada turno del jugador afectado:
    /// el <see cref="counter"/> se decrementa y, al llegar a 0, dispara su payload
    /// y se elimina. El contador se mide en turnos del jugador afectado.
    /// </summary>
    [Serializable]
    public class StatusEffect
    {
        public StatusType statusType;

        /// <summary>Magnitud (ej. multiplicador de producción = 2 para DoubleProduction).</summary>
        public int value;

        /// <summary>Turnos del jugador afectado hasta disparar. Decrementa antes de disparar.</summary>
        public int counter;

        public StatusEffect() { }

        public StatusEffect(StatusType statusType, int value, int counter)
        {
            this.statusType = statusType;
            this.value = value;
            this.counter = counter;
        }

        /// <summary>Copia independiente — al aplicar un status se inserta una copia, no la plantilla.</summary>
        public StatusEffect Clone() => new StatusEffect(statusType, value, counter);

        /// <summary>
        /// True si el status vive en el jugador (producción, modelo fire-on-expiry). Los demás
        /// (Poison/Stun/Furia/Desmoralizar) viven por unidad (modelo active-while-present, spec §7.7).
        /// </summary>
        public static bool IsPlayerStatus(StatusType t) =>
            t == StatusType.SkipProduction || t == StatusType.DoubleProduction;
    }
}
