using BepInEx.Configuration;

namespace EasyDeliveryVR
{
    internal static class VrConfig
    {
        public static ConfigEntry<bool> EnableVr;
        public static ConfigEntry<bool> ForceCabView;
        public static ConfigEntry<bool> DisableCameraShake;
        public static ConfigEntry<bool> SmoothTruckPhysics;

        public static void Bind(ConfigFile cfg)
        {
            EnableVr = cfg.Bind("General", "EnableVR", true, "Start the game in VR (OpenXR).");
            ForceCabView = cfg.Bind("Driving", "ForceCabView", true,
                "Start every drive in the first-person cab view. The change-camera button (right stick tap) still switches to the chase camera.");
            DisableCameraShake = cfg.Bind("Comfort", "DisableCameraShake", true, "Disable the acceleration-based camera shake.");
            SmoothTruckPhysics = cfg.Bind("Comfort", "SmoothTruckPhysics", true,
                "Enable rigidbody interpolation on the truck so the cab doesn't judder at headset refresh rates.");
        }
    }
}
