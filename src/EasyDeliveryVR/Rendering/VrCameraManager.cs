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
        public enum ViewMode { None, Default, Cab, OnFoot }

        public static ViewMode Mode { get; private set; }

        /// <summary>Transform the tracked head is relative to (after recentering): truck pivot in the cab, else the camera's parent.</summary>
        public static Transform Anchor { get; private set; }

        private Camera _cam;
        private sCameraController _truckCam;
        private bool _cabForcedThisScene;

        private static Vector3 _originPos;
        private static Quaternion _originRot = Quaternion.identity;
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
            _footCam = null;
            _footForcedThisScene = false;
            _seat = null;
            _wheel = null;
            _guy = null;
            _footAnchor = null;
            _guyFirstPersonSet = false;
            _hiddenHead = null;
            _needsRecenter = true;
        }

        public void Recenter() => _needsRecenter = true;

        /// <summary>Converts a pose in XR tracking space (e.g. a controller) to world space, matching the camera.</summary>
        public static bool TrackingToWorld(Vector3 pos, Quaternion rot, out Vector3 worldPos, out Quaternion worldRot)
        {
            worldPos = default;
            worldRot = Quaternion.identity;
            var a = Anchor;
            if (a == null) return false;
            var inv = Quaternion.Inverse(_originRot);
            var lp = inv * (pos - _originPos);
            var lr = inv * rot;
            if (Mode == ViewMode.Cab || Mode == ViewMode.OnFoot)
            {
                worldPos = a.position + AnchorRotation * lp;
                worldRot = AnchorRotation * lr;
            }
            else
            {
                worldPos = a.TransformPoint(lp);
                worldRot = a.rotation * lr;
            }
            return true;
        }

        private void Update()
        {
            VrControllers.Update();
            var kb = Keyboard.current;
            if ((kb != null && kb.f8Key.wasPressedThisFrame) || VrControllers.RightClickHeld) Recenter();

            if (_cam == null || !_cam.isActiveAndEnabled) FindAndSetupCamera();
            else if (_cam.targetTexture != null) _cam.targetTexture = null; // game may re-assign it

            if (_truckCam == null) FindTruckCamera();
            UpdateOnFoot();
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
            Plugin.Log.LogInfo($"VR: game near clip {_cam.nearClipPlane}, using 0.03");
            _cam.nearClipPlane = Mathf.Min(_cam.nearClipPlane, 0.03f);
            HideNearClipBlockers();
            Plugin.Log.LogInfo($"VR: using camera '{_cam.name}' in scene '{_cam.gameObject.scene.name}'");
            _needsRecenter = true;
        }

        /// <summary>
        /// The game parks black inside-out boxes ("CameraBlocked") around its camera that are normally
        /// hidden by the large near-clip plane. With the VR near clip of 3 cm they'd fill the view.
        /// </summary>
        private void HideNearClipBlockers()
        {
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.gameObject.name != "CameraBlocked" || !r.enabled) continue;
                r.enabled = false;
                Plugin.Log.LogInfo("VR: hid near-clip blocker " + r.transform.root.name + "/.../" + r.name);
            }
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
            if (tc == null || sys == null || tc.car == null || tc.car.GuyActive) return false;
            // Truck camera in first person, or the on-foot first-person controller looking from the cab pivot (its mode 2).
            return (tc.firstPerson && !tc.fixedPerspective && sys.currentTarget == tc.camPivot.transform)
                   || sys.currentTarget == tc.firstPersonPivot;
        }

        private bool GuyActive => _truckCam != null && _truckCam.car != null && _truckCam.car.GuyActive;

        /// <summary>First person on foot: either the game's own add-on controller, or the mod's own mode.</summary>
        private bool OnFoot(out Transform anchor)
        {
            anchor = null;
            if (!GuyActive) return false;
            var sys = sCameraSystem.instance;
            if (_footCam != null && _footCam.isActiveAndEnabled)
            {
                if (_footCam.working && sys != null && sys.currentTarget == _footCam.transform) anchor = _footCam.transform;
            }
            else if (_vrFootFirstPerson && _footAnchor != null)
            {
                anchor = _footAnchor;
            }
            return anchor != null;
        }

        private void OnBeforeRender()
        {
            if (_cam == null || !XrRuntime.Running) return;
            if (!VrInput.TryGetHeadPose(out var pos, out var rot)) return;

            Transform footAnchor = null;
            var mode = InCab() ? ViewMode.Cab : OnFoot(out footAnchor) ? ViewMode.OnFoot : ViewMode.Default;
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

            switch (mode)
            {
                case ViewMode.Cab:
                    Anchor = UpdateSeat();
                    AnchorRotation = Anchor.rotation; // rigid with the truck, including its tilt
                    break;
                case ViewMode.OnFoot:
                    Anchor = footAnchor;
                    AnchorRotation = Quaternion.Euler(0f, Anchor.eulerAngles.y, 0f); // body yaw only
                    break;
                default:
                    Anchor = _cam.transform.parent;
                    break;
            }

            if (mode == ViewMode.Default)
            {
                _cam.transform.localPosition = headPos;
                _cam.transform.localRotation = headRot;
            }
            else
            {
                _cam.transform.SetPositionAndRotation(Anchor.position + AnchorRotation * headPos, AnchorRotation * headRot);
            }
        }

        // ---- Cab seat ----

        private Transform _seat;
        private InteriorInteraction _wheel;
        private bool _seatLogged;

        /// <summary>
        /// The head's recenter point in the cab: the game's first-person pivot, moved sideways so it lines up
        /// with the steering wheel's center, plus the configured seat offset.
        /// </summary>
        private Transform UpdateSeat()
        {
            var pivot = _truckCam.firstPersonPivot;
            if (_seat == null || _seat.parent != pivot)
            {
                _seat = new GameObject("VR Seat").transform;
                _seat.SetParent(pivot, false);
                _seat.localRotation = Quaternion.identity;
                _seatLogged = false;
            }
            if (_wheel == null) _wheel = FindFirstObjectByType<InteriorInteraction>();

            var offset = Vector3.zero;
            if (VrConfig.CenterOnWheel.Value && _wheel != null && _wheel.steeringWheel != null)
            {
                // Sideways only; pivot space, unaffected by the pivot's scale.
                var toWheel = Quaternion.Inverse(pivot.rotation) * (_wheel.steeringWheel.position - pivot.position);
                offset.x = toWheel.x;
                if (!_seatLogged)
                {
                    _seatLogged = true;
                    Plugin.Log.LogInfo($"VR: cab seat centered on wheel, sideways shift {toWheel.x:F3} m (wheel at {toWheel} from pivot)");
                }
            }
            offset += new Vector3(VrConfig.SeatOffsetX.Value, VrConfig.SeatOffsetY.Value, VrConfig.SeatOffsetZ.Value);

            // Set in world units so the pivot's scale doesn't stretch the offset.
            _seat.position = pivot.position + pivot.rotation * offset;
            _seat.rotation = pivot.rotation;
            return _seat;
        }

        // ---- On foot ----

        private sFirstPersonController _footCam;
        private bool _footForcedThisScene;
        private bool _turnLatched;

        /// <summary>World rotation the tracked head is relative to (cab: truck pivot; on foot: body yaw).</summary>
        public static Quaternion AnchorRotation { get; private set; } = Quaternion.identity;

        // The mod's own first-person on foot, used when the game's first-person add-on isn't active.
        private sCharacterController _guy;
        private Transform _footAnchor;
        private bool _vrFootFirstPerson;
        private bool _guyFirstPersonSet;
        private Renderer[] _hiddenHead;

        /// <summary>True while the right-stick tap is used by the mod (on foot), so it must not reach the game's camera toggle.</summary>
        public static bool ConsumeCameraButton { get; private set; }

        private void UpdateOnFoot()
        {
            if (_footCam == null) _footCam = FindFirstObjectByType<sFirstPersonController>();
            if (_guy == null)
            {
                _guy = FindFirstObjectByType<sCharacterController>();
                if (_guy != null)
                {
                    _vrFootFirstPerson = VrConfig.FirstPersonOnFoot.Value;
                    _footAnchor = CreateFootAnchor(_guy);
                }
            }

            bool gameAddOn = _footCam != null && _footCam.isActiveAndEnabled;
            ConsumeCameraButton = !gameAddOn && GuyActive;

            if (gameAddOn)
            {
                // Its Start coroutine restores the saved camera mode two frames in; switch to first person after that.
                if (VrConfig.FirstPersonOnFoot.Value && !_footForcedThisScene && Time.timeSinceLevelLoad > 1f)
                {
                    _footForcedThisScene = true;
                    _footCam.cameraMode = 2; // 2 = first person on foot, cab pivot in the truck
                    InterSceneData.SetInt("cameraMode", 2);
                    Plugin.Log.LogInfo("VR: on-foot first person enabled (game add-on)");
                }
            }
            else if (_guy != null)
            {
                if (GuyActive && VrControllers.RightClickTapped)
                {
                    _vrFootFirstPerson = !_vrFootFirstPerson;
                    Plugin.Log.LogInfo("VR: on-foot view " + (_vrFootFirstPerson ? "first person" : "over the head"));
                }
                ApplyVrFootFirstPerson(GuyActive && _vrFootFirstPerson);
            }

            if (Mode != ViewMode.OnFoot) return;
            var body = _guy != null ? _guy.transform : _footCam != null ? _footCam.transform.parent : null;
            if (body == null) return;

            // Turning: snap (default) or smooth, around the player's head so the view doesn't shift sideways.
            float x = VrControllers.Right.Stick.x;
            float angle = 0f;
            if (VrConfig.SmoothTurn.Value)
            {
                if (Mathf.Abs(x) > 0.2f) angle = x * VrConfig.SmoothTurnSpeed.Value * Time.deltaTime;
            }
            else if (!_turnLatched && Mathf.Abs(x) > 0.6f)
            {
                _turnLatched = true;
                angle = Mathf.Sign(x) * VrConfig.SnapTurnAngle.Value;
            }
            if (Mathf.Abs(x) < 0.3f) _turnLatched = false;
            if (angle != 0f) body.RotateAround(_cam.transform.position, Vector3.up, angle);
        }

        /// <summary>Eye point on the character: top of its capsule, facing the way it walks (the model faces backwards).</summary>
        private static Transform CreateFootAnchor(sCharacterController guy)
        {
            var anchor = new GameObject("VR Foot Eye").transform;
            anchor.SetParent(guy.transform, false);
            float eye = 1.6f;
            if (guy.TryGetComponent<CharacterController>(out var chc))
                eye = chc.center.y + chc.height * 0.5f - 0.12f;
            anchor.localPosition = new Vector3(0f, eye, 0f);
            anchor.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Plugin.Log.LogInfo($"VR: on-foot eye point at {eye:F2} m on '{guy.name}'");
            return anchor;
        }

        private void ApplyVrFootFirstPerson(bool on)
        {
            if (on == _guyFirstPersonSet) return;
            _guyFirstPersonSet = on;
            // In first person the character turns only by look input (zeroed in VR, the mod snap-turns instead),
            // rather than turning to face where it walks.
            _guy.firstPerson = on;

            if (_hiddenHead == null)
            {
                var app = FindFirstObjectByType<sAltAppearance>();
                _hiddenHead = app != null && app.guyHead != null ? app.guyHead.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            }
            foreach (var r in _hiddenHead) if (r != null) r.enabled = !on;
        }

        /// <summary>Flat forward direction of the player's head in world space (for head-relative walking).</summary>
        public static Vector3 HeadForwardFlat { get; private set; } = Vector3.forward;

        private void LateUpdate()
        {
            if (_cam == null) return;
            var f = _cam.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) HeadForwardFlat = f.normalized;
        }
    }
}
