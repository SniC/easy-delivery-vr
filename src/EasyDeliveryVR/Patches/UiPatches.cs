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

        // HUD markers (drop-off point, "pick up", labels, icons) are placed with the world camera's
        // WorldToScreenPoint, assuming it renders the 456x256 screen texture. In VR it renders the headset,
        // so the markers landed off-screen. Place them where the point appears on the VR HUD panel instead.
        [HarmonyPrefix, HarmonyPatch(typeof(sHUD), "WorldToHUDPoint")]
        private static bool VrHudPoint(sHUD __instance, UnityEngine.Vector3 worldPoint, ref UnityEngine.Vector2 __result)
        {
            if (!XrRuntime.Running || __instance.R == null) return true;
            Ui.VrScreenMirror.WorldToRtPixel(__instance.R.rt, worldPoint, out __result); // off-screen (-100,-100) if not visible
            return false;
        }
    }
}
