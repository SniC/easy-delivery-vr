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
        private readonly Hand _l = new Hand { Left = true };
        private readonly Hand _r = new Hand { Left = false };

        private InteriorInteraction _wheel;
        private float _radius;
        private float _wheelAngle;
        private Renderer[] _gameHand;
        private sCarController _car;
        private Vector3 _prevVel;
        private float _nextSearch;

        private void Awake() => _instance = this;
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
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private bool ShowHands =>
            XrRuntime.Running && VrConfig.ShowHands.Value && !PauseSystem.paused &&
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
            _gameHand = _wheel.hand != null ? _wheel.hand.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            Plugin.Log.LogInfo($"VR hands: steering wheel '{wt.name}' radius {_radius:F2} m");
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
                foreach (var h in new[] { _l, _r })
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

            if (_gameHand != null)
                foreach (var r in _gameHand) if (r != null) r.enabled = !steering;
        }

        // ---- Haptics ----

        private void UpdateHaptics(bool inCab)
        {
            if (!inCab || _car == null || _car.rb == null || VrConfig.HapticStrength.Value <= 0f) { _prevVel = Vector3.zero; return; }
            var vel = _car.transform.InverseTransformDirection(_car.rb.linearVelocity);
            var accel = vel - _prevVel; // per frame, like the game's ReactiveRumble
            _prevVel = vel;
            if (_car.Airbourne()) return;
            float amp = Mathf.Clamp01(accel.magnitude / 10f * VrConfig.HapticStrength.Value * 4f);
            if (amp < 0.04f) return;
            Haptic(true, amp * (_l.Grabbing ? 1f : 0.5f), 0.05f);
            Haptic(false, amp * (_r.Grabbing ? 1f : 0.5f), 0.05f);
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
            foreach (var h in new[] { _l, _r })
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
