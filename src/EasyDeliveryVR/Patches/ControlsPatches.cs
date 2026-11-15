using System.Collections.Generic;
using EasyDeliveryVR.Xr;
using HarmonyLib;

namespace EasyDeliveryVR.Patches
{
    /// <summary>
    /// The game's controls screen (and its in-game hints) show keyboard keys or gamepad icons. In VR they show
    /// the Touch controller bindings of this mod instead (see InputPatches), drawn where the keyboard key goes.
    /// </summary>
    [HarmonyPatch(typeof(sControlsDisplay))]
    internal static class ControlsPatches
    {
        // By the game's mapping label (sControlsDisplay.mappings). Short: drawn right-aligned in 8 px characters.
        private static readonly Dictionary<string, string> Touch = new Dictionary<string, string>
        {
            { "interact/ok", "A" },
            { "brake/back", "B" },
            { "map/items", "X" },
            { "pause", "L+R STICK" },     // click both sticks (☰ if the runtime passes it on)
            { "job selection", "Y" },
            { "drive", "STICK+TRIG" },    // left stick steers / walks, triggers throttle and brake
            { "camera", "R CLICK" },
            { "reset (hold)", "L CLICK" },
            { "lights", "L CLICK" },
            { "radio on", "GRIP+DOWN" },  // hold left grip, flick the left stick
            { "channels", "GRIP+L/R" },
            { "scan", "GRIP+UP" },
        };

        // Steam Frame controllers: the game's gamepad layout (see InputPatches.MergeFrameButtons).
        private static readonly Dictionary<string, string> Frame = new Dictionary<string, string>
        {
            { "interact/ok", "A" },       // A/B follow the game's swap option, see FrameKey
            { "brake/back", "B" },
            { "map/items", "VIEW/LB" },
            { "pause", "MENU" },
            { "job selection", "VIEW" },
            { "drive", "STICK+TRIG" },
            { "camera", "L CLICK" },
            { "reset (hold)", "Y" },
            { "lights", "X" },
            { "radio on", "D-DOWN" },
            { "channels", "D-L/R" },
            { "scan", "D-UP" },
        };

        private static bool TryGetKey(string label, out string key)
        {
            if (!VrControllers.IsFrame) return Touch.TryGetValue(label, out key);
            if (!Frame.TryGetValue(label, out key)) return false;
            if (!InputPatches.FrameSouthIsOk() && (key == "A" || key == "B")) key = key == "A" ? "B" : "A";
            return true;
        }

        [HarmonyPrefix, HarmonyPatch("DrawControl", typeof(MiniRenderer), typeof(int), typeof(string), typeof(float), typeof(float))]
        private static bool DrawTouchControl(sControlsDisplay __instance, MiniRenderer R, int index, string label, float x, float y)
        {
            if (!XrRuntime.Running || __instance.mappings == null || index < 0 || index >= __instance.mappings.Length) return true;
            if (!TryGetKey(__instance.mappings[index].label ?? "", out var key)) return true; // unknown entry: game's own

            if (label.Length > 0)
            {
                R.fontOptions.alignment = sFancyText.FontOptions.Alignment.left;
                R.fput(LocalizationDictionary.Translate(label), x, y);
            }
            R.put(key, x - key.Length * 8 - 8f, y);
            return false;
        }
    }
}
