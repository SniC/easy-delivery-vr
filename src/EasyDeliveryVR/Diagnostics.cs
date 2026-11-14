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
