using BepInEx.Configuration;

namespace EasyDeliveryVR
{
    internal static class VrConfig
    {
        public static ConfigEntry<bool> EnableVr;
        public static ConfigEntry<bool> ForceCabView;
        public static ConfigEntry<bool> DisableCameraShake;
        public static ConfigEntry<float> CabTilt;
        public static ConfigEntry<bool> SmoothTruckPhysics;
        public static ConfigEntry<bool> CenterOnWheel;
        public static ConfigEntry<float> SeatOffsetX, SeatOffsetY, SeatOffsetZ;
        public static ConfigEntry<float> ScreenDistance;
        public static ConfigEntry<float> ScreenFov;
        public static ConfigEntry<bool> DebugSnapshots;
        public static ConfigEntry<float> LaserPitch;
        public static ConfigEntry<bool> DebugInput;
        public static ConfigEntry<bool> FirstPersonOnFoot;
        public static ConfigEntry<bool> SmoothTurn;
        public static ConfigEntry<float> SnapTurnAngle;
        public static ConfigEntry<float> SmoothTurnSpeed;
        public static ConfigEntry<bool> ShowHands;
        public static ConfigEntry<float> WheelLockDegrees;
        public static ConfigEntry<float> WheelGrabDistance;
        public static ConfigEntry<float> HapticStrength;
        public static ConfigEntry<bool> HandGrab;
        public static ConfigEntry<float> GrabRadius;
        public static ConfigEntry<float> Vignette;
        public static ConfigEntry<bool> RetroVertexSnap;
        public static ConfigEntry<bool> DashboardHud;
        public static ConfigEntry<float> DashboardHeight;

        public static void Bind(ConfigFile cfg)
        {
            EnableVr = cfg.Bind("General", "EnableVR", true, "Start the game in VR (OpenXR).");
            ForceCabView = cfg.Bind("Driving", "ForceCabView", true,
                "Start every drive in the first-person cab view. The change-camera button (right stick tap) still switches to the chase camera.");
            CenterOnWheel = cfg.Bind("Driving", "CenterOnWheel", true,
                "When recentering in the cab, line your head up sideways with the steering wheel's center.");
            SeatOffsetX = cfg.Bind("Driving", "SeatOffsetRight", 0f, "Extra seat offset in meters: positive = to the right.");
            SeatOffsetY = cfg.Bind("Driving", "SeatOffsetUp", 0f, "Extra seat offset in meters: positive = higher.");
            SeatOffsetZ = cfg.Bind("Driving", "SeatOffsetForward", 0f, "Extra seat offset in meters: positive = closer to the windshield.");
            CabTilt = cfg.Bind("Comfort", "CabTilt", 0f,
                "How much the cab view follows the truck's roll and pitch (bumps, body sway): 0 = level horizon, follows heading only; 1 = fully rigid with the truck.");
            DisableCameraShake = cfg.Bind("Comfort", "DisableCameraShake", true, "Disable the acceleration-based camera shake.");
            SmoothTruckPhysics = cfg.Bind("Comfort", "SmoothTruckPhysics", true,
                "Enable rigidbody interpolation on the truck so the cab doesn't judder at headset refresh rates.");
            FirstPersonOnFoot = cfg.Bind("OnFoot", "FirstPerson", false,
                "Start on foot in first person (the game's camera mode 2) instead of the over-the-head view. " +
                "Either way, the change-camera button (right stick tap) cycles the game's camera modes; " +
                "head-relative walking and snap/smooth turning apply whenever you're in first person on foot.");
            SmoothTurn = cfg.Bind("OnFoot", "SmoothTurn", false, "Turn smoothly with the right stick instead of in snap steps.");
            SnapTurnAngle = cfg.Bind("OnFoot", "SnapTurnAngle", 30f, "Degrees per snap turn.");
            SmoothTurnSpeed = cfg.Bind("OnFoot", "SmoothTurnSpeed", 120f, "Degrees per second for smooth turning.");
            ShowHands = cfg.Bind("Hands", "ShowHands", true, "Show simple hands at the controllers in the cab and in first person on foot.");
            WheelLockDegrees = cfg.Bind("Hands", "WheelLockDegrees", 270f,
                "How far (degrees) you turn the steering wheel by hand for full steering lock. Lower = more responsive.");
            WheelGrabDistance = cfg.Bind("Hands", "WheelGrabDistance", 0.12f, "How close (meters) a hand must be to the wheel rim to grab it with the grip.");
            HapticStrength = cfg.Bind("Hands", "HapticStrength", 1f, "Controller vibration strength for bumps and crashes (0 = off).");
            HandGrab = cfg.Bind("Hands", "HandGrab", true,
                "In first person on foot: grip near a package, door, truck bed or item to use it; grip again to put a carried package down where your hand is.");
            GrabRadius = cfg.Bind("Hands", "GrabRadius", 0.35f, "How close (meters) your hand must be to something to grab or use it.");
            Vignette = cfg.Bind("Comfort", "Vignette", 0f, "Darken the edges of the view while the view turns artificially (0 = off, 1 = strong).");
            RetroVertexSnap = cfg.Bind("Comfort", "RetroVertexSnap", false,
                "Keep the game's retro vertex snapping (models snap to a 2 cm grid). In VR this makes nearby objects jump with every head/hand movement.");
            DashboardHud = cfg.Bind("UI", "DashboardHud", true,
                "In the cab, show fuel, energy, money, time and the radio on a display above the steering wheel instead of on the floating HUD.");
            DashboardHeight = cfg.Bind("UI", "DashboardHeight", 0f, "Extra height in meters of the dashboard display (negative = lower).");
            ScreenDistance = cfg.Bind("UI", "ScreenDistance", 2.0f, "Distance in meters of the virtual screen showing HUD and menus.");
            ScreenFov = cfg.Bind("UI", "ScreenFov", 50f, "Vertical size in degrees of the virtual screen showing HUD and menus.");
            LaserPitch = cfg.Bind("UI", "LaserPitch", 30f,
                "Downward tilt in degrees applied to the laser when the runtime only reports the controller grip pose.");
            DebugInput = cfg.Bind("Debug", "LogInput", false,
                "Log VR controller values and the truck's received input every 2 s while a control is used.");
            DebugSnapshots = cfg.Bind("Debug", "AutoSnapshots", true,
                "Save a flat image of the VR view to BepInEx/vr_snapshots 8 s after each scene load (F9 any time). The headset blinks once when it happens.");
        }
    }
}
