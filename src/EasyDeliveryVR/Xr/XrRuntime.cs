using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace EasyDeliveryVR.Xr
{
    /// <summary>
    /// Injects Unity's OpenXR plugin at runtime. The game ships without XR, so we create the
    /// XR Management / OpenXR ScriptableObjects ourselves. Everything goes through reflection so the
    /// plugin builds (and the game runs flat) without Unity's XR assemblies present.
    /// Required files are installed by tools/Install-XrRuntime.ps1.
    /// </summary>
    internal static class XrRuntime
    {
        public static bool Running { get; private set; }

        private static object _manager;

        private const string OpenXrAsm = "Unity.XR.OpenXR";
        private const string MgmtAsm = "Unity.XR.Management";

        private static readonly string[] InteractionProfiles =
        {
            "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.ValveIndexControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.HTCViveControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.KHRSimpleControllerProfile",
        };

        public static string XrDir =>
            Path.Combine(Path.GetDirectoryName(typeof(XrRuntime).Assembly.Location), "XR");

        public static bool Start()
        {
            try
            {
                if (!LoadAssemblies()) return false;
                if (!CheckNativeFiles()) return false;

                CreateOpenXrSettings();

                var managerType = FindType(MgmtAsm, "UnityEngine.XR.Management.XRManagerSettings");
                var loaderType = FindType(OpenXrAsm, "UnityEngine.XR.OpenXR.OpenXRLoader");
                var generalType = FindType(MgmtAsm, "UnityEngine.XR.Management.XRGeneralSettings");

                _manager = ScriptableObject.CreateInstance(managerType);
                var loader = ScriptableObject.CreateInstance(loaderType);
                UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)_manager);
                UnityEngine.Object.DontDestroyOnLoad(loader);

                bool added = (bool)managerType.GetMethod("TryAddLoader")
                    .Invoke(_manager, new object[] { loader, -1 });
                if (!added) { Plugin.Log.LogError("XR: could not add OpenXR loader"); return false; }

                // Make XRGeneralSettings.Instance valid for any code that asks for it.
                var general = ScriptableObject.CreateInstance(generalType);
                UnityEngine.Object.DontDestroyOnLoad(general);
                generalType.GetField("m_LoaderManagerInstance", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(general, _manager);
                generalType.GetField("s_RuntimeSettingsInstance", BindingFlags.Static | BindingFlags.NonPublic)
                    ?.SetValue(null, general);

                managerType.GetMethod("InitializeLoaderSync").Invoke(_manager, null);
                var active = managerType.GetProperty("activeLoader").GetValue(_manager);
                if (active == null)
                {
                    Plugin.Log.LogError("XR: OpenXR loader failed to initialize. Is SteamVR / Virtual Desktop running and set as the OpenXR runtime?");
                    return false;
                }

                managerType.GetMethod("StartSubsystems").Invoke(_manager, null);
                Running = true;

                LogRuntimeInfo();
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("XR: startup failed: " + (e.InnerException ?? e));
                return false;
            }
        }

        public static void Stop()
        {
            if (_manager == null) return;
            try
            {
                var t = _manager.GetType();
                t.GetMethod("StopSubsystems").Invoke(_manager, null);
                t.GetMethod("DeinitializeLoader").Invoke(_manager, null);
            }
            catch (Exception e) { Plugin.Log.LogWarning("XR: shutdown error: " + e.Message); }
            Running = false;
        }

        private static bool LoadAssemblies()
        {
            var dir = XrDir;
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning($"XR: folder not found ({dir}). Run tools/Install-XrRuntime.ps1. Running flat.");
                return false;
            }

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var file = Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };

            // Load dependencies first (CoreUtils, Management), OpenXR last.
            var files = Directory.GetFiles(dir, "*.dll")
                .OrderBy(f => f.Contains("OpenXR") ? 1 : 0)
                .ToArray();
            foreach (var f in files)
            {
                try { Assembly.LoadFrom(f); }
                catch (Exception e) { Plugin.Log.LogError($"XR: failed to load {Path.GetFileName(f)}: {e.Message}"); return false; }
            }
            Plugin.Log.LogInfo("XR: loaded " + string.Join(", ", files.Select(Path.GetFileName)));
            return true;
        }

        private static bool CheckNativeFiles()
        {
            var data = Application.dataPath;
            var native = Path.Combine(data, "Plugins/x86_64/UnityOpenXR.dll");
            var manifest = Path.Combine(data, "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json");
            bool ok = true;
            if (!File.Exists(native)) { Plugin.Log.LogError("XR: missing " + native); ok = false; }
            if (!File.Exists(manifest)) { Plugin.Log.LogError("XR: missing " + manifest); ok = false; }
            if (!ok) Plugin.Log.LogError("XR: run tools/Install-XrRuntime.ps1 (and restart the game).");
            return ok;
        }

        private static void CreateOpenXrSettings()
        {
            var settingsType = FindType(OpenXrAsm, "UnityEngine.XR.OpenXR.OpenXRSettings");
            var featureType = FindType(OpenXrAsm, "UnityEngine.XR.OpenXR.Features.OpenXRFeature");

            // In a player build OpenXRSettings.Awake registers the instance as OpenXRSettings.Instance.
            var settings = ScriptableObject.CreateInstance(settingsType);
            UnityEngine.Object.DontDestroyOnLoad(settings);

            var list = new System.Collections.Generic.List<object>();
            foreach (var name in InteractionProfiles)
            {
                var t = FindType(OpenXrAsm, name, required: false);
                if (t == null) { Plugin.Log.LogInfo("XR: profile not available: " + name); continue; }
                var f = ScriptableObject.CreateInstance(t);
                UnityEngine.Object.DontDestroyOnLoad(f);
                featureType.GetProperty("enabled").SetValue(f, true);
                list.Add(f);
            }
            var arr = Array.CreateInstance(featureType, list.Count);
            for (int i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
            settingsType.GetField("features", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(settings, arr);

            // Multi-pass: the game's shaders were built without single-pass-instanced stereo variants.
            var renderModeProp = settingsType.GetProperty("renderMode");
            if (renderModeProp != null)
                renderModeProp.SetValue(settings, Enum.Parse(renderModeProp.PropertyType, "MultiPass"));

            Plugin.Log.LogInfo($"XR: OpenXR settings created with {list.Count} interaction profiles, multi-pass");
        }

        private static void LogRuntimeInfo()
        {
            var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            var d = displays.FirstOrDefault(s => s.running);
            Plugin.Log.LogInfo($"XR: running. Display subsystem: {(d != null ? d.subsystemDescriptor.id : "none")}, device: {XRSettings.loadedDeviceName}");

            try
            {
                var rt = FindType(OpenXrAsm, "UnityEngine.XR.OpenXR.OpenXRRuntime", required: false);
                if (rt != null)
                    Plugin.Log.LogInfo($"XR: OpenXR runtime: {rt.GetProperty("name")?.GetValue(null)} {rt.GetProperty("version")?.GetValue(null)}");
            }
            catch { /* informational only */ }
        }

        private static Type FindType(string asm, string fullName, bool required = true)
        {
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == asm)
                .Select(a => a.GetType(fullName))
                .FirstOrDefault(x => x != null);
            if (t == null && required) throw new Exception($"type {fullName} not found in {asm}");
            return t;
        }
    }
}
