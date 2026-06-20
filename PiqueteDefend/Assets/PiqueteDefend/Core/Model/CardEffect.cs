using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Efecto inmediato que se resuelve al jugar una carta de acción (spec §7.6).
    /// Una carta puede llevar varios <see cref="CardEffect"/>, resueltos en orden.
    ///
    /// Qué campos aplican según <see cref="effectType"/>:
    /// <list type="bullet">
    /// <item>ModifyHP    → <see cref="target"/>, <see cref="targetSlot"/>, <see cref="value"/> (con signo).</item>
    /// <item>ModifyResource → <see cref="target"/>, <see cref="resourceTarget"/>, <see cref="value"/>.</item>
    /// <item>RemoveUnit  → <see cref="target"/>, <see cref="targetSlot"/>.</item>
    /// <item>ApplyStatus → <see cref="target"/>, <see cref="status"/>.</item>
    /// </list>
    ///
    /// Los efectos "diferidos" (saltear/doblar producción) no son un tipo especial: son
    /// <see cref="CardEffectType.ApplyStatus"/> que coloca un <see cref="StatusEffect"/> con contador.
    /// </summary>
    [Serializable]
    public class CardEffect
    {
        public CardEffectType effectType;
        public TargetType target;

        /// <summary>Slot de unidad afectado (ModifyHP / RemoveUnit). -1 = lo elige el jugador al jugar.</summary>
        public int targetSlot = -1;

        /// <summary>Recurso afectado (sólo para <see cref="CardEffectType.ModifyResource"/>).</summary>
        public ResourceType resourceTarget;

        /// <summary>Magnitud con signo: daño/drenaje negativos, cura/boost positivos.</summary>
        public int value;

        /// <summary>Plantilla del status a insertar (sólo si <see cref="CardEffectType.ApplyStatus"/>).</summary>
        public StatusEffect status;

        public CardEffect() { }

        public CardEffect(CardEffectType effectType, TargetType target,
                          ResourceType resourceTarget = ResourceType.Dinero,
                          int value = 0, StatusEffect status = null, int targetSlot = -1)
        {
            this.effectType = effectType;
            this.target = target;
            this.resourceTarget = resourceTarget;
            this.value = value;
            this.status = status;
            this.targetSlot = targetSlot;
        }

        /// <summary>True si este efecto necesita que el jugador elija un slot objetivo (targetSlot &lt; 0).</summary>
        public bool TargetsAUnitSlot =>
            (effectType == CardEffectType.ModifyHP || effectType == CardEffectType.RemoveUnit);
    }
}
