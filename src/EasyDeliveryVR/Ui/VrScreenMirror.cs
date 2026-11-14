using System.Collections.Generic;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EasyDeliveryVR.Ui
{
    /// <summary>
    /// The game composes its HUD, menus, title screen and in-game computer as quads in front of a
    /// monitor-only camera ("PauseMenuURP/Camera Persp"), each showing a MiniRenderer texture.
    /// We mirror those quads into a virtual screen in front of the VR player, preserving the exact
    /// on-monitor layout (perspective-correct), and draw them on top of the world.
    /// The quad that shows the 3D world ("Screen" render texture) is skipped.
    /// </summary>
    internal class VrScreenMirror : MonoBehaviour
    {
        private class Copy
        {
            public Renderer Source, Target;
            public Material[] Mats;
        }

        private Camera _menuCam;
        private Transform _root;
        private readonly Dictionary<Renderer, Copy> _copies = new Dictionary<Renderer, Copy>();
        private float _nextScan;

        private const float ReferenceDepth = 10.9f; // depth of the game's main screen quad

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene s, LoadSceneMode m) => Clear();

        private void Clear()
        {
            foreach (var c in _copies.Values)
            {
                if (c.Target) Destroy(c.Target.gameObject);
                if (c.Mats != null) foreach (var mat in c.Mats) Destroy(mat);
            }
            _copies.Clear();
            _menuCam = null;
            _nextScan = 0;
        }

        private void LateUpdate()
        {
            if (!XrRuntime.Running) return;

            if (_root == null)
            {
                _root = new GameObject("VR Screen Mirror").transform;
                DontDestroyOnLoad(_root.gameObject);
            }

            if (_menuCam == null || Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                Scan();
            }
            if (_menuCam == null) { _root.gameObject.SetActive(false); return; }
            _root.gameObject.SetActive(true);

            PlaceRoot();
            Sync();
        }

        private void Scan()
        {
            if (_menuCam == null)
            {
                foreach (var p in FindObjectsByType<pixelPerfectView>(FindObjectsSortMode.None))
                {
                    var cam = p.GetComponentInParent<Camera>();
                    if (cam != null) { _menuCam = cam; break; }
                }
                if (_menuCam == null) return;
                Plugin.Log.LogInfo($"VR UI: mirroring screen layers of '{_menuCam.name}'");
            }

            foreach (var r in _menuCam.GetComponentsInChildren<Renderer>(true))
            {
                if (_copies.ContainsKey(r)) continue;
                if (IsWorldView(r)) { _copies[r] = new Copy { Source = r }; continue; }

                var go = new GameObject("VR_" + r.name) { layer = 0 };
                go.transform.SetParent(_root, false);
                Renderer target;
                if (r is SpriteRenderer)
                    target = go.AddComponent<SpriteRenderer>();
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf))
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    target = go.AddComponent<MeshRenderer>();
                }
                else { Destroy(go); _copies[r] = new Copy { Source = r }; continue; }

                target.shadowCastingMode = ShadowCastingMode.Off;
                target.receiveShadows = false;
                _copies[r] = new Copy { Source = r, Target = target };
                var m = r.sharedMaterial;
                Plugin.Log.LogInfo($"VR UI: mirrored '{r.name}' ({r.GetType().Name}, shader {(m ? m.shader.name : "-")})");
            }
        }

        private static bool IsWorldView(Renderer r)
        {
            if (r.name == "Screen") return true; // the quad showing the 3D world render texture
            var m = r.sharedMaterial;
            if (m == null) return false;
            foreach (var prop in new[] { "_MainTex", "_BaseMap" })
                if (m.HasProperty(prop) && m.GetTexture(prop) is RenderTexture rt && rt.name == "Screen")
                    return true;
            return false;
        }

        /// <summary>The virtual screen hangs in front of the recentered forward direction of the current VR anchor.</summary>
        private void PlaceRoot()
        {
            var anchor = VrCameraManager.Anchor;
            if (anchor == null) return;
            if (_root.parent != anchor)
                _root.SetParent(anchor, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
        }

        private void Sync()
        {
            var camT = _menuCam.transform;
            float dist = VrConfig.ScreenDistance.Value;

            foreach (var c in _copies.Values)
            {
                if (c.Target == null) continue;
                var src = c.Source;
                bool visible = src != null && src.enabled && src.gameObject.activeInHierarchy;
                c.Target.enabled = visible;
                if (!visible) continue;

                // Project the source onto the virtual screen plane: keep its angular position and size,
                // flatten depth to the screen distance (keeping the original front-to-back order).
                var lp = camT.InverseTransformPoint(src.transform.position);
                float z = Mathf.Max(lp.z, 0.05f);
                float k = dist / z;
                float depth = dist + (z - ReferenceDepth) * 0.01f;
                var t = c.Target.transform;
                t.localPosition = new Vector3(lp.x * k, lp.y * k, depth);
                t.localRotation = Quaternion.Inverse(camT.rotation) * src.transform.rotation;
                t.localScale = src.transform.lossyScale * k;

                SyncMaterials(c, z);

                if (src is SpriteRenderer ss && c.Target is SpriteRenderer ts)
                {
                    ts.sprite = ss.sprite;
                    ts.color = ss.color;
                    ts.flipX = ss.flipX;
                    ts.flipY = ss.flipY;
                }
            }
        }

        /// <summary>Material copies so we can draw on top of the world without touching the monitor's materials.</summary>
        private static void SyncMaterials(Copy c, float originalDepth)
        {
            var src = c.Source.sharedMaterials;
            if (c.Mats == null || c.Mats.Length != src.Length)
            {
                if (c.Mats != null) foreach (var m in c.Mats) Destroy(m);
                c.Mats = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                    c.Mats[i] = src[i] != null ? new Material(src[i]) : null;
                c.Target.sharedMaterials = c.Mats;
            }

            for (int i = 0; i < src.Length; i++)
            {
                var m = c.Mats[i];
                if (m == null || src[i] == null) continue;
                if (m.shader != src[i].shader) m.shader = src[i].shader;
                m.CopyPropertiesFromMaterial(src[i]);
                // Overlay: after all world geometry, farther layers first, ignore depth.
                m.renderQueue = 4000 + Mathf.Clamp(Mathf.RoundToInt(100f - originalDepth * 5f), 0, 499);
                if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)CompareFunction.Always);
                m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            }
        }
    }
}
