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
                if (cmd == "snapshot") Take("cmd");
                else if (cmd == "settings") Ui.VrSettingsMenu.Toggle();
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
