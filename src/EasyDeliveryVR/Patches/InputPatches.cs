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
    ///   Right grip ........ horn                Left grip + left stick flick ... radio (d-pad)
    ///   Left stick tap .... headlights          Left stick hold  reset truck
    ///   Right stick tap ... change camera       Right stick hold recenter view
    ///   Right stick ....... camera look (3rd person / on foot)
    ///
    /// Steam Frame controllers (own profile, see VrControllers.IsFrame) follow the game's gamepad layout
    /// instead: see MergeFrameButtons.
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
            if (Ui.VrSettingsMenu.IsOpen) return; // controllers drive the settings panel, not the game
            try { Merge(__instance); }
            finally { DebugLog(__instance); }
        }

        private static void Merge(sInputManager __instance)
        {
            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var pR = VrControllers.PrevRight;
            bool frame = VrControllers.IsFrame;

            var stick = Dz(L.Stick);
            float throttle = R.Trigger - L.Trigger;
            if (Mathf.Abs(throttle) < 0.05f) throttle = 0f;

            // Radio: hold the left grip (away from the wheel) and flick the left stick, like the game's d-pad:
            // left/right = tune to the previous/next station, down = radio on/off, up = start/stop scanning.
            // While the grip is held the stick doesn't steer. The Steam Frame has a real d-pad for this.
            bool radioMode = !frame && !PauseSystem.paused && L.GripButton && !Motion.VrHands.LeftBusy && !Motion.VrGrab.Active;
            var stickDpad = StickToDpad(L.Stick);
            var radioDpad = frame ? VrControllers.FrameDpad(L) : radioMode ? stickDpad : Vector2.zero;
            if (radioMode) stick = Vector2.zero;
            if (radioDpad != Vector2.zero && _prevRadioDpad == Vector2.zero)
            {
                __instance.radioPressed = true;
                __instance.radioInput = MirrorMode.mirrored ? new Vector2(-radioDpad.x, radioDpad.y) : radioDpad;
            }
            _prevRadioDpad = radioDpad;

            float steer = stick.x;
            if (MirrorMode.mirrored) { steer = -steer; }

            __instance.driveInput = Combine(__instance.driveInput, new Vector2(steer, throttle));
            __instance.playerInput = Combine(__instance.playerInput, MirrorMode.mirrored && !PauseSystem.paused ? new Vector2(-stick.x, stick.y) : stick);
            // radioInput drives step-by-step menu navigation (one step per change), so it must be d-pad-like.
            if (!radioMode) __instance.radioInput = Combine(__instance.radioInput, frame ? Combine(stickDpad, radioDpad) : stickDpad);
            __instance.cameraLook = Combine(__instance.cameraLook, Dz(R.Stick));
            // On foot the mod turns the body itself (snap/smooth); the game's per-frame stick turn would double it.
            if (Rendering.VrCameraManager.Mode == Rendering.VrCameraManager.ViewMode.OnFoot)
                __instance.cameraLook = Vector2.zero;
            if (!Ui.VrLaser.HasHit)
                __instance.mouseInput = Combine(__instance.mouseInput, Dz(R.Stick)); // stick cursor fallback

            if (PauseSystem.paused)
            {
                // Right trigger clicks while a menu is open (it's the throttle otherwise).
                __instance.selectPressed |= R.Trigger > 0.6f && pR.Trigger <= 0.6f;
                __instance.selectReleased |= R.Trigger < 0.4f && pR.Trigger >= 0.4f;
            }
            if (frame) MergeFrameButtons(__instance);
            else MergeTouchButtons(__instance);
            if (!Rendering.VrCameraManager.ConsumeCameraButton)
                __instance.cameraPressed |= VrControllers.CameraTapped;
        }

        /// <summary>Touch (and Index/Vive): A/B right, X/Y left, the rest on grips and stick clicks.</summary>
        private static void MergeTouchButtons(sInputManager __instance)
        {
            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var pL = VrControllers.PrevLeft;
            var pR = VrControllers.PrevRight;

            __instance.selectPressed |= VrControllers.Pressed(R.Primary, pR.Primary);
            __instance.selectReleased |= VrControllers.Pressed(pR.Primary, R.Primary);
            __instance.backPressed |= VrControllers.Pressed(R.Secondary, pR.Secondary);
            __instance.backReleased |= VrControllers.Pressed(pR.Secondary, R.Secondary);
            __instance.brakePressed |= R.Secondary;
            __instance.inventoryPressed |= VrControllers.Pressed(L.Primary, pL.Primary);
            __instance.inventoryReleased |= VrControllers.Pressed(pL.Primary, L.Primary);
            __instance.inventoryHeld |= L.Primary;
            __instance.mapPressed |= VrControllers.YTapped; // on release; holding Y opens the VR settings instead
            // Pause: ☰ tap (if the runtime passes it on; Steam Link keeps it), or both stick clicks together.
            // In the game's free camera (cam.exe) pausing is the only way out, so B also pauses there.
            __instance.pausePressed |= VrControllers.MenuTapped || VrControllers.BothClicks
                                       || (__instance.freeCamMode && VrControllers.Pressed(R.Secondary, pR.Secondary));
            // Grips grab the steering wheel when a hand is on or near it; otherwise horn (right) / radio (left).
            // On foot in first person the grips grab things instead (see VrGrab).
            if (!Motion.VrHands.RightBusy && !Motion.VrGrab.Active)
                __instance.hornPressed |= VrControllers.Pressed(R.GripButton, pR.GripButton);
            __instance.headlightsPressed |= VrControllers.LeftClickTapped;
            __instance.resetHeld |= VrControllers.LeftClickHeld;
        }

        /// <summary>
        /// Steam Frame: the game's own gamepad layout. A/B interact and brake/back (following the game's
        /// "swap A/B" option), X headlights, Y reset, LB inventory, RB horn, d-pad radio and menus,
        /// View map (hold: VR settings), Menu pause, left stick click camera. Grips only grab (wheel, items).
        /// </summary>
        private static void MergeFrameButtons(sInputManager __instance)
        {
            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var pL = VrControllers.PrevLeft;
            var pR = VrControllers.PrevRight;

            FrameAB(__instance, out bool ok, out bool pOk, out bool back, out bool pBack);
            __instance.selectPressed |= VrControllers.Pressed(ok, pOk);
            __instance.selectReleased |= VrControllers.Pressed(pOk, ok);
            __instance.backPressed |= VrControllers.Pressed(back, pBack);
            __instance.backReleased |= VrControllers.Pressed(pBack, back);
            __instance.brakePressed |= back;
            __instance.headlightsPressed |= VrControllers.Pressed(R.West, pR.West);
            bool reset = VrControllers.Pressed(R.North, pR.North);
            __instance.resetPressed |= reset;
            __instance.resetHeld |= reset;
            __instance.inventoryPressed |= VrControllers.Pressed(L.Bumper, pL.Bumper);
            __instance.inventoryReleased |= VrControllers.Pressed(pL.Bumper, L.Bumper);
            __instance.inventoryHeld |= L.Bumper;
            __instance.hornPressed |= VrControllers.Pressed(R.Bumper, pR.Bumper);
            __instance.mapPressed |= VrControllers.MenuTapped; // View, on release; holding it opens the VR settings
            __instance.pausePressed |= VrControllers.Pressed(R.Menu, pR.Menu) || VrControllers.BothClicks
                                       || (__instance.freeCamMode && VrControllers.Pressed(back, pBack));
        }

        /// <summary>
        /// Which Steam Frame button is "ok" and which "back". The game's default action map is "PlayerABSwap"
        /// (gamepad south = ok); its "swap A/B" option switches to "Player" (east = ok).
        /// </summary>
        private static void FrameAB(sInputManager inst, out bool ok, out bool pOk, out bool back, out bool pBack)
        {
            var R = VrControllers.Right;
            var pR = VrControllers.PrevRight;
            bool southOk = SouthIsOk(inst);
            ok = southOk ? R.South : R.East;
            pOk = southOk ? pR.South : pR.East;
            back = southOk ? R.East : R.South;
            pBack = southOk ? pR.East : pR.South;
        }

        /// <summary>The Steam Frame "ok" button is gamepad south unless the game's A/B swap says east.</summary>
        public static bool FrameSouthIsOk() =>
            SouthIsOk(sInputManager.players != null && sInputManager.players.Length > 0 ? sInputManager.players[0] : null);

        private static bool SouthIsOk(sInputManager inst)
        {
            var map = inst != null && inst.input != null ? inst.input.currentActionMap : null;
            return map == null || map.name != "Player";
        }

        [HarmonyPostfix, HarmonyPatch("GetMenuInput")]
        private static void GetMenuInputPostfix(sInputManager __instance)
        {
            if (!XrRuntime.Running || sInputManager.players == null || sInputManager.players[0] != __instance) return;
            VrControllers.Update();
            if (!VrControllers.AnyValid || Ui.VrSettingsMenu.IsOpen) return;

            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var b = __instance.btn;
            var pb = __instance.pbtn;

            b.up |= L.Stick.y > 0.5f;
            b.down |= L.Stick.y < -0.5f;
            b.left |= L.Stick.x < -0.5f;
            b.right |= L.Stick.x > 0.5f;
            if (VrControllers.IsFrame)
            {
                FrameAB(__instance, out bool ok, out _, out bool back, out _);
                b.up |= L.North;
                b.down |= L.South;
                b.left |= L.West;
                b.right |= L.East;
                b.a |= ok || R.Trigger > 0.7f;
                b.b |= back;
                b.start |= R.Menu;
                b.select |= L.Menu;
            }
            else
            {
                b.a |= R.Primary || R.Trigger > 0.7f;
                b.b |= R.Secondary;
                b.start |= L.Menu;
                b.select |= L.Secondary;
            }

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
        private static Vector2 _prevRadioDpad;

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
