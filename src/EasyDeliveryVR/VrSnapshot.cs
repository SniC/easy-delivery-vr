using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace EasyDeliveryVR
{
    /// <summary>
    /// Debug aid: saves a flat (mono) render of the VR camera, including the mirrored UI, to
    /// BepInEx/vr_snapshots/. Automatic a few seconds after each scene load (config) and on F9.
    /// Briefly redirects the camera into a texture for one frame, so the headset may blink once.
    /// </summary>
    internal class VrSnapshot : MonoBehaviour
    {
        private bool _busy;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (VrConfig.DebugSnapshots.Value) StartCoroutine(Delayed(s.name));
        }

        private IEnumerator Delayed(string scene)
        {
            yield return new WaitForSecondsRealtime(8f);
            if (SceneManager.GetActiveScene().name == scene) Take(scene);
        }

        private float _nextCmdCheck;

        private static void DumpTree(string label, Transform root)
        {
            if (root == null) { Plugin.Log.LogInfo($"VR {label}: none"); return; }
            Plugin.Log.LogInfo($"VR {label}: parent path '{TPath(root.parent)}'");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var r = t.GetComponent<Renderer>();
                string info = "";
                if (r is SkinnedMeshRenderer smr)
                    info = $" SKINNED mesh={smr.sharedMesh?.name} bones={smr.bones?.Length} rootBone={smr.rootBone?.name}";
                else if (r != null)
                    info = $" {r.GetType().Name} mesh={t.GetComponent<MeshFilter>()?.sharedMesh?.name} bounds={r.bounds.size}";
                if (r != null) info += $" mat={r.sharedMaterial?.name}/{r.sharedMaterial?.shader.name} enabled={r.enabled}";
                Plugin.Log.LogInfo($"VR {label}:  {TPath(t)} active={t.gameObject.activeInHierarchy} lpos={t.localPosition} lrot={t.localEulerAngles} lscale={t.localScale}{info}");
            }
        }

        private static string TPath(Transform t) => t == null ? "" : (t.parent == null ? t.name : TPath(t.parent) + "/" + t.name);

        /// <summary>Logs every shader used by active renderers, with its properties and a sample object.</summary>
        private static void DumpShaders()
        {
            var seen = new System.Collections.Generic.HashSet<Shader>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || !seen.Add(m.shader)) continue;
                    var s = m.shader;
                    var props = new System.Text.StringBuilder();
                    for (int i = 0; i < s.GetPropertyCount(); i++)
                    {
                        var n = s.GetPropertyName(i);
                        props.Append(n);
                        if (s.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Float || s.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Range)
                            props.Append('=').Append(m.GetFloat(n));
                        else if (s.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Vector)
                            props.Append('=').Append(m.GetVector(n));
                        props.Append(' ');
                    }
                    Plugin.Log.LogInfo($"VR shader '{s.name}' on '{r.name}' kw=[{string.Join(",", m.shaderKeywords)}] props: {props}");
                }
            foreach (var n in new[] { "_Resolution", "_SnapResolution", "_ScreenRes", "_PixelSize", "_Snap", "_VertexSnap", "_Jitter" })
                Plugin.Log.LogInfo($"VR global {n} = {Shader.GetGlobalFloat(n)} / {Shader.GetGlobalVector(n)}");
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) Take("manual");

            // Debug hook for development: commands written to BepInEx/vr_cmd.txt (one per line), e.g. "settings", "snapshot".
            if (Time.unscaledTime < _nextCmdCheck) return;
            _nextCmdCheck = Time.unscaledTime + 0.5f;
            var file = Path.Combine(BepInEx.Paths.BepInExRootPath, "vr_cmd.txt");
            if (!File.Exists(file)) return;
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
            foreach (var raw in lines)
            {
                var cmd = raw.Trim().ToLowerInvariant();
                Plugin.Log.LogInfo("VR debug command: " + cmd);
                try { RunCommand(cmd); }
                catch (System.Exception e) { Plugin.Log.LogError($"VR debug command '{cmd}' failed: {e}"); }
            }
        }

        private void RunCommand(string cmd)
        {
            {
                if (cmd == "snapshot") Take("cmd");
                else if (cmd == "settings") Ui.VrSettingsMenu.Toggle();
                else if (cmd == "shaders") DumpShaders();
                else if (cmd == "perf") Rendering.VrPerf.LogNow(true);
                else if (cmd == "continue") // title screen "continue"
                {
                    var intro = FindFirstObjectByType<IntroDotExe>(FindObjectsInactive.Include);
                    Plugin.Log.LogInfo("VR: continue -> " + (intro ? intro.name : "no IntroDotExe"));
                    if (intro)
                    {
                        // Same as IntroDotExe.LoadDelivery, without the fade (SceneTransition isn't always set up here).
                        int scene = sSaveSystem.GetInt("deliveryCurrentLastMapBuildIndex", intro.deliverySceneIndex);
                        sSaveSystem.SetBool("deliveryLoadedFromMenu", data: true);
                        if (PauseSystem.paused) PauseSystem.pauseSystem.TogglePaused();
                        Plugin.Log.LogInfo($"VR: continue loads scene {scene} ({SceneUtility.GetScenePathByBuildIndex(scene)}), current {SceneManager.GetActiveScene().buildIndex}");
                        SceneManager.LoadScene(scene);
                    }
                }
                else if (cmd == "controls")
                {
                    foreach (var cd in Resources.FindObjectsOfTypeAll<sControlsDisplay>())
                        for (int i = 0; i < cd.mappings.Length; i++)
                            Plugin.Log.LogInfo($"VR controls '{cd.name}' #{i}: label='{cd.mappings[i].label}' keyboard='{cd.mappings[i].keyboard}'");
                }
                else if (cmd == "hands")
                {
                    foreach (var ii in FindObjectsByType<InteriorInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        DumpTree("cab hand", ii.hand);
                    foreach (var ph in FindObjectsByType<sPlayerHands>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        DumpTree("player left", ph.leftHand ? ph.leftHand.transform : null);
                        DumpTree("player right", ph.rightHand ? ph.rightHand.transform : null);
                    }
                }
                else if (cmd == "nav")
                {
                    var jb = FindFirstObjectByType<jobBoard>();
                    var nav = jb != null ? jb.navigation : FindFirstObjectByType<sPathFinder>();
                    Plugin.Log.LogInfo($"VR nav: jobBoard={(jb ? $"progress={jb.progress} payload={(jb.currentPayload ? jb.currentPayload.name : "-")}" : "-")} " +
                                       $"dest={(nav && nav.dest ? $"{nav.dest.name} at {nav.dest.transform.position} destination={nav.dest.destination}" : "-")} " +
                                       $"car={(nav && nav.car ? nav.car.transform.position.ToString() : "-")}");
                }
            }
        }

        public void Take(string label)
        {
            if (!_busy) StartCoroutine(Capture(label));
        }

        private IEnumerator Capture(string label)
        {
            Camera cam = null;
            foreach (var c in Camera.allCameras)
                if (c.stereoTargetEye == StereoTargetEyeMask.Both && c.targetTexture == null) { cam = c; break; }
            if (cam == null) yield break;

            _busy = true;
            var rt = new RenderTexture(1280, 960, 24);
            cam.targetTexture = rt; // VrCameraManager restores null on its next Update
            yield return new WaitForEndOfFrame();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            if (cam.targetTexture == rt) cam.targetTexture = null;

            var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "vr_snapshots");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{System.DateTime.Now:HHmmss}_{SceneManager.GetActiveScene().name}_{label}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            Destroy(rt);
            Plugin.Log.LogInfo("VR: snapshot saved " + file);
            _busy = false;
        }
    }
}
