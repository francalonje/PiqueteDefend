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
        private const string ResourcesDir = "Assets/PiqueteDefend/Presentation/Resources";
        private const string ScenesDir = "Assets/PiqueteDefend/Scenes";

        /// <summary>Nombre con el que el runtime carga el PanelSettings vía Resources.Load.</summary>
        public const string PanelSettingsResourceName = "UIPanelSettings";

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
            // Limpia el asset viejo bajo Data/ (ya no se usa; ahora va en Resources/).
            AssetDatabase.DeleteAsset("Assets/PiqueteDefend/Data/UIPanelSettings.asset");

            if (!AssetDatabase.IsValidFolder(ResourcesDir))
                AssetDatabase.CreateFolder("Assets/PiqueteDefend/Presentation", "Resources");

            string path = ResourcesDir + "/" + PanelSettingsResourceName + ".asset";
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

            // Cámara (fondo sólido; evita el aviso "No cameras rendering")
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.094f, 0.102f, 0.129f);
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            // GameObject con UIDocument (renderiza el UXML del menú)
            var uiGo = new GameObject("MainMenu");
            var doc = uiGo.AddComponent<UIDocument>();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/MainMenu.uxml");

            // Asignar por SerializedObject: la propiedad pública no siempre se serializa al guardar.
            var so = new SerializedObject(doc);
            so.FindProperty("m_PanelSettings").objectReferenceValue = panel;
            so.FindProperty("sourceAsset").objectReferenceValue = uxml;
            so.ApplyModifiedPropertiesWithoutUndo();

            uiGo.AddComponent<MainMenuController>();

            // EventSystem (Input System) para que la UI reciba clicks en runtime
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            EditorUtility.SetDirty(doc);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenesDir + "/Main.unity");
        }
    }
}
