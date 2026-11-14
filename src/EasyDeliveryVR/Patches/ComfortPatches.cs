using EasyDeliveryVR.Xr;
using HarmonyLib;

namespace EasyDeliveryVR.Patches
{
    /// <summary>Camera effects that cause discomfort in VR. Each prefix skips the original while XR runs.</summary>
    [HarmonyPatch]
    internal static class ComfortPatches
    {
        // FOV is dictated by the headset; the game's speed-based FOV zoom would only fight it.
        [HarmonyPrefix, HarmonyPatch(typeof(sCameraSystem), "DoFOV")]
        private static bool NoFov() => !XrRuntime.Running;

        // Acceleration-driven camera wobble.
        [HarmonyPrefix, HarmonyPatch(typeof(CameraShake), "Update")]
        private static bool NoShake() => !XrRuntime.Running || !VrConfig.DisableCameraShake.Value;

        // Constant sinusoidal yaw jitter used as a cheap antialiasing trick on the low-res screen.
        [HarmonyPrefix, HarmonyPatch(typeof(sFakeAntialias), "Update")]
        private static bool NoJitter() => !XrRuntime.Running;
    }
}
