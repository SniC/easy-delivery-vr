using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using EasyDeliveryVR.Motion;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Xr;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyDeliveryVR.Ui
{
    /// <summary>
    /// In-headset VR settings panel. Hold Y or the left menu button for 1 s (or press F10) to open/close.
    /// Left stick up/down selects, left/right (or A) changes, B closes. Changes save to the BepInEx config at once.
    /// </summary>
    internal class VrSettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private static VrSettingsMenu _instance;
        private void Awake() => _instance = this;

        public static void Toggle()
        {
            if (_instance == null) return;
            if (IsOpen) _instance.Close(); else _instance.Open();
        }

        private class Item
        {
            public string Name;
            public Func<string> Value;
            public Action<int> Change; // +1 / -1
        }

        private readonly List<Item> _items = new List<Item>();
        private int _selected;
        private Transform _panel;
        private TextMesh _text;
        private Vector2 _dpad;

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Application.onBeforeRender += FollowView;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.onBeforeRender -= FollowView;
        }

        // Panel pose relative to the view anchor (position + AnchorRotation), so it doesn't wobble with the truck.
        private Transform _followAnchor;
        private Vector3 _relPos;
        private Quaternion _relRot;

        private void FollowView()
        {
            if (!IsOpen || _panel == null || _followAnchor == null || _followAnchor != VrCameraManager.Anchor) return;
            if (VrCameraManager.Mode == VrCameraManager.ViewMode.Default) return; // plain parenting is right there
            var ar = VrCameraManager.AnchorRotation;
            _panel.SetPositionAndRotation(_followAnchor.position + ar * _relPos, ar * _relRot);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            if (IsOpen) Close(); // the panel may have been parented to an object of the old scene
        }

        private void Start()
        {
            AddChoice("Cab tilt (bumps/sway)", VrConfig.CabTilt, new[] { 0f, 0.25f, 0.5f, 1f },
                v => v <= 0f ? "Level horizon" : v >= 1f ? "Full (rigid)" : $"{v * 100:F0}%");
            AddChoice("Comfort vignette", VrConfig.Vignette, new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, v => v <= 0f ? "Off" : $"{v * 100:F0}%");
            AddBool("Turning on foot", VrConfig.SmoothTurn, "Smooth", "Snap");
            AddChoice("Snap turn angle", VrConfig.SnapTurnAngle, new[] { 15f, 22.5f, 30f, 45f, 90f }, v => $"{v:0.#}°");
            AddChoice("Smooth turn speed", VrConfig.SmoothTurnSpeed, new[] { 60f, 90f, 120f, 180f, 240f }, v => $"{v:F0}°/s");
            AddChoice("Wheel turn for full lock", VrConfig.WheelLockDegrees, new[] { 90f, 180f, 270f, 360f, 540f }, v => $"{v:F0}°");
            AddChoice("Vibration", VrConfig.HapticStrength, new[] { 0f, 0.5f, 1f, 1.5f, 2f }, v => v <= 0f ? "Off" : $"{v * 100:F0}%");
            AddChoice("Menu/HUD distance", VrConfig.ScreenDistance, new[] { 1.25f, 1.5f, 2f, 2.5f, 3f }, v => $"{v:0.##} m");
            AddChoice("Menu/HUD size", VrConfig.ScreenFov, new[] { 35f, 40f, 45f, 50f, 60f }, v => $"{v:F0}°");
            AddBool("Menu screen on left hand", VrConfig.MenuOnHand, "On (first person)", "Off (floating)");
            AddBool("Truck info on dashboard", VrConfig.DashboardHud, "On", "Off (floating HUD)");
            AddChoice("Dashboard display height", VrConfig.DashboardHeight, Steps(-0.2f, 0.2f, 0.02f), v => $"{v * 100:+0;-0;0} cm");
            AddBool("Show hands", VrConfig.ShowHands, "On", "Off");
            AddBool("Grab things by hand (on foot)", VrConfig.HandGrab, "On", "Off");
            AddBool("Camera shake", VrConfig.DisableCameraShake, "Off", "On");
            AddBool("Retro vertex wobble", VrConfig.RetroVertexSnap, "On", "Off");
            AddChoice("Seat left/right", VrConfig.SeatOffsetX, Steps(-0.15f, 0.15f, 0.01f), v => $"{v * 100:+0;-0;0} cm");
            AddChoice("Seat height", VrConfig.SeatOffsetY, Steps(-0.2f, 0.2f, 0.02f), v => $"{v * 100:+0;-0;0} cm");
            AddChoice("Seat forward/back", VrConfig.SeatOffsetZ, Steps(-0.2f, 0.2f, 0.02f), v => $"{v * 100:+0;-0;0} cm");
            _items.Add(new Item { Name = "Recenter view", Value = () => "press A", Change = d => FindFirstObjectByType<VrCameraManager>()?.Recenter() });
            _items.Add(new Item { Name = "Close", Value = () => "press A / B", Change = d => Close() });
        }

        private static float[] Steps(float min, float max, float step)
        {
            var list = new List<float>();
            for (float v = min; v <= max + step * 0.5f; v += step) list.Add(Mathf.Round(v * 1000f) / 1000f);
            return list.ToArray();
        }

        private void AddChoice(string name, ConfigEntry<float> entry, float[] options, Func<float, string> fmt)
        {
            _items.Add(new Item
            {
                Name = name,
                Value = () => fmt(entry.Value),
                Change = d =>
                {
                    // Step to the next option from the current value (which may be a custom one from the cfg file).
                    float cur = entry.Value;
                    int i = d > 0 ? Array.FindIndex(options, o => o > cur + 1e-4f) : Array.FindLastIndex(options, o => o < cur - 1e-4f);
                    if (i >= 0) entry.Value = options[i];
                }
            });
        }

        private void AddBool(string name, ConfigEntry<bool> entry, string onText, string offText)
        {
            _items.Add(new Item { Name = name, Value = () => entry.Value ? onText : offText, Change = d => entry.Value = !entry.Value });
        }

        private void Update()
        {
            if (!XrRuntime.Running) return;
            VrControllers.Update();

            var kb = Keyboard.current;
            bool toggle = VrControllers.MenuHeld || VrControllers.YHeld || (kb != null && kb.f10Key.wasPressedThisFrame);
            if (toggle) { if (IsOpen) Close(); else Open(); return; }
            if (!IsOpen) return;

            var L = VrControllers.Left;
            var R = VrControllers.Right;
            var dir = Dpad(L.Stick);
            if (dir.y > 0) _selected = (_selected + _items.Count - 1) % _items.Count;
            if (dir.y < 0) _selected = (_selected + 1) % _items.Count;
            if (dir.x != 0) _items[_selected].Change((int)dir.x);
            if (VrControllers.Pressed(R.Primary, VrControllers.PrevRight.Primary)) _items[_selected].Change(1);
            if (VrControllers.Pressed(R.Secondary, VrControllers.PrevRight.Secondary)) { Close(); return; }
            if (dir != Vector2.zero || R.Primary) VrHands.Haptic(true, 0.15f, 0.02f);

            Render();
        }

        /// <summary>Edge-triggered 4-way input from the stick (one step per flick).</summary>
        private Vector2 Dpad(Vector2 s)
        {
            if (_dpad != Vector2.zero)
            {
                if (s.magnitude < 0.3f) _dpad = Vector2.zero;
                return Vector2.zero;
            }
            if (s.magnitude < 0.6f) return Vector2.zero;
            _dpad = Mathf.Abs(s.x) > Mathf.Abs(s.y) ? new Vector2(Mathf.Sign(s.x), 0f) : new Vector2(0f, Mathf.Sign(s.y));
            return _dpad;
        }

        private void Open()
        {
            if (_panel == null) Build();
            var cam = Camera.allCameras.Length > 0 ? FindHmdCamera() : null;
            if (cam == null) return;

            // In front of the head at 1 m, level, moving along with the truck/player.
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _panel.SetParent(null, false);
            _panel.position = cam.transform.position + fwd * 1.0f - Vector3.up * 0.1f;
            _panel.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            _followAnchor = VrCameraManager.Anchor;
            if (_followAnchor != null)
            {
                var inv = Quaternion.Inverse(VrCameraManager.AnchorRotation);
                _relPos = inv * (_panel.position - _followAnchor.position);
                _relRot = inv * _panel.rotation;
                _panel.SetParent(_followAnchor, true);
            }
            _panel.gameObject.SetActive(true);
            IsOpen = true;
            _dpad = new Vector2(1, 1); // ignore the stick until it's released
            Render();
        }

        private void Close()
        {
            IsOpen = false;
            if (_panel != null)
            {
                _panel.SetParent(null, false);
                DontDestroyOnLoad(_panel.gameObject);
                _panel.gameObject.SetActive(false);
            }
            Plugin.Instance.Config.Save();
        }

        private static Camera FindHmdCamera()
        {
            foreach (var c in Camera.allCameras)
                if (c.stereoTargetEye == StereoTargetEyeMask.Both && c.targetTexture == null) return c;
            return null;
        }

        private void Build()
        {
            var root = new GameObject("VR Settings Panel");
            DontDestroyOnLoad(root);
            _panel = root.transform;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(_panel, false);
            bg.transform.localPosition = new Vector3(0f, -0.095f, 0.002f);
            bg.transform.localScale = new Vector3(0.92f, 0.86f, 1f);
            var bgMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.05f, 0.05f, 0.06f, 0.96f), renderQueue = 4700 };
            bg.GetComponent<Renderer>().sharedMaterial = bgMat;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_panel, false);
            textGo.transform.localPosition = new Vector3(-0.42f, 0.28f, 0f);
            _text = textGo.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 64;
            _text.characterSize = 0.0045f;
            _text.anchor = TextAnchor.UpperLeft;
            _text.richText = true;
            var textMat = new Material(_text.font.material) { renderQueue = 4710 };
            textMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            textGo.GetComponent<MeshRenderer>().sharedMaterial = textMat;
            root.SetActive(false);
        }

        private void Render()
        {
            if (_text == null) return;
            var sb = new StringBuilder();
            sb.Append("<b>VR SETTINGS</b>\n");
            sb.Append("<size=40><color=#999999>left stick: choose / change   A: change   B or hold Y: close</color></size>\n\n");
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                bool sel = i == _selected;
                string line = $"{it.Name}:  {it.Value()}";
                sb.Append(sel ? $"<color=#ffd24a>>  {line}   < ></color>\n" : $"<color=#dddddd>    {line}</color>\n");
            }
            _text.text = sb.ToString();
        }
    }
}
