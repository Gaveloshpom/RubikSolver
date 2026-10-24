using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Меню Rubik → Create Main Scene: створює Assets/Scenes/Main.unity з камерою, світлом і кубом.
public static class RubikSceneSetup
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string MaterialsFolder = "Assets/Materials";

    [MenuItem("Rubik/Create Main Scene")]
    private static void CreateMainScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Rubik", $"{ScenePath} already exists. Overwrite?", "Overwrite", "Cancel"))
            return;

        var stickerMat = LoadOrCreateMaterial("Sticker", Color.white, 0.6f);
        var bodyMat = LoadOrCreateMaterial("CubeBody", new Color(0.06f, 0.06f, 0.07f), 0.35f);

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
        var view = cubeGo.AddComponent<CubeView>();
        view.stickerMaterial = stickerMat;
        view.bodyMaterial = bodyMat;

        var app = new GameObject("App").AddComponent<CubeAppController>();
        app.view = view;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log($"[Rubik] Created {ScenePath}. Press Play.");
    }

    private static Material LoadOrCreateMaterial(string name, Color color, float smoothness)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets", "Materials");
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
