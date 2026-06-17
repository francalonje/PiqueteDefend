using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Efecto inmediato que se resuelve al jugar una carta de acción.
    /// Una carta puede llevar varios <see cref="CardEffect"/>, resueltos en orden.
    /// Los efectos "diferidos" (saltear/doblar producción) no son un tipo especial:
    /// son un efecto <see cref="CardEffectType.ApplyStatus"/> que coloca un
    /// <see cref="StatusEffect"/> con contador en el objetivo.
    /// </summary>
    [Serializable]
    public class CardEffect
    {
        public CardEffectType effectType;
        public TargetType target;

        /// <summary>Recurso afectado (sólo para <see cref="CardEffectType.ModifyResource"/>).</summary>
        public ResourceType resourceTarget;

        /// <summary>Magnitud con signo: daño/drenaje negativos, cura/boost positivos.</summary>
        public int value;

        /// <summary>Plantilla del status a insertar (sólo si <see cref="CardEffectType.ApplyStatus"/>).</summary>
        public StatusEffect status;

        public CardEffect() { }

        public CardEffect(CardEffectType effectType, TargetType target,
                          ResourceType resourceTarget = ResourceType.Dinero,
                          int value = 0, StatusEffect status = null)
        {
            this.effectType = effectType;
            this.target = target;
            this.resourceTarget = resourceTarget;
            this.value = value;
            this.status = status;
        }
    }
}
