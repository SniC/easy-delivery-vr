using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

/// <summary>
/// Builds an empty Windows player with OpenXR enabled, purely to harvest the XR runtime files
/// (Unity.XR.*.dll, UnityOpenXR.dll, openxr_loader.dll, UnitySubsystemsManifest.json) for the mod.
/// Run headless via tools/Install-XrRuntime.ps1, or from the menu: Easy Delivery VR > Build XR Runtime.
/// </summary>
public static class BuildXrRuntime
{
    private const string OutDir = "Build";
    private const string ScenePath = "Assets/Empty.unity";

    [MenuItem("Easy Delivery VR/Build XR Runtime")]
    public static void Build()
    {
        var group = BuildTargetGroup.Standalone;

        // 1. XR Plug-in Management: assign the OpenXR loader for Standalone.
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
        var general = perTarget.SettingsForBuildTarget(group);
        general.InitManagerOnStart = false;
        XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);

        // 2. OpenXR settings (the mod recreates these at runtime; set here so validation passes).
        FeatureHelpers.RefreshFeatures(group);
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        openxr.renderMode = OpenXRSettings.RenderMode.MultiPass;
        foreach (var f in openxr.GetFeatures<OculusTouchControllerProfile>()) f.enabled = true;
        foreach (var f in openxr.GetFeatures<ValveIndexControllerProfile>()) f.enabled = true;
        foreach (var f in openxr.GetFeatures<HTCViveControllerProfile>()) f.enabled = true;
        EditorUtility.SetDirty(openxr);

        // 3. Player settings matching the game: Mono, x64.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        AssetDatabase.SaveAssets();

        // 4. Build an empty scene.
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(OutDir, "XrRuntime.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = group,
            options = BuildOptions.None,
        });

        Debug.Log($"[BuildXrRuntime] Result: {report.summary.result}, output: {report.summary.outputPath}");
        if (Application.isBatchMode)
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
