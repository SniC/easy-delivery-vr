using System.Collections.Generic;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace EasyDeliveryVR.Motion
{
    /// <summary>
    /// Hand interaction in first person on foot (M6). Squeeze a grip with your hand near something the game
    /// lets you interact with: a package ("pick up"), the truck bed (open / pick up from truck), a door ("get in"),
    /// an item. The game's own action runs for exactly that target. While carrying a package it follows your
    /// hand; squeeze the grip again to put it down where your hand is (ground or truck bed), using the same
    /// steps as the game's PlacePackage so deliveries still count.
    /// </summary>
    internal class VrGrab : MonoBehaviour
    {
        public static bool Active { get; private set; } // hand interaction mode is on (grips belong to it)

        private sCharacterInteraction _ci;
        private sItemManager _im;
        private float _nextSearch, _nextCandidates;
        private readonly List<Component> _candidates = new List<Component>();
        private readonly Component[] _near = new Component[2];

        // Package carried in the hand: the game's packagePoint is moved to the hand and restored afterwards.
        private bool _ppMoved;
        private Vector3 _ppLocalPos;
        private Quaternion _ppLocalRot;
        private int _carryHand = 1; // 0 = left, 1 = right

        private void Update()
        {
            Active = false;
            if (!XrRuntime.Running || !VrConfig.HandGrab.Value) { RestorePackagePoint(); return; }
            VrControllers.Update();

            if ((_ci == null || _im == null) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _ci = FindFirstObjectByType<sCharacterInteraction>();
                _im = _ci != null && _ci.itemManager != null ? _ci.itemManager : FindFirstObjectByType<sItemManager>();
            }

            bool onFoot = VrCameraManager.Mode == VrCameraManager.ViewMode.OnFoot && _ci != null && _im != null && !PauseSystem.paused;
            if (!onFoot) { RestorePackagePoint(); return; }
            Active = true;

            if (Time.unscaledTime >= _nextCandidates)
            {
                _nextCandidates = Time.unscaledTime + 0.1f;
                RefreshCandidates();
            }

            bool carrying = _ci.packagePlatform != null && _ci.packagePlatform.activeSelf;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                var cur = left ? VrControllers.Left : VrControllers.Right;
                var prev = left ? VrControllers.PrevLeft : VrControllers.PrevRight;
                bool press = cur.Grip > 0.6f && prev.Grip <= 0.6f;
                if (!TryHandPose(left, out var handPos, out _)) { _near[i] = null; continue; }

                if (carrying)
                {
                    _near[i] = null;
                    if (press) TryPlace(handPos, left);
                    continue;
                }

                var target = FindTarget(handPos);
                if (target != null && target != _near[i]) VrHands.Haptic(left, 0.2f, 0.03f); // "you can grab this"
                _near[i] = target;
                if (press && target != null)
                {
                    _carryHand = i;
                    Activate(target);
                    VrHands.Haptic(left, 0.5f, 0.06f);
                }
            }
        }

        // Added after VrCameraManager, so this runs after the head/anchor pose of this frame is set.
        private void OnEnable() => Application.onBeforeRender += CarryInHand;
        private void OnDisable() => Application.onBeforeRender -= CarryInHand;

        private void LateUpdate() => CarryInHand(); // also here, so game code reading packagePoint sees the hand position

        /// <summary>
        /// Carry the package in the hand that grabbed it. Runs again right before rendering with the latest
        /// controller pose (like the hand visuals), otherwise the package lags a frame behind the hand.
        /// </summary>
        private void CarryInHand()
        {
            if (!Active || _ci == null || _ci.packagePlatform == null || !_ci.packagePlatform.activeSelf || _ci.packagePoint == null)
            {
                RestorePackagePoint();
                return;
            }
            if (!TryHandPose(_carryHand == 0, out var pos, out var rot)) return;
            var pp = _ci.packagePoint;
            if (!_ppMoved)
            {
                _ppMoved = true;
                _ppLocalPos = pp.localPosition;
                _ppLocalRot = pp.localRotation;
            }
            pp.position = pos + rot * new Vector3(0f, -0.05f, 0.18f);
        }

        private void RestorePackagePoint()
        {
            if (!_ppMoved) return;
            _ppMoved = false;
            if (_ci != null && _ci.packagePoint != null)
                _ci.packagePoint.SetLocalPositionAndRotation(_ppLocalPos, _ppLocalRot);
        }

        private static bool TryHandPose(bool left, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var d = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            return d.isValid
                   && d.TryGetFeatureValue(CommonUsages.devicePosition, out var p)
                   && d.TryGetFeatureValue(CommonUsages.deviceRotation, out var r)
                   && VrCameraManager.TrackingToWorld(p, r, out pos, out rot);
        }

        // ---- Targets ----

        private void RefreshCandidates()
        {
            _candidates.Clear();
            foreach (var ip in FindObjectsByType<InteractionPoint>(FindObjectsSortMode.None))
                if (ip.enabled && ip.gameObject.activeInHierarchy) _candidates.Add(ip);
            foreach (var item in FindObjectsByType<ItemID>(FindObjectsSortMode.None))
                if (item.gameObject.activeInHierarchy) _candidates.Add(item);
        }

        private Component FindTarget(Vector3 handPos)
        {
            float best = VrConfig.GrabRadius.Value;
            Component found = null;
            foreach (var c in _candidates)
            {
                if (c == null) continue;
                float d = Vector3.Distance(handPos, c.transform.position);
                if (d < best) { best = d; found = c; }
            }
            return found;
        }

        /// <summary>Runs the game's own interaction for this exact target (same checks as pressing A).</summary>
        private void Activate(Component target)
        {
            if (target is InteractionPoint ip)
            {
                _im.closestInteraction = ip;
                _im.closestItem = null;
            }
            else if (target is ItemID item)
            {
                _im.closestItem = item;
                _im.closestInteraction = null;
            }
            else return;
            _im.ButtonPressed();
            Plugin.Log.LogInfo($"VR grab: {target.name} ({target.GetType().Name})");
        }

        // ---- Placing a carried package where the hand is ----

        private void TryPlace(Vector3 handPos, bool left)
        {
            var origin = handPos + Vector3.up * 0.5f;
            var hits = Physics.SphereCastAll(origin, 0.15f, Vector3.down, 3f, -1, QueryTriggerInteraction.Ignore);
            RaycastHit? hit = null;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(_ci.transform)) continue; // the player itself
                if (h.distance <= 0f) continue;                              // overlapping at start
                if (hit == null || h.distance < hit.Value.distance) hit = h;
            }
            if (hit == null) { VrHands.Haptic(left, 0.1f, 0.02f); return; }
            var hitInfo = hit.Value;

            if (hitInfo.rigidbody != null && hitInfo.rigidbody.name == "Car")
            {
                if (!_ci.tailgateOpen)
                {
                    _ci.ToggleTailgate(); // like the game: first press opens the tailgate
                    VrHands.Haptic(left, 0.3f, 0.05f);
                    return;
                }
                RestorePackagePoint();
                var pkg = _ci.packagePoint.GetChild(0).gameObject;
                pkg.name = "Placed In Truck";
                pkg.SetActive(false);
                _ci.truckBedInteraction.name = "close";
                _ci.truckBedInteraction.useIcon = false;
                _ci.truckBedInteraction.callback = "ToggleTailgate";
                _ci.car.ResetFreeze();
            }
            else
            {
                RestorePackagePoint();
                var child = _ci.packagePoint.GetChild(0);
                child.parent = null;
                child.position = hitInfo.point;
                child.localScale = _ci.car.transform.localScale;
                foreach (var col in child.GetComponentsInChildren<Collider>()) col.enabled = true;
            }
            _ci.packagePlatform.SetActive(false);
            _ci.gameObject.SendMessage("Package", true);
            VrHands.Haptic(left, 0.5f, 0.06f);
            Plugin.Log.LogInfo($"VR grab: placed package on '{hitInfo.collider.name}'");
        }
    }
}
