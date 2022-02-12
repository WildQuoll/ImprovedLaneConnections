using ColossalFramework;
using System;
using UnityEngine;

namespace ImprovedLaneConnections
{
    public class Config
    {
        public Config()
        {
            Load(CONFIG_PATH);
        }

        private bool FromString(string s)
        {
            return (s == "1");
        }

        private void Load(string path)
        {
            try
            {
                string text = System.IO.File.ReadAllText(path);

                var splitText = text.Split('\n');

                foreach (var line in splitText)
                {
                    if (!line.Contains("="))
                    {
                        continue;
                    }

                    var splitLine = line.Split('=');
                    if (splitLine.Length != 2)
                    {
                        continue;
                    }
                    switch (splitLine[0])
                    {
                        case "LegacyMode":
                            LegacyMode = FromString(splitLine[1]);
                            break;
                        default:
                            break;
                    }
                }
            }
            catch
            { }
        }

        private void Save(string path)
        {
            string text = "LegacyMode=" + (LegacyMode ? 1 : 0);

            try
            {
                System.IO.File.WriteAllText(path, text);
            }
            catch (Exception e)
            {
                Debug.Log(Mod.Identifier + "Failed to save config: " + e.Message);
            }
        }
        public bool IsLegacyMode()
        {
            return LegacyMode;
        }

        private bool IsValid(NetSegment segment)
        {
            return (segment.m_flags & NetSegment.Flags.Created) != 0
                && (segment.m_flags & (NetSegment.Flags.Collapsed | NetSegment.Flags.Deleted)) == 0;
        }

        public void SetLegacyMode(bool legacy)
        {
            LegacyMode = legacy;

            // Update all lane connections if in game

            if (!Singleton<NetManager>.exists)
            {
                return;
            }

            NetManager netManager = Singleton<NetManager>.instance;

            for (ushort segmentId = 1; segmentId < netManager.m_segments.m_size; ++segmentId)
            {
                var segment = netManager.m_segments.m_buffer[segmentId];
                if (IsValid(segment))
                {
                    segment.UpdateLanes(segmentId, false);
                }
            }
        }

        private const string CONFIG_PATH = "ImprovedLaneConnectionsConfig.txt";

        private bool LegacyMode = false;
    }
}
