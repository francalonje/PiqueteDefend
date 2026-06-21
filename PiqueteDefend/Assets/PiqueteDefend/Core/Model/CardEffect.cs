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

        /// <summary>Slot de unidad afectado / origen (ModifyHP, RemoveUnit, ApplyStatus-a-unidad,
        /// MoveUnit, SwapUnits). -1 = lo elige el jugador al jugar.</summary>
        public int targetSlot = -1;

        /// <summary>Slot secundario: destino de <see cref="CardEffectType.MoveUnit"/> o segundo slot de
        /// <see cref="CardEffectType.SwapUnits"/>. -1 = lo elige el jugador.</summary>
        public int targetSlotB = -1;

        /// <summary>Recurso afectado (sólo para <see cref="CardEffectType.ModifyResource"/>).</summary>
        public ResourceType resourceTarget;

        /// <summary>Magnitud con signo: daño/drenaje negativos, cura/boost positivos.</summary>
        public int value;

        /// <summary>Plantilla del status a insertar (sólo si <see cref="CardEffectType.ApplyStatus"/>).</summary>
        public StatusEffect status;

        public CardEffect() { }

        public CardEffect(CardEffectType effectType, TargetType target,
                          ResourceType resourceTarget = ResourceType.Dinero,
                          int value = 0, StatusEffect status = null, int targetSlot = -1,
                          int targetSlotB = -1)
        {
            this.effectType = effectType;
            this.target = target;
            this.resourceTarget = resourceTarget;
            this.value = value;
            this.status = status;
            this.targetSlot = targetSlot;
            this.targetSlotB = targetSlotB;
        }

        /// <summary>
        /// True si este efecto recae sobre una unidad en un slot (puede requerir que el jugador elija).
        /// ApplyStatus cuenta sólo si el status es por unidad (los de producción van al jugador).
        /// </summary>
        public bool TargetsAUnitSlot
        {
            get
            {
                switch (effectType)
                {
                    case CardEffectType.ModifyHP:
                    case CardEffectType.RemoveUnit:
                    case CardEffectType.MoveUnit:
                    case CardEffectType.SwapUnits:
                        return true;
                    case CardEffectType.ApplyStatus:
                        return status != null && !StatusEffect.IsPlayerStatus(status.statusType);
                    default:
                        return false;
                }
            }
        }

        /// <summary>True si el efecto usa un segundo slot (MoveUnit destino / SwapUnits segundo).</summary>
        public bool NeedsSecondSlot =>
            effectType == CardEffectType.MoveUnit || effectType == CardEffectType.SwapUnits;
    }
}
