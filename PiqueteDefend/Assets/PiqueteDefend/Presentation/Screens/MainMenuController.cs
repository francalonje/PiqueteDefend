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

            // Dos modos (spec §17.5/§11.1): "La Marcha" = run single-player vs IA; "Picado local" = hotseat.
            Button marcha = root.Q<Button>("la-marcha-button");
            Button picado = root.Q<Button>("picado-button");
            if (marcha != null) marcha.clicked += () => StartMode(GameMode.Run);
            if (picado != null) picado.clicked += () => StartMode(GameMode.Hotseat);

            // Ajustes: aún no implementado (deshabilitado). Salir: cierra el juego.
            Button settings = root.Q<Button>("settings-button");
            if (settings != null) settings.SetEnabled(false);
            Button quit = root.Q<Button>("quit-button");
            if (quit != null) quit.clicked += QuitGame;

            AudioManager.Instance?.PlayMusic(AudioId.MusicMain);
        }

        private void StartMode(GameMode mode)
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
            MatchConfig.Mode = mode;
            SceneManager.LoadScene("FactionSelect");
        }

        /// <summary>Cierra el juego. En el editor, frena el Play mode (para poder probarlo).</summary>
        public static void QuitGame()
        {
            AudioManager.Instance?.PlaySfx(AudioId.ButtonClick);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
