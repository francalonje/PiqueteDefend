using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Base de toda carta como dato puro (ScriptableObject), spec §7.1. Abstracta: cada carta
    /// concreta es una <see cref="UnitCardData"/> o <see cref="ActionCardData"/>. Agregar una
    /// carta = crear un asset, sin tocar código.
    ///
    /// Sólo contiene dominio + referencias de presentación livianas permitidas en Core
    /// (<see cref="Sprite"/>, ids de sonido/animación por string). El audio/efectos visuales
    /// concretos los resuelve la capa de presentación (spec §7.10).
    /// </summary>
    public abstract class CardData : ScriptableObject
    {
        [Header("Identidad")]
        public string id;
        public string cardName;
        public Faction faction;

        /// <summary>Derivado de la subclase concreta — no es un campo serializado (spec §7.1).</summary>
        public abstract CardType CardType { get; }

        [Header("Costo")]
        [Tooltip("Hoy una entrada; lista para soportar costos multi-recurso.")]
        public List<ResourceCost> costs = new List<ResourceCost>();

        [Header("Presentación")]
        public Sprite sprite;
        [TextArea] public string descriptionText;
        [TextArea] public string flavorText;
        [Tooltip("Id del sonido al jugar (lo resuelve AudioManager desde Resources). Opcional.")]
        public string playSoundId;
        [Tooltip("[FUTURO] nombre de animación a disparar al jugar.")]
        public string animationHook;
    }
}
