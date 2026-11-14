using EasyDeliveryVR.Xr;
using UnityEngine;

namespace EasyDeliveryVR.Rendering
{
    /// <summary>
    /// Comfort vignette: darkens the edges of the view while the view rotates artificially
    /// (truck turning, smooth/snap turning on foot). Strength 0 = off (config Comfort/Vignette).
    /// </summary>
    internal class VrVignette : MonoBehaviour
    {
        private Transform _quad;
        private Material _mat;
        private float _intensity;
        private float _prevYaw;
        private bool _hasPrev;

        private void Start() => Create();

        private void Create()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "VR Vignette";
            Destroy(go.GetComponent<Collider>());
            DontDestroyOnLoad(go);
            _quad = go.transform;
            _mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = MakeTexture(), renderQueue = 4900 };
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
        }

        private static Texture2D MakeTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, r));
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            tex.Apply();
            return tex;
        }

        private void LateUpdate()
        {
            if (_quad == null) Create(); // destroyed along with a previous scene's camera
            float strength = VrConfig.Vignette.Value;
            var cam = FindHmd();
            if (!XrRuntime.Running || strength <= 0f || cam == null || PauseSystem.paused)
            {
                if (_quad != null && _quad.gameObject.activeSelf) _quad.gameObject.SetActive(false);
                _hasPrev = false;
                _intensity = 0f;
                return;
            }

            // Artificial rotation = rotation of the anchor the head is attached to (truck heading / body yaw).
            float target = 0f;
            var mode = VrCameraManager.Mode;
            if (mode == VrCameraManager.ViewMode.Cab || mode == VrCameraManager.ViewMode.OnFoot)
            {
                float yaw = VrCameraManager.AnchorRotation.eulerAngles.y;
                if (_hasPrev && Time.deltaTime > 0f)
                {
                    float rate = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) / Time.deltaTime; // deg/s
                    target = Mathf.InverseLerp(20f, 90f, rate);
                    if (Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yaw)) > 10f) target = 1f; // snap turn
                }
                _prevYaw = yaw;
                _hasPrev = true;
            }
            else _hasPrev = false;

            // Fade in fast, out slowly.
            float speed = target > _intensity ? 8f : 2f;
            _intensity = Mathf.MoveTowards(_intensity, target, Time.deltaTime * speed);

            bool show = _intensity > 0.01f;
            if (_quad.gameObject.activeSelf != show) _quad.gameObject.SetActive(show);
            if (!show) return;

            if (_quad.parent != cam.transform)
            {
                _quad.SetParent(cam.transform, false);
                _quad.localPosition = new Vector3(0f, 0f, 0.15f);
                _quad.localRotation = Quaternion.identity;
            }
            // Smaller quad = smaller clear area in the middle.
            float size = Mathf.Lerp(0.9f, 0.45f, Mathf.Clamp01(strength));
            _quad.localScale = new Vector3(size, size, 1f);
            _mat.color = new Color(1f, 1f, 1f, _intensity);
        }

        private Camera _cached;

        private Camera FindHmd()
        {
            if (_cached != null && _cached.isActiveAndEnabled && _cached.stereoTargetEye == StereoTargetEyeMask.Both) return _cached;
            _cached = null;
            foreach (var c in Camera.allCameras)
                if (c.stereoTargetEye == StereoTargetEyeMask.Both && c.targetTexture == null) { _cached = c; break; }
            if (_cached == null && _quad != null && _quad.parent != null)
            {
                _quad.SetParent(null, false);
                DontDestroyOnLoad(_quad.gameObject);
            }
            return _cached;
        }
    }
}
