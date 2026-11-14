using EasyDeliveryVR.Xr;
using HarmonyLib;
using UnityEngine;

namespace EasyDeliveryVR.Patches
{
    /// <summary>
    /// Merges VR controller input into the game's input state. Gamepad/keyboard keep working;
    /// VR values are combined on top.
    ///
    /// Default mapping (Touch / Index / Vive naming: A/B right, X/Y left):
    ///   Left stick ........ steer (driving) / move (on foot) / navigate menus
    ///   Right trigger ..... throttle            Left trigger ... brake / reverse
    ///   A ................. interact / select   B .............. handbrake / back
    ///   X ................. inventory           Y .............. map
    ///   Left menu ......... pause
    ///   Right grip ........ horn                Left grip ...... radio
    ///   Left stick tap .... headlights          Left stick hold  reset truck
    ///   Right stick tap ... change camera       Right stick hold recenter view
    ///   Right stick ....... camera look (3rd person / on foot)
    /// </summary>
    [HarmonyPatch(typeof(sInputManager))]
    internal static class InputPatches
    {
        private const float Deadzone = 0.15f;

        private static float _nextLog;

        [HarmonyPostfix, HarmonyPatch("GetInput")]
        private static void GetInputPostfix(sInputManager __instance)
        {
            if (!XrRuntime.Running) return;
            VrControllers.Update();
            if (sInputManager.players == null || sInputManager.players.Length == 0 || sInputManager.players[0] != __instance) return;
            if (!VrControllers.AnyValid) return;
            try { Merge(__instance); }
            finally { DebugLog(__instance); }
        }

        private static void Merge(sInputManager __instance)
        {

            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var pL = VrControllers.PrevLeft;
            var pR = VrControllers.PrevRight;

            var stick = Dz(L.Stick);
            float throttle = R.Trigger - L.Trigger;
            if (Mathf.Abs(throttle) < 0.05f) throttle = 0f;

            float steer = stick.x;
            if (MirrorMode.mirrored) { steer = -steer; }

            __instance.driveInput = Combine(__instance.driveInput, new Vector2(steer, throttle));
            __instance.playerInput = Combine(__instance.playerInput, MirrorMode.mirrored && !PauseSystem.paused ? new Vector2(-stick.x, stick.y) : stick);
            // radioInput drives step-by-step menu navigation (one step per change), so it must be d-pad-like.
            __instance.radioInput = Combine(__instance.radioInput, StickToDpad(L.Stick));
            __instance.cameraLook = Combine(__instance.cameraLook, Dz(R.Stick));
            if (!Ui.VrLaser.HasHit)
                __instance.mouseInput = Combine(__instance.mouseInput, Dz(R.Stick)); // stick cursor fallback

            __instance.selectPressed |= VrControllers.Pressed(R.Primary, pR.Primary);
            __instance.selectReleased |= VrControllers.Pressed(pR.Primary, R.Primary);
            if (PauseSystem.paused)
            {
                // Right trigger clicks while a menu is open (it's the throttle otherwise).
                __instance.selectPressed |= R.Trigger > 0.6f && pR.Trigger <= 0.6f;
                __instance.selectReleased |= R.Trigger < 0.4f && pR.Trigger >= 0.4f;
            }
            __instance.backPressed |= VrControllers.Pressed(R.Secondary, pR.Secondary);
            __instance.backReleased |= VrControllers.Pressed(pR.Secondary, R.Secondary);
            __instance.brakePressed |= R.Secondary;
            __instance.inventoryPressed |= VrControllers.Pressed(L.Primary, pL.Primary);
            __instance.inventoryReleased |= VrControllers.Pressed(pL.Primary, L.Primary);
            __instance.inventoryHeld |= L.Primary;
            __instance.mapPressed |= VrControllers.Pressed(L.Secondary, pL.Secondary);
            __instance.pausePressed |= VrControllers.Pressed(L.Menu, pL.Menu);
            __instance.hornPressed |= VrControllers.Pressed(R.GripButton, pR.GripButton);
            __instance.radioPressed |= VrControllers.Pressed(L.GripButton, pL.GripButton);
            __instance.headlightsPressed |= VrControllers.LeftClickTapped;
            __instance.resetHeld |= VrControllers.LeftClickHeld;
            __instance.cameraPressed |= VrControllers.RightClickTapped;
        }

        [HarmonyPostfix, HarmonyPatch("GetMenuInput")]
        private static void GetMenuInputPostfix(sInputManager __instance)
        {
            if (!XrRuntime.Running || sInputManager.players == null || sInputManager.players[0] != __instance) return;
            VrControllers.Update();
            if (!VrControllers.AnyValid) return;

            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var b = __instance.btn;
            var pb = __instance.pbtn;

            b.up |= L.Stick.y > 0.5f;
            b.down |= L.Stick.y < -0.5f;
            b.left |= L.Stick.x < -0.5f;
            b.right |= L.Stick.x > 0.5f;
            b.a |= R.Primary || R.Trigger > 0.7f;
            b.b |= R.Secondary;
            b.start |= L.Menu;
            b.select |= L.Secondary;

            var bp = __instance.btnp;
            bp.up = b.up && !pb.up;
            bp.down = b.down && !pb.down;
            bp.left = b.left && !pb.left;
            bp.right = b.right && !pb.right;
            bp.a = b.a && !pb.a;
            bp.b = b.b && !pb.b;
            bp.start = b.start && !pb.start;
            bp.select = b.select && !pb.select;
        }

        /// <summary>Debug (config Debug/LogInput): every 2 s while a VR control is touched, log what we read and what the truck gets.</summary>
        private static void DebugLog(sInputManager inst)
        {
            var L = VrControllers.Left;
            var R = VrControllers.Right;
            bool active = L.Stick.sqrMagnitude > 0.04f || R.Stick.sqrMagnitude > 0.04f || L.Trigger > 0.1f || R.Trigger > 0.1f
                          || L.Primary || R.Primary || L.Secondary || R.Secondary;
            if (!VrConfig.DebugInput.Value || !active || Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + 2f;
            int idx = sInputManager.players == null ? -2 : System.Array.IndexOf(sInputManager.players, inst);
            var car = Object.FindFirstObjectByType<sCarController>();
            var rally = car != null ? car.GetComponent<sRallyDriving>() : null;
            Plugin.Log.LogInfo($"VR input: L(stick={L.Stick} trig={L.Trigger:F2}) R(trig={R.Trigger:F2}) " +
                               $"-> player {idx}/{sInputManager.players?.Length} merged drive={inst.driveInput} brake={inst.brakePressed} paused={PauseSystem.paused} | " +
                               $"car={(car ? car.name : "none")} player={(car ? car.player : -99)} override={(car ? car.overridePhysics : false)} " +
                               $"target={(car ? car.targetInput : Vector2.zero)} input={(car ? car.input : Vector2.zero)} guy={(car ? car.GuyActive : false)} " +
                               $"rally={(rally ? $"{rally.enabled} p{rally.playerNumber}" : "none")}");
        }

        private static Vector2 _dpad;

        /// <summary>Stick to 4-way d-pad with hysteresis: engage past 0.6, release below 0.3, dominant axis only.</summary>
        private static Vector2 StickToDpad(Vector2 s)
        {
            if (_dpad != Vector2.zero)
            {
                if (s.magnitude < 0.3f) _dpad = Vector2.zero;
                return _dpad;
            }
            if (s.magnitude > 0.6f)
                _dpad = Mathf.Abs(s.x) > Mathf.Abs(s.y) ? new Vector2(Mathf.Sign(s.x), 0f) : new Vector2(0f, Mathf.Sign(s.y));
            return _dpad;
        }

        private static Vector2 Dz(Vector2 v)
        {
            float m = v.magnitude;
            if (m < Deadzone) return Vector2.zero;
            return v.normalized * Mathf.Clamp01((m - Deadzone) / (1f - Deadzone));
        }

        /// <summary>Per axis, keep whichever source is pushed further.</summary>
        private static Vector2 Combine(Vector2 game, Vector2 vr) =>
            new Vector2(Mathf.Abs(vr.x) > Mathf.Abs(game.x) ? vr.x : game.x,
                        Mathf.Abs(vr.y) > Mathf.Abs(game.y) ? vr.y : game.y);
    }
}
