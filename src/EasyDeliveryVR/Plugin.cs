using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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
        }
    }
}
