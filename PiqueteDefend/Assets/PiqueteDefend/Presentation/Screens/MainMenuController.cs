using UnityEngine;
using UnityEngine.SceneManagement;
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
        /// <summary>PanelSettings cargado de Resources (lo genera SceneSetup con este nombre).</summary>
        private const string PanelSettingsResource = "UIPanelSettings";

        private void OnEnable()
        {
            var doc = GetComponent<UIDocument>();

            // Garantiza el PanelSettings en runtime (la serialización del campo en escena es poco fiable).
            if (doc.panelSettings == null)
                doc.panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);

            VisualElement root = doc.rootVisualElement;
            if (root == null) return;

            SceneBackground.Apply(root, "bg-menu");

            Button play = root.Q<Button>("play-button");
            if (play != null) play.clicked += OnPlay;

            // v1: sin Ajustes ni Créditos
            SetDisabled(root.Q<Button>("settings-button"));
            SetDisabled(root.Q<Button>("credits-button"));

            AudioManager.Instance?.PlayMusic(AudioId.MusicMain);
        }

        private static void SetDisabled(Button button)
        {
            if (button != null) button.SetEnabled(false);
        }

        private void OnPlay()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            SceneManager.LoadScene("FactionSelect");
        }
    }
}
