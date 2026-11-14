using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace EasyDeliveryVR.Rendering
{
    /// <summary>
    /// Sends the game camera to the headset and applies head tracking on top of the game's camera pose.
    /// The game resets its camera to the pivot every LateUpdate (sCameraSystem), so we apply the
    /// head pose in Application.onBeforeRender, which runs after all LateUpdates.
    /// </summary>
    internal class VrCameraManager : MonoBehaviour
    {
        private Camera _cam;
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
            _needsRecenter = true;
        }

        public void Recenter() => _needsRecenter = true;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Recenter();
            if (VrInput.Button(false, CommonUsages.primary2DAxisClick) && VrInput.Button(true, CommonUsages.primary2DAxisClick))
                Recenter();

            if (_cam == null || !_cam.isActiveAndEnabled) FindAndSetupCamera();
            else if (_cam.targetTexture != null) _cam.targetTexture = null; // game may re-assign it
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

        private void OnBeforeRender()
        {
            if (_cam == null || !XrRuntime.Running) return;
            if (!VrInput.TryGetHeadPose(out var pos, out var rot)) return;

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
            _cam.transform.localPosition = inv * (pos - _originPos);
            _cam.transform.localRotation = inv * rot;
        }
    }
}
