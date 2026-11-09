using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Menu Rubik → Create Main Scene: creates Assets/Scenes/Main.unity with a camera, a light, the cube and the UI.
public static class RubikSceneSetup
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Rubik/Create Main Scene")]
    private static void CreateMainScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Rubik", $"{ScenePath} already exists. Overwrite?", "Overwrite", "Cancel"))
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        cam.fieldOfView = 40f;
        cam.gameObject.AddComponent<OrbitCamera>();

        var light = Object.FindAnyObjectByType<Light>();
        if (light != null)
        {
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
        }

        var cubeGo = new GameObject("Cube");
        var view = cubeGo.AddComponent<CubeView>(); // draws itself with the Rubik/CubeFace shader from Resources

        var app = new GameObject("App").AddComponent<CubeAppController>();
        app.view = view;

        var uiGo = new GameObject("UI");
        var document = uiGo.AddComponent<UIDocument>();
        document.panelSettings = LoadOrCreatePanelSettings();
        uiGo.AddComponent<CubeUI>().app = app;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[Rubik] Created {ScenePath}. Press Play.");
    }

    private static PanelSettings LoadOrCreatePanelSettings()
    {
        const string path = "Assets/UI/RubikPanelSettings.asset";
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
        if (settings != null) return settings;

        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI/RubikTheme.tss");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1600, 900);
        settings.match = 0.5f;
        AssetDatabase.CreateAsset(settings, path);
        return settings;
    }
}
