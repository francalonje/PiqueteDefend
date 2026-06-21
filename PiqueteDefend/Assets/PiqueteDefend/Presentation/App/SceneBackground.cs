using UnityEngine;
using UnityEngine.UIElements;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Carga la imagen de fondo de una pantalla en runtime desde <c>Resources/</c>.
    /// Busca el elemento llamado "bg" en el root y le asigna el sprite/textura por nombre.
    /// El modo de escala (cover, 100% 100%, etc.) lo define el USS de cada pantalla vía su clase.
    /// Centralizado acá para no duplicar la carga en cada controlador (Main, FactionSelect, Game).
    /// </summary>
    public static class SceneBackground
    {
        public static void Apply(VisualElement root, string resourceName)
        {
            if (root == null) return;
            var bg = root.Q<VisualElement>("bg");
            if (bg == null) return;

            var texture = Resources.Load<Texture2D>(resourceName);
            if (texture != null) { bg.style.backgroundImage = new StyleBackground(texture); return; }

            var sprite = Resources.Load<Sprite>(resourceName);
            if (sprite != null) bg.style.backgroundImage = new StyleBackground(sprite);
        }
    }
}
