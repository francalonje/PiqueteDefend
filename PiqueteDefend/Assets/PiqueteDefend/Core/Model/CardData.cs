using System.Collections.Generic;
using UnityEngine;

namespace PiqueteDefend.Core
{
    /// <summary>
    /// Definición de una carta como dato puro (ScriptableObject). Cada carta es un asset.
    /// Agregar una carta = crear un asset, sin tocar código.
    ///
    /// Las cartas de <see cref="CardType.Unidad"/> dejan <see cref="effects"/> vacío: su
    /// efecto es pasivo y lo resuelve el motor según <see cref="unitSubtype"/> y el contador
    /// del slot. Las cartas de <see cref="CardType.Accion"/> usan <see cref="effects"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "PiqueteDefend/Card", order = 0)]
    public class CardData : ScriptableObject
    {
        [Header("Identidad")]
        public string id;
        public string cardName;
        public Faction faction;
        public CardType cardType;

        [Header("Acción (sólo si cardType == Accion)")]
        [Tooltip("Categoría visual/temática. No afecta la lógica.")]
        public ActionCategory actionCategory;
        public List<CardEffect> effects = new List<CardEffect>();

        [Header("Unidad (sólo si cardType == Unidad)")]
        public UnitSubtype unitSubtype;
        [Tooltip("Recurso que produce (sólo si unitSubtype == Productora).")]
        public ResourceType productionResource;

        [Header("Costo")]
        public int costDinero;
        public int costFuerza;
        public int costSocial;

        [Header("Presentación")]
        public Sprite sprite;
        [TextArea] public string descriptionText;
        [TextArea] public string flavorText;
    }
}
