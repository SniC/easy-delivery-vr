using System.Collections.Generic;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyDeliveryVR.Ui
{
    /// <summary>
    /// In the cab, shows the HUD's truck info on two screens built into the dashboard instead of on the
    /// floating HUD: fuel, energy, money and time behind the steering wheel, the radio in the middle of the dash. The game draws its whole HUD
    /// into one texture (sHUD.R, 456x256 with 68 px side bars); the display shows crops of that texture, and
    /// the floating HUD copy gets a mesh with those areas cut out. Markers, warnings and messages stay floating.
    /// </summary>
    internal class VrDashboard : MonoBehaviour
    {
        // The crops (HUD pixels, from sHUD.FrameUpdate / FuelDisplay / EnergyDisplay / RadioDisplay) are in BuildMeshes.

        /// <summary>
        /// Drawn after the opaque world, depth-tested like part of the dashboard (the wheel can cover it);
        /// the VR hands render after it (VrHands.HandQueue).
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
            if (_mat.HasProperty("_ZTest")) _mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            _mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);

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

        // ---- Placement on the dashboard surface ----

        private Transform _dash;
        private Vector3[] _dashVerts;
        private int[] _dashTris;
        private float _nextPlace;
        private float _placedHeight = float.NaN;
        private readonly Pose[] _local = new Pose[2]; // panel poses in the dashboard's local space
        private bool _placed;

        /// <summary>
        /// Lays the panels flat on the dashboard mesh like built-in screens: the gauges where an instrument
        /// cluster sits (seen through the top of the steering wheel), the radio in the middle of the dash.
        /// Found by casting rays from the driver's eye against the dashboard's triangles.
        /// </summary>
        private void Place()
        {
            if (_dash == null) FindDashboard();
            if (!_placed || Time.unscaledTime >= _nextPlace || _placedHeight != VrConfig.DashboardHeight.Value)
            {
                _nextPlace = Time.unscaledTime + 2f;
                _placedHeight = VrConfig.DashboardHeight.Value;
                ComputePlacement();
            }

            var basis = _dash != null ? _dash : _wheel.transform;
            for (int i = 0; i < 2; i++)
            {
                var t = (i == 0 ? _gauges : _radio).transform;
                t.SetPositionAndRotation(basis.TransformPoint(_local[i].position), basis.rotation * _local[i].rotation);
            }
        }

        private void FindDashboard()
        {
            _dash = null;
            _dashVerts = null;
            var root = _wheel.car != null ? _wheel.car.transform : _wheel.transform.root;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.IndexOf("dashboard", System.StringComparison.OrdinalIgnoreCase) < 0 || mf.sharedMesh == null) continue;
                _dash = mf.transform;
                var mesh = mf.sharedMesh;
                if (mesh.isReadable)
                {
                    _dashVerts = mesh.vertices;
                    _dashTris = mesh.triangles;
                }
                else ReadFromGpu(mesh, out _dashVerts, out _dashTris);
                Plugin.Log.LogInfo($"VR dashboard: surface '{mf.name}' mesh '{mesh.name}' readable={mesh.isReadable} tris={(_dashTris != null ? _dashTris.Length / 3 : 0)}");
                break;
            }
            _placed = false;
        }

        /// <summary>
        /// The game's meshes aren't CPU-readable; copy positions and indices back from the GPU buffers instead.
        /// </summary>
        private static void ReadFromGpu(Mesh mesh, out Vector3[] verts, out int[] tris)
        {
            verts = null;
            tris = null;
            try
            {
                if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position) ||
                    mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position) != UnityEngine.Rendering.VertexAttributeFormat.Float32)
                    return;
                int stream = mesh.GetVertexAttributeStream(UnityEngine.Rendering.VertexAttribute.Position);
                int offset = mesh.GetVertexAttributeOffset(UnityEngine.Rendering.VertexAttribute.Position);
                int stride = mesh.GetVertexBufferStride(stream);
                int n = mesh.vertexCount;

                using (var vb = mesh.GetVertexBuffer(stream))
                {
                    var raw = new byte[vb.count * vb.stride];
                    vb.GetData(raw);
                    verts = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        int o = i * stride + offset;
                        verts[i] = new Vector3(System.BitConverter.ToSingle(raw, o), System.BitConverter.ToSingle(raw, o + 4), System.BitConverter.ToSingle(raw, o + 8));
                    }
                }

                var list = new List<int>();
                using (var ib = mesh.GetIndexBuffer())
                {
                    bool is16 = mesh.indexFormat == IndexFormat.UInt16;
                    var raw = new byte[ib.count * ib.stride];
                    ib.GetData(raw);
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var sm = mesh.GetSubMesh(s);
                        if (sm.topology != MeshTopology.Triangles) continue;
                        for (int k = 0; k < sm.indexCount; k++)
                        {
                            int idx = sm.indexStart + k;
                            int v = is16 ? System.BitConverter.ToUInt16(raw, idx * 2) : System.BitConverter.ToInt32(raw, idx * 4);
                            list.Add(v + sm.baseVertex);
                        }
                    }
                }
                tris = list.ToArray();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("VR dashboard: couldn't read the dashboard mesh from the GPU: " + e.Message);
                verts = null;
                tris = null;
            }
        }

        private void ComputePlacement()
        {
            var wt = _wheel.steeringWheel;
            var car = _wheel.car != null ? _wheel.car.transform : wt.root;
            var eye = VrCameraManager.Anchor != null ? VrCameraManager.Anchor.position : wt.position - car.forward * 0.6f + car.up * 0.3f;
            var up = car.up;
            float h = VrConfig.DashboardHeight.Value;

            float radius = 0.2f;
            foreach (var r in wt.GetComponentsInChildren<Renderer>()) radius = Mathf.Max(radius, Mathf.Min(0.3f, r.bounds.extents.y));

            // Gauges: looking through the upper half of the wheel. Radio: the dash's center, at the same height.
            var gaugeAim = wt.position + up * (radius * 0.55f + h);
            float lateral = Vector3.Dot(wt.position - car.position, car.right);
            var radioAim = wt.position - car.right * lateral + up * (radius * 0.3f + h);

            var basis = _dash != null ? _dash : _wheel.transform;
            int hits = 0;
            foreach (var (i, aim) in new[] { (0, gaugeAim), (1, radioAim) })
            {
                Vector3 pos, normal;
                if (RaycastDash(new Ray(eye, aim - eye), out var p, out var n))
                {
                    hits++;
                    pos = p + n * 0.004f; // just above the surface
                    normal = n;
                }
                else
                {
                    pos = aim; // fallback: hang where we aimed, facing the driver
                    normal = (eye - aim).normalized;
                }
                var panelUp = Vector3.ProjectOnPlane(up, normal);
                if (panelUp.sqrMagnitude < 1e-4f) panelUp = Vector3.ProjectOnPlane(car.forward, normal);
                var rot = Quaternion.LookRotation(-normal, panelUp.normalized); // +z into the dashboard, mesh faces the driver
                _local[i] = new Pose(basis.InverseTransformPoint(pos), Quaternion.Inverse(basis.rotation) * rot);
            }
            if (!_placed) Plugin.Log.LogInfo($"VR dashboard: placed panels, {hits}/2 on the dashboard surface");
            _placed = true;
        }

        /// <summary>Nearest hit of the ray on the dashboard's triangles (world space), normal facing the ray's origin.</summary>
        private bool RaycastDash(Ray worldRay, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (_dash == null || _dashVerts == null) return false;
            var o = _dash.InverseTransformPoint(worldRay.origin);
            var d = _dash.InverseTransformDirection(worldRay.direction); // local units; t compares fine along one ray
            float best = float.MaxValue;
            Vector3 bestN = default;
            for (int i = 0; i + 2 < _dashTris.Length; i += 3)
            {
                var a = _dashVerts[_dashTris[i]];
                var b = _dashVerts[_dashTris[i + 1]];
                var c = _dashVerts[_dashTris[i + 2]];
                // Möller–Trumbore, double-sided
                var e1 = b - a; var e2 = c - a;
                var pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                var tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv;
                if (v < 0f || u + v > 1f) continue;
                float t = Vector3.Dot(e2, qv) * inv;
                if (t <= 1e-4f || t >= best) continue;
                best = t;
                bestN = Vector3.Cross(e1, e2);
            }
            if (best == float.MaxValue) return false;
            point = _dash.TransformPoint(o + d * best);
            normal = _dash.TransformDirection(bestN).normalized;
            if (Vector3.Dot(normal, worldRay.direction) > 0f) normal = -normal;
            return true;
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
            bgMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
            var br = bg.GetComponent<Renderer>();
            br.sharedMaterial = bgMat;
            br.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        /// <summary>A piece of the HUD texture (pixels, y down) placed at a spot on a panel (panel pixels, y down).</summary>
        private struct Part
        {
            public Rect Src;
            public Vector2 Dst;
            public Part(Rect src, float x, float y) { Src = src; Dst = new Vector2(x, y); }
        }

        private void BuildMeshes(int w, int h)
        {
            _meshSize = new Vector2Int(w, h);

            // Instrument cluster: short and wide to fit the recess behind the wheel.
            //   [energy|fuel]  [distance to drop-off]
            //                  [money / time       ]
            var bars = Rect.MinMaxRect(68, h / 2 + 4, 100, h / 2 + 52);    // energy bars + fuel gauge (y ~132..178)
            var distance = Rect.MinMaxRect(68, 98, 140, 118);              // DrawDeliveryIndicator (70,100) + "123m"
            var money = Rect.MinMaxRect(68, h - 68, 140, h - 40);          // money (h-66) and time (h-54)
            var radio = Rect.MinMaxRect(w - 212, h - 80, w - 68, h - 36); // scan, fm frequency, song

            SetPanel(_gauges, ref _gaugeMesh, w, h, 0.0011f,
                new Part(bars, 0, 0), new Part(distance, bars.width + 4, 0), new Part(money, bars.width + 4, bars.height - money.height));
            SetPanel(_radio, ref _radioMesh, w, h, MetersPerPixel, new Part(radio, 0, 0));

            _holedMesh = HoledQuad(new[] { Uv(bars, w, h), Uv(distance, w, h), Uv(money, w, h), Uv(radio, w, h) });
        }

        private void SetPanel(Renderer r, ref Mesh mesh, int w, int h, float metersPerPixel, params Part[] parts)
        {
            var size = Vector2.zero;
            foreach (var p in parts) size = Vector2.Max(size, p.Dst + p.Src.size);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var p in parts)
            {
                var uv = Uv(p.Src, w, h);
                // Panel pixels (y down) to meters, centered; facing -z, toward the viewer.
                float x0 = (p.Dst.x - size.x / 2f) * metersPerPixel, x1 = x0 + p.Src.width * metersPerPixel;
                float y1 = (size.y / 2f - p.Dst.y) * metersPerPixel, y0 = y1 - p.Src.height * metersPerPixel;
                int b = verts.Count;
                verts.AddRange(new[] { new Vector3(x0, y0), new Vector3(x0, y1), new Vector3(x1, y1), new Vector3(x1, y0) });
                uvs.AddRange(new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin) });
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            if (mesh == null) mesh = new Mesh { name = r.name };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            r.GetComponent<MeshFilter>().sharedMesh = mesh;

            float sx = size.x * metersPerPixel, sy = size.y * metersPerPixel;
            var bg = r.transform.Find("Backing");
            bg.localPosition = new Vector3(0f, 0f, 0.0015f); // between the panel and the dashboard
            bg.localScale = new Vector3(sx + 0.01f, sy + 0.01f, 1f);
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
