using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Menu Rubik → Build Windows: applies Player Settings and builds Builds/Windows/RubikSolver.exe.
public static class RubikBuild
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string OutputPath = "Builds/Windows/RubikSolver.exe";

    [MenuItem("Rubik/Build Windows")]
    private static void BuildWindows()
    {
        if (!File.Exists("Assets/Plugins/Rubik/Rubik.Solver3x3.dll"))
        {
            EditorUtility.DisplayDialog("Rubik", "Solver DLLs are missing. Run unity/sync-solver.ps1 first.", "OK");
            return;
        }

        ApplyProjectSettings();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Rubik] Build succeeded: {Path.GetFullPath(OutputPath)} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s)");
            EditorUtility.RevealInFinder(OutputPath);
        }
        else
        {
            Debug.LogError($"[Rubik] Build {summary.result}: {summary.totalErrors} errors, see Console");
        }
    }

    [MenuItem("Rubik/Apply Project Settings")]
    private static void ApplyProjectSettings()
    {
        PlayerSettings.productName = "Rubik Solver";
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        // Smooth sticker edges (MSAA 4x) in every URP asset of the project
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null || asset.msaaSampleCount == 4) continue;
            asset.msaaSampleCount = 4;
            EditorUtility.SetDirty(asset);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Rubik] Project settings applied");
    }
}
