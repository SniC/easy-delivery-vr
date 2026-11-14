using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace EasyDeliveryVR.Motion
{
    /// <summary>
    /// First person on foot: an item you hold (shovel, lighter, ...) is shown in your right hand instead of the
    /// game's spot in front of the eyes (sItemManager.hands, scaled 1.33x), and big items are shrunk to at most
    /// MaxSize. Only the visual moves: the game resets hands' local pose in its Update and does its reach checks
    /// there, before we move it in LateUpdate / right before rendering.
    /// </summary>
    internal class VrHeldItem : MonoBehaviour
    {
        private const float MaxSize = 0.3f; // meters, largest dimension
        private static readonly Vector3 HoldOffset = new Vector3(0f, 0.02f, 0.10f); // in front of the controller

        private sItemManager _im;
        private float _nextSearch;
        private GameObject _measured;
        private float _baseSize; // item size at hands scale 1

        private float _grow;

        private void OnEnable() => Application.onBeforeRender += BeforeRender;
        private void OnDisable() => Application.onBeforeRender -= BeforeRender;

        private void LateUpdate() => Place(true);
        private void BeforeRender() => Place(false); // latest controller pose

        private void Place(bool fromGame)
        {
            if (!XrRuntime.Running || VrCameraManager.Mode != VrCameraManager.ViewMode.OnFoot) return;
            if (_im == null)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + 1f;
                _im = FindFirstObjectByType<sItemManager>();
                if (_im == null) return;
            }
            if (!_im.holdingItem || _im.heldItem == null || _im.hands == null) return;

            var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!d.isValid
                || !d.TryGetFeatureValue(CommonUsages.devicePosition, out var p)
                || !d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r)
                || !VrCameraManager.TrackingToWorld(p, r, out var handPos, out var handRot))
                return;

            var hands = _im.hands;
            // The game's grow-in animation: hands scale goes 0 -> 1.33 when taking an item out (fresh after its Update).
            if (fromGame) _grow = Mathf.Clamp01(hands.localScale.x / 1.33f);

            if (_measured != _im.heldItem && hands.lossyScale.x > 0.05f)
            {
                _measured = _im.heldItem;
                _baseSize = Measure(_im.heldItem, hands);
            }
            float parentScale = hands.parent != null ? hands.parent.lossyScale.x : 1f;
            float scale = 1.33f; // the game's full size
            if (_measured == _im.heldItem && _baseSize * scale * parentScale > MaxSize) scale = MaxSize / (_baseSize * parentScale);
            hands.localScale = Vector3.one * scale * _grow;

            // Keep the item's own orientation (as designed for the game's view), just move it to the hand.
            hands.position = handPos + handRot * HoldOffset;
        }

        /// <summary>Largest dimension of the item's renderers, per unit of hands scale.</summary>
        private static float Measure(GameObject item, Transform hands)
        {
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || hands.lossyScale.x < 1e-4f) return 0f;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / hands.lossyScale.x;
            Plugin.Log.LogInfo($"VR held item: '{item.name}' {size * hands.lossyScale.x:F2} m at game scale");
            return size;
        }
    }
}
