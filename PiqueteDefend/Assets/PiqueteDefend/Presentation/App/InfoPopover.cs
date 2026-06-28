using UnityEngine;
using UnityEngine.UIElements;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Popover informativo compartido (título + cuerpo) que cualquier pantalla puede mostrar al hacer
    /// hover sobre un elemento, con el mismo look que el popover de combate. Se monta sobre el root del
    /// panel del propio anchor (sirve en combate, mapa, tienda, etc. sin pasar referencias) y se posiciona
    /// arriba del anchor, clampeado al root. Estilos: clases <c>popover*</c> en Common.uss.
    /// </summary>
    public static class InfoPopover
    {
        private const string ElementName = "shared-info-popover";

        /// <summary>Muestra el popover anclado a <paramref name="anchor"/>: <paramref name="title"/> en el
        /// header, <paramref name="body"/> (flavor) y una línea de <paramref name="effect"/> mecánico
        /// resaltada (opcional). No hace nada si el anchor no está en un panel.</summary>
        public static void Show(VisualElement anchor, string title, string body, string effect = null)
        {
            VisualElement root = anchor?.panel?.visualTree;
            if (root == null) return;

            VisualElement pop = Ensure(root);
            pop.Clear();

            var header = new VisualElement();
            header.AddToClassList("popover__header");
            var t = new Label(title);
            t.AddToClassList("popover__title");
            header.Add(t);
            pop.Add(header);

            if (!string.IsNullOrEmpty(body))
            {
                var b = new Label(body);
                b.AddToClassList("popover__body");
                pop.Add(b);
            }

            if (!string.IsNullOrEmpty(effect))
            {
                var e = new Label(effect);
                e.AddToClassList("popover__effect");
                pop.Add(e);
            }

            pop.style.display = DisplayStyle.Flex;
            // Posicionar cuando el layout ya resolvió el tamaño del popover (próximo tick).
            pop.schedule.Execute(() => PlacePopover(pop, root, anchor)).StartingIn(0);
        }

        /// <summary>Oculta el popover (si lo hay en el panel del anchor).</summary>
        public static void Hide(VisualElement anchor)
        {
            VisualElement root = anchor?.panel?.visualTree;
            VisualElement pop = root?.Q<VisualElement>(ElementName);
            if (pop != null) pop.style.display = DisplayStyle.None;
        }

        private static VisualElement Ensure(VisualElement root)
        {
            VisualElement pop = root.Q<VisualElement>(ElementName);
            if (pop == null)
            {
                pop = new VisualElement { name = ElementName, pickingMode = PickingMode.Ignore };
                pop.AddToClassList("popover");
                pop.style.position = Position.Absolute;
                pop.style.display = DisplayStyle.None;
            }
            root.Add(pop);   // (re)montar al final = al frente
            return pop;
        }

        private static void PlacePopover(VisualElement pop, VisualElement root, VisualElement anchor)
        {
            float w = pop.resolvedStyle.width;
            float h = pop.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;

            Rect a = anchor.worldBound;
            Vector2 topCenter = root.WorldToLocal(new Vector2(a.center.x, a.yMin));
            float x = topCenter.x - w * 0.5f;
            float y = topCenter.y - h - 8f;        // arriba del anchor
            if (y < 4f)                            // sin lugar arriba → debajo
            {
                Vector2 botCenter = root.WorldToLocal(new Vector2(a.center.x, a.yMax));
                y = botCenter.y + 8f;
            }

            float rootW = root.resolvedStyle.width;
            x = Mathf.Clamp(x, 4f, Mathf.Max(4f, rootW - w - 4f));
            pop.style.left = x;
            pop.style.top = y;
        }
    }
}
