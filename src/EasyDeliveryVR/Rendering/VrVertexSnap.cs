using System.Collections.Generic;
using UnityEngine;

namespace EasyDeliveryVR.Rendering
{
    /// <summary>
    /// The game's retro shader ("Shader Graphs/TestingShader") snaps vertices to a coarse grid (_VertexResolution,
    /// 50 = 2 cm steps). On a flat 456x256 image that's a subtle wobble; in VR every small head or hand movement
    /// makes nearby objects (carried package, cab interior, screens) visibly jump. Comfort/RetroVertexSnap turns
    /// it back on. Materials are patched in memory only.
    /// </summary>
    internal class VrVertexSnap : MonoBehaviour
    {
        private static readonly int Prop = Shader.PropertyToID("_VertexResolution");
        private static readonly Vector4 Off = new Vector4(100000f, 100000f, 100000f, 0f);

        private readonly Dictionary<Material, Vector4> _original = new Dictionary<Material, Vector4>();
        private float _nextScan;
        private bool _applied;

        private void Update()
        {
            bool want = !VrConfig.RetroVertexSnap.Value;
            if (want != _applied) { _applied = want; _nextScan = 0f; if (!want) Restore(); }
            if (!want || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 3f; // new materials appear with scene loads and spawned objects

            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || _original.ContainsKey(m) || !m.HasProperty(Prop)) continue;
                _original[m] = m.GetVector(Prop);
                m.SetVector(Prop, Off);
                n++;
            }
            if (n > 0) Plugin.Log.LogInfo($"VR: retro vertex snapping off on {n} material(s)");
        }

        private void Restore()
        {
            foreach (var kv in _original)
                if (kv.Key != null) kv.Key.SetVector(Prop, kv.Value);
            _original.Clear();
        }
    }
}
