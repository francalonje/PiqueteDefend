using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Patrón de ataque de una unidad (spec §7.2). Define a qué slots del oponente pega y cuánto.
    ///
    /// <para><see cref="reference"/> = Absolute → <see cref="pattern"/> son slots del oponente (0–5).
    /// Relative → son offsets respecto del slot del atacante (0 = enfrentado).</para>
    ///
    /// <para><see cref="pickCount"/> = 0 → golpea TODOS los slots de <see cref="pattern"/>.
    /// N &gt; 0 → el atacante elige N de <see cref="pattern"/> al atacar. Para un ataque a libre
    /// elección, <see cref="pattern"/> son los 6 slots y <see cref="pickCount"/> = 1.</para>
    /// </summary>
    [Serializable]
    public class UnitAttack
    {
        public AttackReference reference;
        public int[] pattern = Array.Empty<int>();
        public int pickCount;

        /// <summary>
        /// Magnitud por slot: <b>daño</b> si <see cref="effect"/> es DamageEnemies,
        /// <b>curación</b> (tope maxHp) si es HealAllies (spec §7.2).
        /// </summary>
        public int damagePerSlot;

        /// <summary>Tablero objetivo: rival (daño) o propio (cura). Default DamageEnemies (spec §7.2).</summary>
        public AttackEffect effect = AttackEffect.DamageEnemies;

        /// <summary>
        /// Id del sonido al golpear (lo resuelve AudioManager desde Resources). Opcional:
        /// vacío = usa el default global de ataque. Punto de extensión para sonido por ataque.
        /// </summary>
        public string hitSoundId;

        public UnitAttack() { }

        public UnitAttack(AttackReference reference, int[] pattern, int pickCount, int damagePerSlot,
                          AttackEffect effect = AttackEffect.DamageEnemies)
        {
            this.reference = reference;
            this.pattern = pattern ?? Array.Empty<int>();
            this.pickCount = pickCount;
            this.damagePerSlot = damagePerSlot;
            this.effect = effect;
        }

        /// <summary>True si el atacante debe elegir slot(s) (ataque a elección).</summary>
        public bool RequiresChoice => pickCount > 0;

        /// <summary>True si cura aliadas en vez de dañar al rival.</summary>
        public bool IsHeal => effect == AttackEffect.HealAllies;
    }
}
