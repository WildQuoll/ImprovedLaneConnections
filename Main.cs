﻿using ICities;
using CitiesHarmony.API;
using UnityEngine;

namespace ImprovedLaneConnections
{
    public class Mod : IUserMod
    {
        public string Name => "Improved Lane Connections";
        public string Description => "Changes lane connection rules for junctions. Helps with roundabouts, dedicated turning lanes and more.";

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
            Debug.Log("WQ.ILC: " + msg);
        }
    }
}
