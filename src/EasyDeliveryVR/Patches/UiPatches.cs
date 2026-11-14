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

        // Laser pointer replaces the OS mouse on the in-game screen.
        [HarmonyPostfix, HarmonyPatch(typeof(ScreenSystem), "GetMouse")]
        private static void LaserMouse(ref UnityEngine.Vector2 __result)
        {
            if (XrRuntime.Running && Ui.VrLaser.HasHit) __result = Ui.VrLaser.MousePos;
        }
    }
}
