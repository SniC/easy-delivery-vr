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
            public bool Valid;
        }

        public static Hand Left, Right, PrevLeft, PrevRight;
        private static int _frame = -1;

        // Hold tracking for stick clicks (tap vs. hold)
        private static float _leftClickTime, _rightClickTime, _menuTime;
        private static bool _leftHoldFired, _rightHoldFired, _menuHoldFired;
        public static bool LeftClickTapped, RightClickTapped, LeftClickHeld, RightClickHeld;
        public static bool MenuTapped, MenuHeld; // left menu button: tap = pause, hold = VR settings

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
        }

        public static bool AnyValid => Left.Valid || Right.Valid;

        public static bool Pressed(bool now, bool prev) => now && !prev;

        private static readonly System.Collections.Generic.HashSet<string> Logged = new System.Collections.Generic.HashSet<string>();

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
            d.TryGetFeatureValue(CommonUsages.primaryButton, out h.Primary);
            d.TryGetFeatureValue(CommonUsages.secondaryButton, out h.Secondary);
            d.TryGetFeatureValue(CommonUsages.menuButton, out h.Menu);
            d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out h.StickClick);
            d.TryGetFeatureValue(CommonUsages.gripButton, out h.GripButton);
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
