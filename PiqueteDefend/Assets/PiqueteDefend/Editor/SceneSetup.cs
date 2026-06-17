using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;
using PiqueteDefend.Presentation;

namespace PiqueteDefend.EditorTools
{
    /// <summary>
    /// Genera por código los assets y escenas de UI (PanelSettings + escenas con UIDocument).
    /// Mantiene la capa de presentación reproducible y versionable sin armado manual en el editor.
    ///
    /// Menú: PiqueteDefend → Setup UI Scenes.
    /// </summary>
    public static class SceneSetup
    {
        private const string UiDir = "Assets/PiqueteDefend/Presentation/UI";
        private const string DataDir = "Assets/PiqueteDefend/Data";
        private const string ScenesDir = "Assets/PiqueteDefend/Scenes";

        [MenuItem("PiqueteDefend/Setup UI Scenes")]
        public static void SetupAll()
        {
            PanelSettings panel = GetOrCreatePanelSettings();
            BuildMainMenuScene(panel);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneSetup] Listo.");
        }

        private static PanelSettings GetOrCreatePanelSettings()
        {
            const string path = DataDir + "/UIPanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel != null) return panel;

            panel = ScriptableObject.CreateInstance<PanelSettings>();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiDir + "/DefaultRuntimeTheme.tss");
            if (theme != null)
                panel.themeStyleSheet = theme;
            else
                Debug.LogWarning("[SceneSetup] No se encontró DefaultRuntimeTheme.tss; el panel puede no renderizar.");

            AssetDatabase.CreateAsset(panel, path);
            return panel;
        }

        private static void BuildMainMenuScene(PanelSettings panel)
        {
            if (!AssetDatabase.IsValidFolder(ScenesDir))
                AssetDatabase.CreateFolder("Assets/PiqueteDefend", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // GameObject con UIDocument (renderiza el UXML del menú)
            var uiGo = new GameObject("MainMenu");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/MainMenu.uxml");
            uiGo.AddComponent<MainMenuController>();

            // EventSystem (Input System) para que la UI reciba clicks en runtime
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, ScenesDir + "/Main.unity");
        }
    }
}
