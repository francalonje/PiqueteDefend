using UnityEngine;
using UnityEngine.UIElements;

namespace PiqueteDefend.Presentation
{
    /// <summary>
    /// Controlador de la pantalla de menú principal (spec §11.1). Conecta los botones del
    /// UXML a las acciones de flujo. Ajustes/Créditos quedan deshabilitados en v1.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;

            Button play = root.Q<Button>("play-button");
            if (play != null) play.clicked += OnPlay;

            // v1: sin Ajustes ni Créditos
            SetDisabled(root.Q<Button>("settings-button"));
            SetDisabled(root.Q<Button>("credits-button"));
        }

        private static void SetDisabled(Button button)
        {
            if (button != null) button.SetEnabled(false);
        }

        private void OnPlay()
        {
            // TODO Fase 4: navegar a selección de facción.
            Debug.Log("[MainMenu] Jugar.");
        }
    }
}
