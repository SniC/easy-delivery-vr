using System.Collections.Generic;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyDeliveryVR.Ui
{
    /// <summary>
    /// In the cab, shows the HUD's truck info on a small display above the steering wheel instead of on the
    /// floating HUD: fuel, energy, money and time (left) and the radio (right). The game draws its whole HUD
    /// into one texture (sHUD.R, 456x256 with 68 px side bars); the display shows crops of that texture, and
    /// the floating HUD copy gets a mesh with those areas cut out. Markers, warnings and messages stay floating.
    /// </summary>
    internal class VrDashboard : MonoBehaviour
    {
        // Crops in HUD pixels (x right, y down), from sHUD.FrameUpdate / FuelDisplay / EnergyDisplay / RadioDisplay.
        // Width/height relative to the texture's edges so it survives a different HUD size.
        private static Rect GaugeRect(int w, int h) => Rect.MinMaxRect(68, h / 2 - 30, 140, h - 40);    // energy, fuel, delivery distance, money, time
        private static Rect RadioRect(int w, int h) => Rect.MinMaxRect(w - 212, h - 80, w - 68, h - 36); // scan, fm frequency, song

        /// <summary>
        /// Drawn after the opaque world without depth test, so the dashboard and wheel can't hide it;
        /// the VR hands render after it (VrHands.HandQueue) and stay in front.
        /// </summary>
        public const int Queue = 2500;

        private const float MetersPerPixel = 0.0012f;

        private sHUD _hud;
        private InteriorInteraction _wheel;
        private float _nextSearch;

        private Transform _root;
        private Renderer _gauges, _radio;
        private Material _mat;
        private Mesh _gaugeMesh, _radioMesh, _holedMesh, _originalMesh;
        private Vector2Int _meshSize;
        private Renderer _floatingCopy;

        private void LateUpdate()
        {
            if (!XrRuntime.Running) return;
            if ((_hud == null || _wheel == null) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _hud = FindFirstObjectByType<sHUD>();
                _wheel = FindFirstObjectByType<InteriorInteraction>();
            }

            var rt = _hud != null && _hud.R != null ? _hud.R.rt : null;
            Renderer src = null, copy = null;
            bool have = rt != null && VrScreenMirror.TryGetCopy(rt, out src, out copy);
            bool on = have && VrConfig.DashboardHud.Value && _wheel != null && _wheel.steeringWheel != null
                      && VrCameraManager.Mode == VrCameraManager.ViewMode.Cab;

            if (_floatingCopy != null && _floatingCopy != copy) RestoreFloating();
            if (!on)
            {
                RestoreFloating();
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }

            if (_root == null) Build();
            if (_meshSize != new Vector2Int(rt.width, rt.height)) BuildMeshes(rt.width, rt.height);

            // Floating HUD: same texture, minus the parts shown on the dashboard.
            var mf = copy.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != _holedMesh)
            {
                _originalMesh = mf.sharedMesh;
                mf.sharedMesh = _holedMesh;
                if (_originalMesh != null)
                    Plugin.Log.LogInfo($"VR dashboard: HUD {rt.width}x{rt.height}, floating quad mesh '{_originalMesh.name}' " +
                                       $"verts [{string.Join(" ", _originalMesh.vertices)}] uv [{string.Join(" ", _originalMesh.uv)}]");
            }
            _floatingCopy = copy;

            // Same look as the game's HUD material.
            var sm = src.sharedMaterial;
            if (_mat.shader != sm.shader) _mat.shader = sm.shader;
            _mat.CopyPropertiesFromMaterial(sm);
            _mat.renderQueue = Queue + 1;
            if (_mat.HasProperty("_ZTest")) _mat.SetInt("_ZTest", (int)CompareFunction.Always);
            _mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

            bool visible = copy.enabled; // follows the HUD (hidden while paused, in cam.exe, ...)
            _root.gameObject.SetActive(visible);
            if (visible) Place();
        }

        private void OnDisable() => RestoreFloating();

        private void RestoreFloating()
        {
            if (_floatingCopy != null && _originalMesh != null)
            {
                var mf = _floatingCopy.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == _holedMesh) mf.sharedMesh = _originalMesh;
            }
            _floatingCopy = null;
        }

        /// <summary>Above the steering wheel's rim, a little toward the windshield, facing the driver's seat.</summary>
        private void Place()
        {
            var wt = _wheel.steeringWheel;
            var car = _wheel.car != null ? _wheel.car.transform : wt.root;
            var seat = VrCameraManager.Anchor != null ? VrCameraManager.Anchor.position : wt.position - car.forward * 0.6f + car.up * 0.3f;

            var axis = wt.up;
            if (Vector3.Dot(axis, seat - wt.position) < 0f) axis = -axis; // from the wheel toward the driver
            float radius = 0.2f;
            foreach (var r in wt.GetComponentsInChildren<Renderer>()) radius = Mathf.Max(radius, Mathf.Min(0.3f, r.bounds.extents.y));

            var up = car.up;
            var center = wt.position + up * (radius + 0.06f + VrConfig.DashboardHeight.Value) - axis * 0.06f;
            var eye = seat + up * 0.05f;
            _root.SetPositionAndRotation(center, Quaternion.LookRotation(center - eye, up));
        }

        private void Build()
        {
            _root = new GameObject("VR Dashboard HUD").transform;
            DontDestroyOnLoad(_root.gameObject);
            _mat = new Material(Shader.Find("Sprites/Default"));
            _gauges = MakePanel("Gauges");
            _radio = MakePanel("Radio");
        }

        private Renderer MakePanel(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // Dark backing so the pixel text reads against the dashboard.
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(bg.GetComponent<Collider>());
            bg.name = "Backing";
            bg.transform.SetParent(go.transform, false);
            var bgMat = new Material(Shader.Find("UI/Default")) { color = new Color(0.02f, 0.03f, 0.03f, 0.85f), renderQueue = Queue };
            bgMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            var br = bg.GetComponent<Renderer>();
            br.sharedMaterial = bgMat;
            br.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        private void BuildMeshes(int w, int h)
        {
            _meshSize = new Vector2Int(w, h);
            var g = GaugeRect(w, h);
            var rr = RadioRect(w, h);
            const float gap = 0.03f;

            float gw = g.width * MetersPerPixel, rw = rr.width * MetersPerPixel;
            float total = gw + gap + rw;
            SetPanel(_gauges, ref _gaugeMesh, g, w, h, new Vector3(-total / 2f + gw / 2f, 0f, 0f));
            SetPanel(_radio, ref _radioMesh, rr, w, h, new Vector3(total / 2f - rw / 2f, 0f, 0f));

            _holedMesh = HoledQuad(new[] { Uv(g, w, h), Uv(rr, w, h) });
        }

        private void SetPanel(Renderer r, ref Mesh mesh, Rect px, int w, int h, Vector3 offset)
        {
            float sx = px.width * MetersPerPixel, sy = px.height * MetersPerPixel;
            var uv = Uv(px, w, h);
            if (mesh == null) mesh = new Mesh { name = r.name };
            mesh.Clear();
            // Facing -z, toward the viewer (the root's +z points from the eye to the display).
            mesh.vertices = new[] { new Vector3(-sx / 2, -sy / 2), new Vector3(-sx / 2, sy / 2), new Vector3(sx / 2, sy / 2), new Vector3(sx / 2, -sy / 2) };
            mesh.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            r.GetComponent<MeshFilter>().sharedMesh = mesh;
            r.transform.localPosition = offset;

            var bg = r.transform.Find("Backing");
            bg.localPosition = new Vector3(0f, 0f, 0.003f);
            bg.localScale = new Vector3(sx + 0.012f, sy + 0.012f, 1f);
        }

        /// <summary>Pixel rect (y down) to texture UV rect (v up).</summary>
        private static Rect Uv(Rect px, int w, int h) =>
            Rect.MinMaxRect(px.xMin / w, 1f - px.yMax / h, px.xMax / w, 1f - px.yMin / h);

        /// <summary>A unit quad (-0.5..0.5, uv 0..1, like Unity's Quad) with the given UV rects cut out.</summary>
        private static Mesh HoledQuad(Rect[] holes)
        {
            var xs = new List<float> { 0f, 1f };
            var ys = new List<float> { 0f, 1f };
            foreach (var h in holes) { xs.Add(h.xMin); xs.Add(h.xMax); ys.Add(h.yMin); ys.Add(h.yMax); }
            xs.Sort(); ys.Sort();

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i + 1 < xs.Count; i++)
            for (int j = 0; j + 1 < ys.Count; j++)
            {
                float x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                if (x1 - x0 < 1e-5f || y1 - y0 < 1e-5f) continue;
                var mid = new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f);
                bool inHole = false;
                foreach (var h in holes) if (h.Contains(mid)) inHole = true;
                if (inHole) continue;
                int b = verts.Count;
                foreach (var p in new[] { new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0) })
                {
                    verts.Add(new Vector3(p.x - 0.5f, p.y - 0.5f, 0f));
                    uvs.Add(p);
                }
                // Same winding as Unity's built-in Quad (visible from -z).
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = "HUD (dashboard parts cut out)" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
