using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.XR;

namespace EasyDeliveryVR.Ui
{
    /// <summary>
    /// Laser pointer from the right controller onto the in-game computer/menu screen (ScreenSystem).
    /// The hit point is converted to the screen's pixel coordinates and fed to ScreenSystem.GetMouse
    /// (see UiPatches); the right trigger clicks (see InputPatches).
    /// </summary>
    internal class VrLaser : MonoBehaviour
    {
        public static bool HasHit { get; private set; }
        public static Vector2 MousePos { get; private set; }

        /// <summary>Set by VrScreenMirror: the mirrored quad that shows ScreenSystem's render texture.</summary>
        public static Transform ScreenQuad;
        public static ScreenSystem Screen;

        private LineRenderer _line;
        private Transform _dot;
        private static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");
        private bool _loggedPoseSource;

        private void Start()
        {
            var mat = new Material(Shader.Find("Sprites/Default")) { renderQueue = 4600 };
            var go = new GameObject("VR Laser") { layer = 0 };
            DontDestroyOnLoad(go);
            _line = go.AddComponent<LineRenderer>();
            _line.sharedMaterial = mat;
            _line.widthMultiplier = 0.004f;
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.startColor = new Color(1f, 1f, 1f, 0.8f);
            _line.endColor = new Color(1f, 1f, 1f, 0.2f);

            _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            Destroy(_dot.GetComponent<Collider>());
            _dot.gameObject.layer = 0;
            _dot.localScale = Vector3.one * 0.012f;
            _dot.GetComponent<Renderer>().sharedMaterial = mat;
            DontDestroyOnLoad(_dot.gameObject);
            Hide();
        }

        private void Hide()
        {
            HasHit = false;
            if (_line) _line.enabled = false;
            if (_dot) _dot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            // Only while a menu/computer screen is open.
            if (!XrRuntime.Running || !PauseSystem.paused || ScreenQuad == null || Screen == null || Screen.R == null
                || !ScreenQuad.gameObject.activeInHierarchy || !TryGetAimPose(out var origin, out var dir))
            {
                Hide();
                return;
            }

            var plane = new Plane(ScreenQuad.forward, ScreenQuad.position);
            var ray = new Ray(origin, dir);
            Vector3 end = origin + dir * 3f;
            HasHit = false;
            if (plane.Raycast(ray, out float t) && t > 0f)
            {
                var hit = ray.GetPoint(t);
                var local = ScreenQuad.InverseTransformPoint(hit); // Unity quad: -0.5..0.5
                float u = local.x + 0.5f, v = local.y + 0.5f;
                if (u >= -0.05f && u <= 1.05f && v >= -0.05f && v <= 1.05f)
                {
                    HasHit = true;
                    end = hit;
                    MousePos = new Vector2(Mathf.Clamp01(u) * Screen.R.width, (1f - Mathf.Clamp01(v)) * Screen.R.height);
                }
            }

            _line.enabled = true;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);
            _dot.gameObject.SetActive(HasHit);
            _dot.position = end;
        }

        private bool TryGetAimPose(out Vector3 origin, out Vector3 dir)
        {
            origin = dir = default;
            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!d.isValid) return false;

            Vector3 p = default; Quaternion r = Quaternion.identity;
            bool aim = d.TryGetFeatureValue(PointerPosition, out p) && d.TryGetFeatureValue(PointerRotation, out r);
            if (!aim)
            {
                if (!d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out p) ||
                    !d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out r)) return false;
                r *= Quaternion.Euler(VrConfig.LaserPitch.Value, 0f, 0f); // grip pose points along the handle
            }
            if (!_loggedPoseSource)
            {
                _loggedPoseSource = true;
                Plugin.Log.LogInfo("VR laser: using " + (aim ? "aim (pointer) pose" : "grip pose + pitch offset"));
            }

            if (!VrCameraManager.TrackingToWorld(p, r, out origin, out var worldRot)) return false;
            dir = worldRot * Vector3.forward;
            return true;
        }
    }
}
