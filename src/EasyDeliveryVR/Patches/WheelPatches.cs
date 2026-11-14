using EasyDeliveryVR.Motion;
using HarmonyLib;
using UnityEngine;

namespace EasyDeliveryVR.Patches
{
    [HarmonyPatch]
    internal static class WheelPatches
    {
        /// <summary>
        /// While the wheel is held, steering comes straight from the wheel angle. Setting both the target
        /// and the current input skips the game's steering smoothing, so the truck follows your hands without lag.
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(sCarController), nameof(sCarController.SetInput), typeof(Vector2))]
        private static void WheelSteering(sCarController __instance)
        {
            if (!VrHands.Steering || __instance.player < 0 || __instance.GuyActive) return;
            float x = MirrorMode.mirrored ? -VrHands.SteerValue : VrHands.SteerValue;
            __instance.targetInput.x = x;
            __instance.input.x = x;
        }
    }
}
