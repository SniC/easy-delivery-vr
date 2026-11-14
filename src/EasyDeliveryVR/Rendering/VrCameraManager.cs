using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace EasyDeliveryVR.Rendering
{
    /// <summary>
    /// Sends the game camera to the headset and applies head tracking.
    ///
    /// Two modes:
    ///  - Cab: the truck's first-person camera. The camera is rigidly attached to the truck's
    ///    firstPersonPivot (ignoring the game's smoothing/look-ahead) plus the tracked head pose.
    ///  - Default: head pose applied on top of whatever pose the game gave the camera.
    ///
    /// The game resets its camera every LateUpdate (sCameraSystem), so the pose is applied in
    /// Application.onBeforeRender, which runs after all LateUpdates.
    /// </summary>
    internal class VrCameraManager : MonoBehaviour
    {
        public enum ViewMode { None, Default, Cab }

        public static ViewMode Mode { get; private set; }

        private Camera _cam;
        private sCameraController _truckCam;
        private bool _cabForcedThisScene;

        private Vector3 _originPos;
        private Quaternion _originRot = Quaternion.identity;
        private bool _needsRecenter = true;

        private void OnEnable()
        {
            Application.onBeforeRender += OnBeforeRender;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _cam = null;
            _truckCam = null;
            _cabForcedThisScene = false;
            _needsRecenter = true;
        }

        public void Recenter() => _needsRecenter = true;

        private void Update()
        {
            VrControllers.Update();
            var kb = Keyboard.current;
            if ((kb != null && kb.f8Key.wasPressedThisFrame) || VrControllers.RightClickHeld) Recenter();

            if (_cam == null || !_cam.isActiveAndEnabled) FindAndSetupCamera();
            else if (_cam.targetTexture != null) _cam.targetTexture = null; // game may re-assign it

            if (_truckCam == null) FindTruckCamera();
        }

        private void FindAndSetupCamera()
        {
            // The world camera renders into the low-res "Screen" RT, which the PauseMenuURP camera
            // then shows (with the HUD quads) on the monitor. We want the world camera.
            Camera cam = sCameraSystem.cam;
            if (cam == null || !cam.isActiveAndEnabled)
            {
                cam = null;
                foreach (var c in Camera.allCameras)
                    if (c.targetTexture != null && c.targetTexture.name == "Screen") { cam = c; break; }
            }
            if (cam == null) cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled) return;
            _cam = cam;

            foreach (var c in Camera.allCameras)
            {
                if (c == _cam) continue;
                // Other cameras (menu/backdrop/minimap) only go to the desktop window for now.
                c.stereoTargetEye = StereoTargetEyeMask.None;
            }

            if (_cam.targetTexture != null)
            {
                Plugin.Log.LogInfo($"VR: game camera rendered into {_cam.targetTexture.name} ({_cam.targetTexture.width}x{_cam.targetTexture.height}); redirecting to HMD");
                _cam.targetTexture = null;
            }
            _cam.stereoTargetEye = StereoTargetEyeMask.Both;
            _cam.nearClipPlane = Mathf.Min(_cam.nearClipPlane, 0.03f);
            Plugin.Log.LogInfo($"VR: using camera '{_cam.name}' in scene '{_cam.gameObject.scene.name}'");
            _needsRecenter = true;
        }

        private void FindTruckCamera()
        {
            var tc = FindFirstObjectByType<sCameraController>();
            if (tc == null || tc.car == null || tc.firstPersonPivot == null) return;
            _truckCam = tc;

            if (VrConfig.SmoothTruckPhysics.Value && tc.car.rb != null && tc.car.rb.interpolation == RigidbodyInterpolation.None)
            {
                tc.car.rb.interpolation = RigidbodyInterpolation.Interpolate;
                Plugin.Log.LogInfo("VR: enabled truck rigidbody interpolation");
            }

            if (VrConfig.ForceCabView.Value && !_cabForcedThisScene)
            {
                _cabForcedThisScene = true;
                tc.firstPerson = true;
                tc.firstPersonMode = true;
                foreach (var hack in FindObjectsByType<FirstPersonCameraHack>(FindObjectsSortMode.None))
                    if (hack.realCamera == tc) hack.setToFirstPerson = true;
                Plugin.Log.LogInfo("VR: switched truck camera to cab view");
            }
        }

        private bool InCab()
        {
            var tc = _truckCam;
            var sys = sCameraSystem.instance;
            return tc != null && sys != null
                   && tc.firstPerson && !tc.fixedPerspective
                   && tc.car != null && !tc.car.GuyActive
                   && sys.currentTarget == tc.camPivot.transform;
        }

        private void OnBeforeRender()
        {
            if (_cam == null || !XrRuntime.Running) return;
            if (!VrInput.TryGetHeadPose(out var pos, out var rot)) return;

            var mode = InCab() ? ViewMode.Cab : ViewMode.Default;
            if (mode != Mode)
            {
                Plugin.Log.LogInfo($"VR: view mode {Mode} -> {mode}");
                Mode = mode;
                _needsRecenter = true;
            }

            if (_needsRecenter)
            {
                // Only yaw is recentered; pitch/roll stay true to the real horizon.
                var fwd = rot * Vector3.forward;
                fwd.y = 0;
                _originRot = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
                _originPos = pos;
                _needsRecenter = false;
            }

            var inv = Quaternion.Inverse(_originRot);
            var headPos = inv * (pos - _originPos);
            var headRot = inv * rot;

            if (mode == ViewMode.Cab)
            {
                var pivot = _truckCam.firstPersonPivot;
                _cam.transform.SetPositionAndRotation(pivot.position + pivot.rotation * headPos, pivot.rotation * headRot);
            }
            else
            {
                _cam.transform.localPosition = headPos;
                _cam.transform.localRotation = headRot;
            }
        }
    }
}
