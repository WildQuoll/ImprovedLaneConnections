using ICities;
using CitiesHarmony.API;
using UnityEngine;

namespace ImprovedLaneConnections
{
    public class Mod : IUserMod
    {
        public string Name => "Improved Lane Connections v2";
        public string Description => "Changes lane connection rules for junctions. Helps with roundabouts, dedicated turning lanes and more.";

        public static string Identifier = "WQ.ILC/";

        public void OnEnabled()
        {
            HarmonyHelper.DoOnHarmonyReady(() => Patcher.PatchAll());
        }

        public void OnDisabled()
        {
            if (HarmonyHelper.IsHarmonyInstalled) Patcher.UnpatchAll();
        }

        public static void LogMessage(string msg)
        {
            Debug.Log(Identifier + msg);
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            helper.AddSpace(16);

            helper.AddDropdown("Lane assignment rules", 
                new string[] {
                    "Default",
                    "More turning lanes (legacy)" },
                config.IsLegacyMode() ? 1 : 0, 
                (index) => { 
                    config.SetLegacyMode(index == 1);
                });
        }

        public static Config config = new Config();
    }
}
