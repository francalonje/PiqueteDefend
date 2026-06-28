using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Carga de íconos de <c>Resources/Icons/&lt;key&gt;.png</c> con caché (incluye null = falta),
    /// compartida por las pantallas (combate, mapa, tienda). Patrón <b>sprite-ready</b>: cuando se deja
    /// el PNG con su key, aparece solo; si falta, el llamador cae a glyph/texto. Centralizado para no
    /// duplicar el loader (antes inline en GameController y MapController).
    /// </summary>
    public static class IconLoader
    {
        private const string Folder = "Icons/";
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>Textura del ícono por key, o null si no existe el PNG. Cachea también el null para
        /// no reintentar la carga.</summary>
        public static Texture2D Texture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_cache.TryGetValue(key, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>(Folder + key);
                _cache[key] = tex;
            }
            return tex;
        }

        /// <summary>Pinta el ícono <paramref name="key"/> como fondo de <paramref name="el"/> si existe.
        /// Devuelve true si había textura (para que el llamador decida el fallback).</summary>
        public static bool TryApplyBackground(VisualElement el, string key)
        {
            Texture2D tex = Texture(key);
            if (tex == null) return false;
            el.style.backgroundImage = new StyleBackground(tex);
            return true;
        }

        /// <summary>Construye un ícono decorativo (no pickable) con el sprite de <paramref name="key"/>;
        /// si falta el PNG, cae a un <see cref="Label"/> con <paramref name="glyphFallback"/>. Aplica
        /// <paramref name="ussClass"/> en ambos casos. Devuelve null si no hay ni sprite ni glyph.</summary>
        public static VisualElement BuildIcon(string key, string glyphFallback, string ussClass = null)
        {
            Texture2D tex = Texture(key);
            VisualElement el;
            if (tex != null)
            {
                el = new VisualElement();
                el.style.backgroundImage = new StyleBackground(tex);
            }
            else if (!string.IsNullOrEmpty(glyphFallback))
            {
                el = new Label(glyphFallback);
            }
            else
            {
                return null;
            }

            if (!string.IsNullOrEmpty(ussClass)) el.AddToClassList(ussClass);
            el.pickingMode = PickingMode.Ignore;
            return el;
        }
    }
}
