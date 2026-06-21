using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Efecto pasivo de una unidad (spec §7.3). Puede producir recursos, curar/buffear aliadas o
    /// dañar/debuffear enemigas. Las pasivas que afectan un conjunto de slots usan el <b>mismo
    /// targeting que un ataque</b> (<see cref="reference"/> + <see cref="pattern"/> + <see cref="pickCount"/>)
    /// sobre el tablero indicado por <see cref="target"/>.
    /// </summary>
    [Serializable]
    public class PassiveEffect
    {
        public PassiveType passiveType;

        /// <summary>Magnitud (daño, cura, recurso, +daño de aura, daño de retaliate/turno).</summary>
        public int value;

        /// <summary>Recurso afectado (sólo <see cref="PassiveType.ProduceResource"/>).</summary>
        public ResourceType resource;

        /// <summary>Plantilla del status a aplicar (sólo <see cref="PassiveType.TurnStatus"/>).</summary>
        public StatusEffect status;

        /// <summary>Sobre qué tablero recae (spec §7.3). Default Self.</summary>
        public PassiveTarget target = PassiveTarget.Self;

        /// <summary>Targeting igual que <see cref="UnitAttack"/> (ignorado si <see cref="target"/> = Self).</summary>
        public AttackReference reference = AttackReference.Relative;
        public int[] pattern = Array.Empty<int>();

        /// <summary>
        /// <c>0</c> = afecta TODOS los slots del patrón; <c>N&gt;0</c> = afecta N slots OCUPADOS del
        /// patrón, elegidos de forma determinista (índice ascendente) por
        /// <see cref="GameEngine"/>.PassiveTargets — no hay elección humana (la pasiva es automática).
        /// Espejo en el sim (`rules.py`).
        /// </summary>
        public int pickCount;

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
