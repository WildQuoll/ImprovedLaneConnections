using ColossalFramework;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ImprovedLaneConnections
{
    [HarmonyPriority(Priority.High)] // Higher than TMPE - hopefully enough to ensure compatibility
    [HarmonyPatch(typeof(RoadBaseAI), "UpdateLanes")]
    public static class UpdateLanesPatch
    {
        private static bool IsJunctionNode(NetNode node)
        {
            return (node.m_flags & NetNode.Flags.Junction) != 0;
        }

        [HarmonyPostfix]
        public static void Postfix(ushort segmentID, ref NetSegment data, NetInfo ___m_info)
        {
            if (___m_info.m_lanes.Length == 0)
            {
                return;
            }

            NetManager netManager = Singleton<NetManager>.instance;

            SegmentLanes lanes = SegmentAnalyser.IdentifyLanes(netManager, ___m_info, segmentID, data.m_lanes);

            // Every other segment is "inverted"
            bool invertedSegment = (netManager.m_segments.m_buffer[segmentID].m_flags & NetSegment.Flags.Invert) != 0;

            NetNode startNode = netManager.m_nodes.m_buffer[data.m_startNode];
            NetNode endNode = netManager.m_nodes.m_buffer[data.m_endNode];

            bool lht = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True;

            if (lanes.forward.lanes.Count > 0)
            {
                var actualEndNode = invertedSegment ? startNode : endNode;

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_startNode : data.m_endNode;
                    var outVector = invertedSegment ? -data.m_startDirection : -data.m_endDirection;
                    LaneConnector.AssignLanes(lanes.forward, outVector, segmentID, nodeID, actualEndNode, lht);
                }
            }

            if (lanes.backward.lanes.Count > 0)
            {
                var actualEndNode = invertedSegment ? endNode : startNode; // other way around than for forward lanes

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_endNode : data.m_startNode;
                    var outVector = invertedSegment ? -data.m_endDirection : -data.m_startDirection;

                    LaneConnector.AssignLanes(lanes.backward, outVector, segmentID, nodeID, actualEndNode, lht);
                }
            }
        }
    }
}
