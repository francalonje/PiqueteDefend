using System;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Patrón de ataque de una unidad (spec §7.2). El targeting está <b>anclado a la formación
    /// enemiga</b> (spec §6): la posición del atacante NO influye, sólo importa la formación del
    /// objetivo.
    ///
    /// <para><see cref="mode"/> decide a quién pega (ver <see cref="TargetMode"/>). <see cref="count"/>
    /// = profundidad/alcance (Frontmost/Backmost) o cuántas elegir (Any); se ignora en All.</para>
    ///
    /// <para>Ej.: <c>Frontmost count=1</c> = "al de adelante de todo" (nunca whiffea);
    /// <c>Frontmost count=2</c> = penetra a los 2 primeros (el 2º whiffea si está vacío);
    /// <c>Any count=1</c> = snipe a elección; <c>All</c> = AoE.</para>
    /// </summary>
    [Serializable]
    public class UnitAttack
    {
        public TargetMode mode = TargetMode.Frontmost;

        /// <summary>Profundidad/alcance (Frontmost/Backmost) o cantidad a elegir (Any). Ignorado en All/Self/Adjacent.</summary>
        public int count = 1;

        /// <summary>
        /// Magnitud por golpe: <b>daño</b> si <see cref="effect"/> es DamageEnemies,
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

        public UnitAttack(TargetMode mode, int count, int damagePerSlot,
                          AttackEffect effect = AttackEffect.DamageEnemies)
        {
            this.mode = mode;
            this.count = count;
            this.damagePerSlot = damagePerSlot;
            this.effect = effect;
        }

        /// <summary>True si el atacante debe elegir objetivo(s): sólo el modo Any (snipe).</summary>
        public bool RequiresChoice => mode == TargetMode.Any;

        /// <summary>True si cura aliadas en vez de dañar al rival.</summary>
        public bool IsHeal => effect == AttackEffect.HealAllies;
    }
}
