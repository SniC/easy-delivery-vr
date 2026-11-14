using UnityEngine;
using UnityEngine.XR;

namespace EasyDeliveryVR.Xr
{
    /// <summary>Thin wrapper over UnityEngine.XR.InputDevices (works with any XR input subsystem).</summary>
    internal static class VrInput
    {
        public static bool TryGetHeadPose(out Vector3 pos, out Quaternion rot)
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (!head.isValid) return false;

            bool hasPos = head.TryGetFeatureValue(CommonUsages.centerEyePosition, out pos)
                          || head.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            bool hasRot = head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rot)
                          || head.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            return hasPos && hasRot;
        }

        public static InputDevice Hand(bool left) =>
            InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);

        public static bool Button(bool left, InputFeatureUsage<bool> usage) =>
            Hand(left).TryGetFeatureValue(usage, out bool v) && v;
    }
}
