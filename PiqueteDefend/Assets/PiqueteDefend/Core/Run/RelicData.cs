using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>Cómo afecta una reliquia al combate (spec §17.6). Cada variante pega en un seam que el
    /// motor YA entiende (recursos/unidades/estados iniciales del <see cref="PlayerSetup"/>), así que
    /// las reliquias no tocan el resolutor de combate. Tipos que necesiten hooks dinámicos
    /// (al-matar, al-inicio-de-turno) quedan para <c>ICombatRule</c> [EXTENSIÓN], aún sin implementar.</summary>
    public enum RelicEffectKind
    {
        /// <summary>+<see cref="RelicData.value"/> a un recurso inicial del humano (spec §17.6).</summary>
        BonusResource,

        /// <summary>Despliega una <see cref="RelicData.unit"/> inicial extra para el humano.</summary>
        ExtraStartingUnit,

        /// <summary>Siembra un <see cref="RelicData.status"/> al iniciar (jugador o sus unidades).</summary>
        InitialStatus
    }

    /// <summary>
    /// Reliquia persistente de la run (spec §17.4/§17.6): modificador pasivo que dura toda la run y se
    /// traduce a bonos del <see cref="PlayerSetup"/> del humano al iniciar cada combate — el mismo seam
    /// que el handicap de la IA, sin tocar el motor. Es <b>data</b> (ScriptableObject): agregar una
    /// reliquia = crear un asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Relic", menuName = "PiqueteDefend/Relic")]
    public sealed class RelicData : ScriptableObject
    {
        public string id;
        public string relicName;
        [TextArea] public string description;

        public RelicEffectKind kind;

        /// <summary>Para <see cref="RelicEffectKind.BonusResource"/>: qué recurso y cuánto.</summary>
        public ResourceType resource;
        public int value;

        /// <summary>Para <see cref="RelicEffectKind.ExtraStartingUnit"/>: la unidad a desplegar.</summary>
        public UnitCardData unit;

        /// <summary>Para <see cref="RelicEffectKind.InitialStatus"/>: el estado a sembrar.</summary>
        public StatusEffect status;
    }
}
