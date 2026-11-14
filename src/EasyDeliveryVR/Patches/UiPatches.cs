using EasyDeliveryVR.Xr;
using HarmonyLib;

namespace EasyDeliveryVR.Patches
{
    [HarmonyPatch]
    internal static class UiPatches
    {
        // The monitor-only menu camera copies the world camera's FOV, which in VR is the headset's (~100°).
        // The HUD/menu quads are sized to fill that FOV; give them a comfortable fixed size instead.
        [HarmonyPrefix, HarmonyPatch(typeof(pixelPerfectView), "AdjustViewPlane")]
        private static void FixedMenuFov(pixelPerfectView __instance)
        {
            if (XrRuntime.Running && __instance.gameCamera != null)
                __instance.gameCamera.fieldOfView = VrConfig.ScreenFov.Value;
        }
    }
}
