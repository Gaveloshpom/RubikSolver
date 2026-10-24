using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Build menu:
//   Rubik → Build Windows   — Builds/Windows/RubikSolver.exe for quick local testing
//   Rubik → Build Release   — Builds/Release/RubikSolver-<version>-win64.zip for GitHub Releases
//   Rubik → Build Web       — Builds/Web, a static site for GitHub Pages
public static class RubikBuild
{
    public const string Version = "1.0.0";

    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string ExeName = "RubikSolver.exe";
    private const string WebTemplate = "PROJECT:Responsive";

    [MenuItem("Rubik/Build Windows")]
    private static void BuildWindows()
    {
        if (Build(BuildTarget.StandaloneWindows64, "Builds/Windows/" + ExeName))
            EditorUtility.RevealInFinder("Builds/Windows/" + ExeName);
    }

    [MenuItem("Rubik/Build Release")]
    private static void BuildRelease()
    {
        const string staging = "Builds/Release/win64";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        if (!Build(BuildTarget.StandaloneWindows64, $"{staging}/{ExeName}")) return;

        // Unity puts debug-only folders next to the player; they must not ship
        foreach (var dir in Directory.GetDirectories(staging))
        {
            string name = Path.GetFileName(dir);
            if (name.EndsWith("_DoNotShip") || name.EndsWith("_ButDontShipItWithYourGame"))
                Directory.Delete(dir, true);
        }

        string zip = $"Builds/Release/RubikSolver-{Version}-win64.zip";
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(staging, zip, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
        Debug.Log($"[Rubik] Release archive: {Path.GetFullPath(zip)} ({new FileInfo(zip).Length / (1024 * 1024)} MB)");
        EditorUtility.RevealInFinder(zip);
    }

    [MenuItem("Rubik/Build Web")]
    private static void BuildWeb()
    {
        const string output = "Builds/Web";
        if (Directory.Exists(output)) Directory.Delete(output, true);

        // Switch first: switching the active platform reloads player settings, which would drop
        // the WebGL values below if they were set before the switch
        if (!SwitchTo(BuildTargetGroup.WebGL, BuildTarget.WebGL)) return;

        // GitHub Pages cannot send Content-Encoding headers, so the loader decompresses gzip itself
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.template = WebTemplate;
        PlayerSettings.WebGL.dataCaching = true;

        AssetDatabase.SaveAssets();

        if (!Build(BuildTarget.WebGL, output)) return;
        if (!File.ReadAllText(Path.Combine(output, "index.html")).Contains("unity-canvas\" tabindex"))
            Debug.LogWarning("[Rubik] The web build did not use the Responsive template, check Player Settings > Resolution and Presentation");
        File.WriteAllText(Path.Combine(output, ".nojekyll"), ""); // serve files as they are on GitHub Pages
        EditorUtility.RevealInFinder(Path.Combine(output, "index.html"));
    }

    private static bool SwitchTo(BuildTargetGroup group, BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target) return true;
        if (EditorUserBuildSettings.SwitchActiveBuildTarget(group, target)) return true;
        Debug.LogError($"[Rubik] Could not switch the active platform to {target}");
        return false;
    }

    private static bool Build(BuildTarget target, string location)
    {
        var group = target == BuildTarget.WebGL ? BuildTargetGroup.WebGL : BuildTargetGroup.Standalone;
        if (!SwitchTo(group, target)) return false;

        if (!File.Exists("Assets/Plugins/Rubik/Rubik.Solver3x3.dll"))
        {
            EditorUtility.DisplayDialog("Rubik", "Solver DLLs are missing. Run unity/sync-solver.ps1 first.", "OK");
            return false;
        }

        ApplyProjectSettings();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = location,
            target = target,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"[Rubik] {target} build {summary.result}: {summary.totalErrors} errors, see Console");
            return false;
        }
        Debug.Log($"[Rubik] {target} build succeeded: {Path.GetFullPath(location)} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s)");
        return true;
    }

    [MenuItem("Rubik/Apply Project Settings")]
    private static void ApplyProjectSettings()
    {
        PlayerSettings.productName = "Rubik Solver";
        PlayerSettings.bundleVersion = Version;
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
