using UnityEngine;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace EasyDeliveryVR.Xr
{
    /// <summary>
    /// Per-frame snapshot of both VR controllers with press/release edges and hold detection.
    /// Read lazily (cached per frame) so it doesn't matter which Update runs first.
    /// </summary>
    internal static class VrControllers
    {
        public struct Hand
        {
            public Vector2 Stick;
            public float Trigger, Grip;
            public bool Primary, Secondary, Menu, StickClick, GripButton;
            // Steam Frame only (see EasyDeliveryVR.SteamFrame): gamepad face positions (right A/B/X/Y,
            // left d-pad down/right/left/up) and the bumper. Primary/Secondary are A/B on the right, unset on the left.
            public bool South, East, West, North, Bumper;
            public bool Valid;
        }

        public static Hand Left, Right, PrevLeft, PrevRight;
        private static int _frame = -1;

        /// <summary>Steam Frame controllers with their own profile: the mod follows the game's gamepad layout.</summary>
        public static bool IsFrame { get; private set; }

        // Hold tracking for stick clicks (tap vs. hold)
        private static float _leftClickTime, _rightClickTime, _menuTime, _yTime;
        private static bool _leftHoldFired, _rightHoldFired, _menuHoldFired, _yHoldFired;
        public static bool BothClicks;           // both sticks clicked together (pause): fires once, suppresses their taps/holds
        private static bool _bothLatched;
        public static bool LeftClickTapped, RightClickTapped, LeftClickHeld, RightClickHeld;
        public static bool MenuTapped, MenuHeld; // left menu button (Steam Frame: View): tap = pause (Frame: map), hold = VR settings
        public static bool YTapped, YHeld;       // left secondary (Y): tap = map, hold = VR settings

        /// <summary>Change camera: right stick tap, or the left stick tap on Steam Frame (like the game's gamepad L3).</summary>
        public static bool CameraTapped => IsFrame ? LeftClickTapped : RightClickTapped;

        public const float HoldSeconds = 1.0f;

        public static void Update()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            PrevLeft = Left;
            PrevRight = Right;
            Left = Read(XRNode.LeftHand);
            Right = Read(XRNode.RightHand);

            Track(Left.StickClick, PrevLeft.StickClick, ref _leftClickTime, ref _leftHoldFired, out LeftClickTapped, out LeftClickHeld);
            Track(Right.StickClick, PrevRight.StickClick, ref _rightClickTime, ref _rightHoldFired, out RightClickTapped, out RightClickHeld);
            Track(Left.Menu, PrevLeft.Menu, ref _menuTime, ref _menuHoldFired, out MenuTapped, out MenuHeld);
            Track(Left.Secondary, PrevLeft.Secondary, ref _yTime, ref _yHoldFired, out YTapped, out YHeld);

            BothClicks = false;
            if (Left.StickClick && Right.StickClick)
            {
                if (!_bothLatched) { _bothLatched = true; BothClicks = true; }
                _leftHoldFired = _rightHoldFired = true; // the chord replaces the single-stick tap/hold actions
                LeftClickTapped = RightClickTapped = LeftClickHeld = RightClickHeld = false;
            }
            else if (!Left.StickClick && !Right.StickClick) _bothLatched = false;
            if (Left.Menu != PrevLeft.Menu) Plugin.Log.LogInfo("VR: left menu button " + (Left.Menu ? "down" : "up"));
        }

        public static bool AnyValid => Left.Valid || Right.Valid;

        public static bool Pressed(bool now, bool prev) => now && !prev;

        /// <summary>The Steam Frame d-pad (left face buttons) as a 4-way vector.</summary>
        public static Vector2 FrameDpad(Hand h) =>
            new Vector2((h.East ? 1f : 0f) - (h.West ? 1f : 0f), (h.North ? 1f : 0f) - (h.South ? 1f : 0f));

        private static readonly System.Collections.Generic.HashSet<string> Logged = new System.Collections.Generic.HashSet<string>();

        private static readonly InputFeatureUsage<bool> FaceSouth = new InputFeatureUsage<bool>("FaceSouth");
        private static readonly InputFeatureUsage<bool> FaceEast = new InputFeatureUsage<bool>("FaceEast");
        private static readonly InputFeatureUsage<bool> FaceWest = new InputFeatureUsage<bool>("FaceWest");
        private static readonly InputFeatureUsage<bool> FaceNorth = new InputFeatureUsage<bool>("FaceNorth");
        private static readonly InputFeatureUsage<bool> Shoulder = new InputFeatureUsage<bool>("ShoulderButton");

        private static Hand Read(XRNode node)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            var h = new Hand { Valid = d.isValid };
            if (!h.Valid) return h;

            if (Logged.Add(node + d.name))
            {
                var usages = new System.Collections.Generic.List<InputFeatureUsage>();
                d.TryGetFeatureUsages(usages);
                var names = new System.Text.StringBuilder();
                foreach (var u in usages) names.Append(u.name).Append(':').Append(u.type.Name).Append(' ');
                Plugin.Log.LogInfo($"VR: {node} device '{d.name}' ({d.characteristics}) usages: {names}");
            }
            d.TryGetFeatureValue(CommonUsages.primary2DAxis, out h.Stick);
            d.TryGetFeatureValue(CommonUsages.trigger, out h.Trigger);
            d.TryGetFeatureValue(CommonUsages.grip, out h.Grip);
            d.TryGetFeatureValue(CommonUsages.menuButton, out h.Menu);
            d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out h.StickClick);
            d.TryGetFeatureValue(CommonUsages.gripButton, out h.GripButton);

            bool frame = d.TryGetFeatureValue(FaceSouth, out h.South);
            if (node == XRNode.RightHand && frame != IsFrame)
            {
                IsFrame = frame;
                Plugin.Log.LogInfo("VR: controller scheme " + (frame ? "Steam Frame (gamepad layout)" : "Touch"));
            }
            if (frame)
            {
                d.TryGetFeatureValue(FaceEast, out h.East);
                d.TryGetFeatureValue(FaceWest, out h.West);
                d.TryGetFeatureValue(FaceNorth, out h.North);
                d.TryGetFeatureValue(Shoulder, out h.Bumper);
                if (node == XRNode.RightHand) { h.Primary = h.South; h.Secondary = h.East; } // A / B for the mod's own panels
            }
            else
            {
                d.TryGetFeatureValue(CommonUsages.primaryButton, out h.Primary);
                d.TryGetFeatureValue(CommonUsages.secondaryButton, out h.Secondary);
            }
            return h;
        }

        /// <summary>Tap fires on release (if the hold didn't fire); hold fires once after HoldSeconds.</summary>
        private static void Track(bool now, bool prev, ref float start, ref bool holdFired, out bool tapped, out bool held)
        {
            tapped = held = false;
            if (now && !prev) { start = Time.unscaledTime; holdFired = false; }
            if (now && !holdFired && Time.unscaledTime - start >= HoldSeconds) { holdFired = true; held = true; }
            if (!now && prev && !holdFired) tapped = true;
        }
    }
}
