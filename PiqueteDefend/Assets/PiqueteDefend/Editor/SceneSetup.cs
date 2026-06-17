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
            GetOrCreatePanelSettings();

            if (!AssetDatabase.IsValidFolder(ScenesDir))
                AssetDatabase.CreateFolder("Assets/PiqueteDefend", "Scenes");

            BuildScene("Main", "MainMenu", typeof(MainMenuController));
            BuildScene("FactionSelect", "FactionSelect", typeof(FactionSelectController));

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenesDir + "/Main.unity", true),
                new EditorBuildSettingsScene(ScenesDir + "/FactionSelect.unity", true),
            };

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

        /// <summary>
        /// Construye una escena de pantalla: cámara + UIDocument (con el UXML dado) + el controller
        /// indicado + EventSystem. El PanelSettings NO se setea acá (no persiste de forma fiable);
        /// cada controller lo carga en runtime desde Resources.
        /// </summary>
        private static void BuildScene(string sceneName, string uxmlName, System.Type controllerType)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Cámara (fondo sólido; evita el aviso "No cameras rendering")
            var camGo = new GameObject("Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.094f, 0.102f, 0.129f);
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            // GameObject con UIDocument + controller
            var uiGo = new GameObject(sceneName);
            var doc = uiGo.AddComponent<UIDocument>();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{UiDir}/{uxmlName}.uxml");

            var so = new SerializedObject(doc);
            so.FindProperty("sourceAsset").objectReferenceValue = uxml;
            so.ApplyModifiedPropertiesWithoutUndo();

            uiGo.AddComponent(controllerType);

            // EventSystem (Input System) para que la UI reciba clicks en runtime
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            EditorUtility.SetDirty(doc);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{sceneName}.unity");
        }
    }
}
