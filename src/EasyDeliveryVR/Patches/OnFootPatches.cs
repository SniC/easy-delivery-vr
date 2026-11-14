using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using HarmonyLib;
using UnityEngine;

namespace EasyDeliveryVR.Patches
{
    [HarmonyPatch]
    internal static class OnFootPatches
    {
        /// <summary>
        /// sCharacterController moves the player relative to its 'perspective' transform, computed from
        /// (-input.x, -input.y), i.e. the perspective faces opposite to where the player walks.
        /// In VR we point it away from the head direction so the stick walks where you look.
        /// </summary>
        [HarmonyPrefix, HarmonyPatch(typeof(sCharacterController), "Update")]
        private static void HeadRelativeWalk(sCharacterController __instance)
        {
            if (!XrRuntime.Running || VrCameraManager.Mode != VrCameraManager.ViewMode.OnFoot) return;
            if (__instance.perspective == null) return;
            __instance.perspective.rotation = Quaternion.LookRotation(-VrCameraManager.HeadForwardFlat, Vector3.up);
        }
    }
}
