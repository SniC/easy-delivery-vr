using System.Collections.Generic;
using System.Reflection;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Input;

namespace EasyDeliveryVR.SteamFrame
{
    /// <summary>
    /// OpenXR interaction profile for the Steam Frame controllers (XR_VALVE_frame_controller_interaction,
    /// /interaction_profiles/valve/frame_controller_valve). Without it SteamVR presents them as emulated Touch
    /// controllers, which loses the d-pad, the four face buttons and the bumpers.
    ///
    /// Read through UnityEngine.XR.InputDevices with these usages (per hand):
    ///   Primary2DAxis (+Click/Touch), Trigger, TriggerButton, Grip, GripButton, MenuButton (right Menu, left View),
    ///   ShoulderButton (bumper), FaceSouth / FaceEast / FaceWest / FaceNorth (right A / B / X / Y,
    ///   left d-pad down / right / left / up), Device + Pointer poses, Haptic.
    /// No Input System layout is registered: the mod only uses the XR InputDevices API.
    /// </summary>
    public class SteamFrameControllerProfile : OpenXRInteractionFeature
    {
        public const string Extension = "XR_VALVE_frame_controller_interaction";
        public const string Profile = "/interaction_profiles/valve/frame_controller_valve";
        public const string DeviceName = "Steam Frame Controller OpenXR";

        protected override void OnEnable()
        {
            base.OnEnable();
            // Normally copied from the editor-only [OpenXRFeature] attribute at build time; the mod creates
            // features at runtime, so request the extension here.
            typeof(OpenXRFeature).GetField("openxrExtensionStrings", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(this, Extension);
        }

        protected override bool OnInstanceCreate(ulong instance)
        {
            if (!OpenXRRuntime.IsExtensionEnabled(Extension)) return false; // other runtimes: Touch etc. as before
            return base.OnInstanceCreate(instance);
        }

        private static ActionBinding Bind(string path, string hand = null) => new ActionBinding
        {
            interactionPath = path,
            interactionProfileName = Profile,
            userPaths = hand == null ? null : new List<string> { hand },
        };

        private static ActionConfig Action(string name, ActionType type, string usage, params ActionBinding[] bindings) => new ActionConfig
        {
            name = name,
            localizedName = name,
            type = type,
            usages = new List<string> { usage },
            bindings = new List<ActionBinding>(bindings),
        };

        /// <summary>A face position: right hand button, left hand d-pad direction.</summary>
        private static ActionConfig Face(string usage, string rightPath, string leftPath) =>
            Action(char.ToLowerInvariant(usage[0]) + usage.Substring(1), ActionType.Binary, usage,
                Bind(rightPath, UserPaths.rightHand), Bind(leftPath, UserPaths.leftHand));

        protected override void RegisterActionMapsWithRuntime()
        {
            const InputDeviceCharacteristics hand = InputDeviceCharacteristics.HeldInHand
                | InputDeviceCharacteristics.TrackedDevice | InputDeviceCharacteristics.Controller;
            AddActionMap(new ActionMapConfig
            {
                name = "steamframecontroller",
                localizedName = DeviceName,
                desiredInteractionProfile = Profile,
                manufacturer = "Valve",
                serialNumber = "",
                deviceInfos = new List<DeviceConfig>
                {
                    new DeviceConfig { characteristics = hand | InputDeviceCharacteristics.Left, userPath = UserPaths.leftHand },
                    new DeviceConfig { characteristics = hand | InputDeviceCharacteristics.Right, userPath = UserPaths.rightHand },
                },
                actions = new List<ActionConfig>
                {
                    Action("thumbstick", ActionType.Axis2D, "Primary2DAxis", Bind("/input/thumbstick")),
                    Action("thumbstickClicked", ActionType.Binary, "Primary2DAxisClick", Bind("/input/thumbstick/click")),
                    Action("thumbstickTouched", ActionType.Binary, "Primary2DAxisTouch", Bind("/input/thumbstick/touch")),
                    Action("trigger", ActionType.Axis1D, "Trigger", Bind("/input/trigger/value")),
                    Action("triggerPressed", ActionType.Binary, "TriggerButton", Bind("/input/trigger/click")),
                    Action("grip", ActionType.Axis1D, "Grip", Bind("/input/squeeze/value")),
                    Action("gripPressed", ActionType.Binary, "GripButton", Bind("/input/squeeze/click")),
                    Action("bumper", ActionType.Binary, "ShoulderButton", Bind("/input/bumper/click")),
                    Action("menu", ActionType.Binary, "MenuButton",
                        Bind("/input/menu/click", UserPaths.rightHand), Bind("/input/view/click", UserPaths.leftHand)),
                    Face("FaceSouth", "/input/a/click", "/input/dpad_down/click"),
                    Face("FaceEast", "/input/b/click", "/input/dpad_right/click"),
                    Face("FaceWest", "/input/x/click", "/input/dpad_left/click"),
                    Face("FaceNorth", "/input/y/click", "/input/dpad_up/click"),
                    Action("devicePose", ActionType.Pose, "Device", Bind("/input/grip/pose")),
                    Action("pointer", ActionType.Pose, "Pointer", Bind("/input/aim/pose")),
                    Action("haptic", ActionType.Vibrate, "Haptic", Bind("/output/haptic")),
                },
            });
        }
    }
}
