using BepInEx.Configuration;

namespace EasyDeliveryVR
{
    internal static class VrConfig
    {
        public static ConfigEntry<bool> EnableVr;
        public static ConfigEntry<bool> ForceCabView;
        public static ConfigEntry<bool> DisableCameraShake;
        public static ConfigEntry<bool> SmoothTruckPhysics;
        public static ConfigEntry<float> ScreenDistance;
        public static ConfigEntry<float> ScreenFov;
        public static ConfigEntry<bool> DebugSnapshots;
        public static ConfigEntry<float> LaserPitch;

        public static void Bind(ConfigFile cfg)
        {
            EnableVr = cfg.Bind("General", "EnableVR", true, "Start the game in VR (OpenXR).");
            ForceCabView = cfg.Bind("Driving", "ForceCabView", true,
                "Start every drive in the first-person cab view. The change-camera button (right stick tap) still switches to the chase camera.");
            DisableCameraShake = cfg.Bind("Comfort", "DisableCameraShake", true, "Disable the acceleration-based camera shake.");
            SmoothTruckPhysics = cfg.Bind("Comfort", "SmoothTruckPhysics", true,
                "Enable rigidbody interpolation on the truck so the cab doesn't judder at headset refresh rates.");
            ScreenDistance = cfg.Bind("UI", "ScreenDistance", 2.0f, "Distance in meters of the virtual screen showing HUD and menus.");
            ScreenFov = cfg.Bind("UI", "ScreenFov", 50f, "Vertical size in degrees of the virtual screen showing HUD and menus.");
            LaserPitch = cfg.Bind("UI", "LaserPitch", 30f,
                "Downward tilt in degrees applied to the laser when the runtime only reports the controller grip pose.");
            DebugSnapshots = cfg.Bind("Debug", "AutoSnapshots", true,
                "Save a flat image of the VR view to BepInEx/vr_snapshots 8 s after each scene load (F9 any time). The headset blinks once when it happens.");
        }
    }
}
