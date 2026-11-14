using BepInEx;
using BepInEx.Logging;
using EasyDeliveryVR.Rendering;
using EasyDeliveryVR.Ui;
using EasyDeliveryVR.Xr;
using HarmonyLib;
using UnityEngine;

namespace EasyDeliveryVR
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "easydeliveryvr";
        public const string Name = "Easy Delivery VR";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            VrConfig.Bind(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded");

            // Own persistent object: the BepInEx manager object isn't guaranteed to survive scene loads.
            var host = new GameObject("EasyDeliveryVR");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<Diagnostics>();

            if (VrConfig.EnableVr.Value && XrRuntime.Start())
            {
                host.AddComponent<VrCameraManager>();
                host.AddComponent<VrScreenMirror>();
                host.AddComponent<VrSnapshot>();
                host.AddComponent<VrLaser>();
                host.AddComponent<Motion.VrHands>(); // after VrCameraManager: its onBeforeRender must run second
                host.AddComponent<VrSettingsMenu>();
                host.AddComponent<Motion.VrGrab>();
                host.AddComponent<VrVignette>();
                host.AddComponent<VrVertexSnap>();
            }
            else
                Log.LogInfo("VR not started; game runs flat.");
        }

        private void OnApplicationQuit() => XrRuntime.Stop();
    }
}
