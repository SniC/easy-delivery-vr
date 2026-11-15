using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace EasyDeliveryVR.Motion
{
    /// <summary>
    /// Visible hands at the controllers (cab and first person on foot), grab-the-wheel steering in the cab,
    /// and controller haptics for bumps and crashes.
    ///
    /// Wheel: grip near the rim grabs it; turning your hands around the wheel axis rotates it. The wheel angle
    /// drives the truck's steering directly (see WheelPatches) as angle / WheelLockDegrees.
    /// </summary>
    internal class VrHands : MonoBehaviour
    {
        private class Hand
        {
            public bool Left;
            public Transform Visual;
            public Vector3 Pos;
            public Quaternion Rot;
            public bool Tracked, Grabbing, NearWheel;
            public float PrevGrip;
            public Vector3 PrevPlanar;
        }

        public static bool Steering { get; private set; }
        public static float SteerValue { get; private set; }
        public static bool LeftBusy => _instance != null && (_instance._l.Grabbing || _instance._l.NearWheel);
        public static bool RightBusy => _instance != null && (_instance._r.Grabbing || _instance._r.NearWheel);

        private static VrHands _instance;

        /// <summary>After the dashboard display (drawn without depth test), so the hands stay in front of it.</summary>
        private const int HandQueue = Ui.VrDashboard.Queue + 100;
        private readonly Hand _l = new Hand { Left = true };
        private readonly Hand _r = new Hand { Left = false };

        private InteriorInteraction _wheel;
        private float _radius;
        private float _wheelAngle;
        private Renderer[] _gameHand;
        private sCarController _car;
        private Vector3 _prevVel;
        private float _nextSearch;

        private Hand[] _both; // both hands, reused every frame

        private void Awake()
        {
            _instance = this;
            _both = new[] { _l, _r };
        }
        private void OnEnable() => Application.onBeforeRender += PlaceVisuals;
        private void OnDisable() => Application.onBeforeRender -= PlaceVisuals;

        private void Start()
        {
            _l.Visual = MakeHandVisual("VR Left Hand");
            _r.Visual = MakeHandVisual("VR Right Hand");
        }

        private static Transform MakeHandVisual(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            DontDestroyOnLoad(go);
            go.transform.localScale = new Vector3(0.075f, 0.035f, 0.11f);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = new Color(0.25f, 0.22f, 0.2f);
            mat.renderQueue = HandQueue;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private bool ShowHands =>
            XrRuntime.Running && VrConfig.ShowHands.Value && // also while paused: the menu screen sits on the left hand

            (VrCameraManager.Mode == VrCameraManager.ViewMode.Cab || VrCameraManager.Mode == VrCameraManager.ViewMode.OnFoot);

        private void Update()
        {
            if (!XrRuntime.Running) return;
            VrControllers.Update();

            if ((_wheel == null || _car == null) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                FindWheel();
            }

            ReadPose(_l);
            ReadPose(_r);

            bool inCab = VrCameraManager.Mode == VrCameraManager.ViewMode.Cab && _wheel != null && !PauseSystem.paused;
            UpdateGrab(_l, VrControllers.Left.Grip, inCab);
            UpdateGrab(_r, VrControllers.Right.Grip, inCab);
            UpdateSteering(inCab);
            UpdateHaptics(inCab);
        }

        private void FindWheel()
        {
            _wheel = FindFirstObjectByType<InteriorInteraction>();
            _car = _wheel != null && _wheel.car != null ? _wheel.car : FindFirstObjectByType<sCarController>();
            if (_wheel == null || _wheel.steeringWheel == null) return;

            // Wheel radius from its meshes, measured in the wheel's plane.
            _radius = 0f;
            var wt = _wheel.steeringWheel;
            foreach (var mf in wt.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                foreach (var corner in new[] { b.min, b.max, new Vector3(b.min.x, b.center.y, b.max.z), new Vector3(b.max.x, b.center.y, b.min.z) })
                {
                    var world = mf.transform.TransformPoint(corner);
                    var d = world - wt.position;
                    var planar = d - wt.up * Vector3.Dot(d, wt.up);
                    _radius = Mathf.Max(_radius, planar.magnitude * 0.7071f); // corners of the bounding box -> circle
                }
            }
            if (_radius < 0.1f || _radius > 0.4f) _radius = 0.2f;
            _gameHand = FindCabHands();
            Plugin.Log.LogInfo($"VR hands: steering wheel '{wt.name}' radius {_radius:F2} m");
        }

        /// <summary>
        /// The game's hands in the cab: the animated one (InteriorInteraction.hand) first, then every other
        /// hand-like renderer in the truck (e.g. the second hand resting on the wheel), matched by name or by
        /// using the same mesh as the animated hand. First entry is the source for the VR hand look.
        /// </summary>
        private Renderer[] FindCabHands()
        {
            var list = new System.Collections.Generic.List<Renderer>();
            if (_wheel.hand != null) list.AddRange(_wheel.hand.GetComponentsInChildren<Renderer>(true));
            Mesh handMesh = list.Count > 0 && list[0].TryGetComponent<MeshFilter>(out var hmf) ? hmf.sharedMesh : null;

            var root = _car != null ? _car.transform : _wheel.transform.root;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (list.Contains(r)) continue;
                bool named = r.name.IndexOf("hand", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool sameMesh = handMesh != null && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh == handMesh
                                && r.sharedMaterial == list[0].sharedMaterial;
                if (!named && !sameMesh) continue;
                if (r.bounds.size.magnitude > 0.6f) continue; // not a hand-sized object
                list.Add(r);
            }
            foreach (var r in list) Plugin.Log.LogInfo($"VR hands: game cab hand '{r.transform.parent?.name}/{r.name}'");
            return list.ToArray();
        }

        private void ReadPose(Hand h)
        {
            var d = InputDevices.GetDeviceAtXRNode(h.Left ? XRNode.LeftHand : XRNode.RightHand);
            h.Tracked = d.isValid
                        && d.TryGetFeatureValue(CommonUsages.devicePosition, out var p)
                        && d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r)
                        && VrCameraManager.TrackingToWorld(p, r, out h.Pos, out h.Rot);
        }

        // ---- Steering wheel ----

        private Vector3 Planar(Vector3 worldPos, out float height)
        {
            var wt = _wheel.steeringWheel;
            var d = worldPos - wt.position;
            height = Vector3.Dot(d, wt.up);
            return d - wt.up * height;
        }

        private void UpdateGrab(Hand h, float grip, bool inCab)
        {
            h.NearWheel = false;
            if (inCab && h.Tracked)
            {
                var planar = Planar(h.Pos, out float height);
                float rimDist = Mathf.Sqrt(height * height + Mathf.Pow(planar.magnitude - _radius, 2f));
                h.NearWheel = rimDist < VrConfig.WheelGrabDistance.Value;

                if (!h.Grabbing && h.NearWheel && grip > 0.6f && h.PrevGrip <= 0.6f)
                {
                    if (!_l.Grabbing && !_r.Grabbing) _wheelAngle = Mathf.Clamp(_wheel.steeringAngle, -Lock, Lock);
                    h.Grabbing = true;
                    h.PrevPlanar = planar;
                    Haptic(h.Left, 0.3f, 0.05f);
                }
            }
            if (h.Grabbing && (grip < 0.4f || !inCab || !h.Tracked)) h.Grabbing = false;
            h.PrevGrip = grip;
        }

        private static float Lock => Mathf.Max(30f, VrConfig.WheelLockDegrees.Value);

        private void UpdateSteering(bool inCab)
        {
            int n = 0;
            float delta = 0f;
            if (inCab)
            {
                foreach (var h in _both)
                {
                    if (!h.Grabbing) continue;
                    var planar = Planar(h.Pos, out _);
                    if (planar.sqrMagnitude > 1e-4f && h.PrevPlanar.sqrMagnitude > 1e-4f)
                    {
                        delta += Vector3.SignedAngle(h.PrevPlanar, planar, _wheel.steeringWheel.up);
                        n++;
                    }
                    h.PrevPlanar = planar;
                }
            }

            bool steering = inCab && (_l.Grabbing || _r.Grabbing);
            if (steering)
            {
                if (n > 0) _wheelAngle = Mathf.Clamp(_wheelAngle + delta / n, -Lock, Lock);
                _wheel.externalSteeringAngle = true;
                _wheel.steeringAngle = _wheelAngle;
                SteerValue = _wheelAngle / Lock;
            }
            else if (Steering && _wheel != null)
            {
                _wheel.externalSteeringAngle = false; // stick steering and the game's wheel animation take over again
            }
            Steering = steering;

            UpdateGameHands();
        }

        // ---- The character's own hands ----

        private sPlayerHands _playerHands;
        private Renderer[] _guyHands = new Renderer[0];
        private bool _skinned;
        private bool _gameHandsHidden;

        /// <summary>
        /// The game's hands are textured spheres (cab: Car/carInt/LeftHand, on foot: guy_model/Left/RightHand).
        /// Give the VR hands the same mesh and material, and hide the game's hands while the VR ones show.
        /// </summary>
        private void UpdateGameHands()
        {
            if (_playerHands == null && Time.frameCount % 60 == 0)
            {
                _playerHands = FindFirstObjectByType<sPlayerHands>();
                if (_playerHands != null)
                {
                    var list = new System.Collections.Generic.List<Renderer>();
                    foreach (var go in new[] { _playerHands.leftHand, _playerHands.rightHand })
                        if (go != null) list.AddRange(go.GetComponentsInChildren<Renderer>(true));
                    _guyHands = list.ToArray();
                }
            }

            if (!_skinned && _gameHand != null && _gameHand.Length > 0 && _gameHand[0] is MeshRenderer src
                && src.TryGetComponent<MeshFilter>(out var srcMf) && srcMf.sharedMesh != null)
            {
                _skinned = true;
                foreach (var h in _both)
                {
                    if (h.Visual == null) continue;
                    h.Visual.GetComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
                    // Copies: the VR hands outlive the scene, the game's materials may not.
                    h.Visual.GetComponent<Renderer>().sharedMaterials =
                        System.Array.ConvertAll(src.sharedMaterials, m => m != null ? new Material(m) { renderQueue = HandQueue } : null);
                    h.Visual.localScale = src.transform.lossyScale; // same size as the hand on the wheel
                }
                Plugin.Log.LogInfo($"VR hands: using the game's hand mesh '{srcMf.sharedMesh.name}' ({src.sharedMaterial?.name})");
            }

            // Hide the game's hands wherever the VR hands stand in for them.
            bool cab = ShowHands && VrCameraManager.Mode == VrCameraManager.ViewMode.Cab;
            // OnFoot is first person only; in the over-the-head view (Default) the character keeps its own hands.
            bool foot = ShowHands && VrCameraManager.Mode == VrCameraManager.ViewMode.OnFoot;
            if (_gameHand != null)
                foreach (var r in _gameHand) if (r != null) r.enabled = !(cab || Steering);
            if (foot != _gameHandsHidden)
            {
                _gameHandsHidden = foot;
                foreach (var r in _guyHands) if (r != null) r.enabled = !foot;
            }
        }

        // ---- Haptics ----

        private void UpdateHaptics(bool inCab) => _hapticsInCab = inCab;

        private bool _hapticsInCab;
        private Vector3 _smoothDv;

        /// <summary>
        /// Rumble for bumps, landings and crashes only. Measured per physics step (the truck's velocity only
        /// changes there), as the sudden part of the velocity change: steady accelerating, braking and cornering
        /// are filtered out. VR controller actuators are much stronger than gamepad motors at the same value,
        /// so the scale is gentle; Vibration in the VR settings scales it further.
        /// </summary>
        private void FixedUpdate()
        {
            float strength = VrConfig.HapticStrength.Value;
            if (!_hapticsInCab || _car == null || _car.rb == null || strength <= 0f || PauseSystem.paused)
            {
                _prevVel = Vector3.zero;
                _smoothDv = Vector3.zero;
                return;
            }
            var vel = _car.transform.InverseTransformDirection(_car.rb.linearVelocity);
            bool first = _prevVel == Vector3.zero;
            var dv = vel - _prevVel; // m/s per physics step
            _prevVel = vel;
            if (first) return;
            _smoothDv = Vector3.Lerp(_smoothDv, dv, 0.1f); // the steady part (engine, brakes, cornering)
            if (_car.Airbourne()) return;

            float jolt = (dv - _smoothDv).magnitude;
            const float threshold = 0.15f; // m/s in one step: below this it's just the road
            if (jolt < threshold) return;
            float amp = Mathf.Clamp01((jolt - threshold) / 2f) * 0.5f * strength; // max 0.5 at Vibration 100%
            if (amp < 0.03f) return;
            float seconds = Time.fixedDeltaTime * 1.5f;
            Haptic(true, amp * (_l.Grabbing ? 1f : 0.6f), seconds);
            Haptic(false, amp * (_r.Grabbing ? 1f : 0.6f), seconds);
        }

        public static void Haptic(bool left, float amplitude, float seconds)
        {
            var d = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            if (d.isValid) d.SendHapticImpulse(0, Mathf.Clamp01(amplitude), seconds);
        }

        // ---- Visuals ----

        private void PlaceVisuals()
        {
            bool show = ShowHands;
            foreach (var h in _both)
            {
                if (h.Visual == null) continue;
                bool visible = show && h.Tracked;
                if (h.Visual.gameObject.activeSelf != visible) h.Visual.gameObject.SetActive(visible);
                if (!visible) continue;

                // Re-read the pose just before rendering so the hands stick to the controllers.
                ReadPose(h);
                var pos = h.Pos;
                if (h.Grabbing && _wheel != null)
                {
                    var planar = Planar(pos, out _);
                    if (planar.sqrMagnitude > 1e-4f) pos = _wheel.steeringWheel.position + planar.normalized * _radius;
                }
                h.Visual.SetPositionAndRotation(pos, h.Rot);
            }
        }
    }
}
