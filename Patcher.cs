using HarmonyLib;
using UnityEngine;

namespace ImprovedLaneConnections
{
    public static class Patcher
    {
        private const string HarmonyId = "WQ.ImprovedLaneConnections";
        private static bool patched = false;

        public static void PatchAll()
        {
            if (patched) return;

            patched = true;
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
        }

        public static void UnpatchAll()
        {
            if (!patched) return;

            var harmony = new Harmony(HarmonyId);
            harmony.UnpatchAll(HarmonyId);
            patched = false;
        }
    }
}
