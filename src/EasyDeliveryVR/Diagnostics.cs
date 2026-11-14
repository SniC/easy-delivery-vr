using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace EasyDeliveryVR
{
    /// <summary>Logs the camera setup on every scene load (and on F7) to help map the game's render pipeline.</summary>
    internal class Diagnostics : MonoBehaviour
    {
        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene s, LoadSceneMode m) => Invoke(nameof(DumpCameras), 2f);

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) DumpCameras();
        }

        private void DumpCameras()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Cameras in '{SceneManager.GetActiveScene().name}':");
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (!c.gameObject.scene.IsValid()) continue;
                var rt = c.targetTexture;
                sb.AppendLine($"  {Path(c.transform)} active={c.isActiveAndEnabled} depth={c.depth} fov={c.fieldOfView:F0} " +
                              $"rt={(rt ? $"{rt.name} {rt.width}x{rt.height}" : "screen")} mask=0x{c.cullingMask:X} eye={c.stereoTargetEye}");
            }
            foreach (var p in Resources.FindObjectsOfTypeAll<pixelPerfectView>())
                if (p.gameObject.scene.IsValid()) sb.AppendLine($"  pixelPerfectView: {Path(p.transform)} z={p.transform.localPosition.z}");

            // Renderers close to the world camera, to identify objects that look wrong in VR.
            var cam = sCameraSystem.cam != null ? sCameraSystem.cam : Camera.main;
            foreach (var c in Camera.allCameras)
                if (c.stereoTargetEye == StereoTargetEyeMask.Both && c.targetTexture == null) cam = c;
            if (cam != null)
            {
                sb.AppendLine($"Renderers within 6 m of '{Path(cam.transform)}' (mask 0x{cam.cullingMask:X}):");
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    float d = Vector3.Distance(r.bounds.ClosestPoint(cam.transform.position), cam.transform.position);
                    if (d > 6f) continue;
                    bool visible = (cam.cullingMask & (1 << r.gameObject.layer)) != 0;
                    var mat = r.sharedMaterial;
                    var tex = mat != null && mat.HasProperty("_MainTex") ? mat.mainTexture : null;
                    if (tex == null && mat != null && mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                    sb.AppendLine($"  {d:F1}m {Path(r.transform)} layer={LayerMask.LayerToName(r.gameObject.layer)}({r.gameObject.layer}) seen={visible} " +
                                  $"mat={(mat ? mat.name : "-")} shader={(mat ? mat.shader.name : "-")} tex={(tex ? tex.name : "-")} size={r.bounds.size}");
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            while ((t = t.parent) != null) s = t.name + "/" + s;
            return s;
        }
    }
}
