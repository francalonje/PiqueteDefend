using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Efecto pasivo de una unidad (spec §7.3). Puede producir recursos, curar/buffear aliadas o
    /// dañar/debuffear enemigas. Las pasivas dirigidas usan el <b>mismo targeting que un ataque</b>
    /// (<see cref="mode"/> + <see cref="count"/>) sobre el tablero indicado por <see cref="target"/>.
    /// Ej.: Aura → <c>Adjacent</c>; Humo (TurnDamage) → <c>Frontmost</c> enemies count N;
    /// Gas (TurnStatus) → <c>Frontmost</c> enemies count 1; Regen/Retaliate → <c>Self</c>;
    /// Jubilado (OnDeath) → Furia a <c>Adjacent</c> allies.
    /// </summary>
    [Serializable]
    public class PassiveEffect
    {
        public PassiveType passiveType;

        /// <summary>Magnitud (daño, cura, recurso, +daño de aura, daño de retaliate/turno).</summary>
        public int value;

        /// <summary>Recurso afectado (sólo <see cref="PassiveType.ProduceResource"/>).</summary>
        public ResourceType resource;

        /// <summary>Plantilla del status a aplicar (<see cref="PassiveType.TurnStatus"/> y <see cref="PassiveType.OnDeath"/> con status).</summary>
        public StatusEffect status;

        /// <summary>Sobre qué tablero recae (spec §7.3). Default Self.</summary>
        public PassiveTarget target = PassiveTarget.Self;

        /// <summary>Targeting igual que <see cref="UnitAttack"/> (ver <see cref="TargetMode"/>). Default Self.</summary>
        public TargetMode mode = TargetMode.Self;

        /// <summary>
        /// Profundidad/alcance (Frontmost/Backmost) o cantidad (Any). Ignorado en All/Self/Adjacent.
        /// Las pasivas son automáticas: el motor resuelve de forma determinista (espejo en `rules.py`).
        /// </summary>
        public int count = 1;

        public PassiveEffect() { }

        /// <summary>Constructor de producción (compat con el uso histórico).</summary>
        public PassiveEffect(PassiveType passiveType, ResourceType resource, int value)
        {
            this.passiveType = passiveType;
            this.resource = resource;
            this.value = value;
        }
    }
}
