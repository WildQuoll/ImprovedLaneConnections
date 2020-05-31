using ColossalFramework;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ImprovedLaneConnections
{
    class LaneInfo
    {
        public LaneInfo(NetLane.Flags direction, byte firstTarget, byte lastTarget)
        {
            this.direction = direction;
            this.firstTarget = firstTarget;
            this.lastTarget = lastTarget;
        }

        // Arrow directions - Left, Forward and/or Right. Does not directly affect traffic
        public NetLane.Flags direction;

        // The first and last lane on the junction this lane leads to which this lane connects to.
        // (Clockwise in RHD, starting from 0).
        public byte firstTarget;
        public byte lastTarget; // inclusive, unlike NetLane.m_lastTarget which is exclusive

        public int GetLaneCount() { return lastTarget - firstTarget + 1; }
    }

    [HarmonyPatch(typeof(RoadBaseAI), "UpdateLanes")]
    public static class UpdateLanesPatch
    {
        private const NetInfo.LaneType vehicleLaneTypes = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
        private const NetLane.Flags noDirections = ~NetLane.Flags.LeftForwardRight;

        private static List<NetLane.Flags> GetLaneList(int left, int forward, int right)
        {
            var laneList = new List<NetLane.Flags>();

            for (int i = 0; i < left; ++i)
            {
                laneList.Add(NetLane.Flags.Left);
            }
            for (int i = 0; i < forward; ++i)
            {
                laneList.Add(NetLane.Flags.Forward);
            }
            for (int i = 0; i < right; ++i)
            {
                laneList.Add(NetLane.Flags.Right);
            }

            return laneList;
        }

        // For junctions with an equal number of incoming (in) lanes and outgoing (out) lanes.
        private static List<LaneInfo> AssignLanesOneToOne(int numLanes, int leftOut, int forwardOut, int rightOut)
        {
            Mod.DebugMessage("Assigning IN == OUT");

            var lanesInfo = new List< LaneInfo >(numLanes);

            byte laneIndex = 0;
            for (int i = 0; i < leftOut; ++i)
            {
                lanesInfo.Add(new LaneInfo(NetLane.Flags.Left, laneIndex, laneIndex));
                laneIndex += 1;
            }

            for (int i = 0; i < forwardOut; ++i)
            {
                lanesInfo.Add(new LaneInfo(NetLane.Flags.Forward, laneIndex, laneIndex));
                laneIndex += 1;
            }

            for (int i = 0; i < rightOut; ++i)
            {
                lanesInfo.Add(new LaneInfo(NetLane.Flags.Right, laneIndex, laneIndex));
                laneIndex += 1;
            }

            return lanesInfo;
        }

        // For junctions with more incoming (in) lanes than outgoing (out) lanes.
        private static List<LaneInfo> AssignLanesMoreInThanOut(int numLanes, int leftOut, int forwardOut, int rightOut, bool lhd)
        {
            Mod.DebugMessage("Assigning IN > OUT");

            var outLanes = GetLaneList(leftOut, forwardOut, rightOut);

            int minInLanesPerOutLane = numLanes / outLanes.Count;
            int extraInLanes = numLanes % outLanes.Count;

            var inLanesPerOutLane = new int[outLanes.Count];
            for(int i = 0; i < outLanes.Count; ++i)
            {
                inLanesPerOutLane[i] = minInLanesPerOutLane;
            }

            int extraInLanesFromRight;
            int extraInLanesFromLeft;
            if (lhd)
            {
                // LHD -> prefer to merge from the right
                extraInLanesFromLeft = extraInLanes / 2;
                extraInLanesFromRight = extraInLanes - extraInLanesFromLeft;
            }
            else
            {
                // RHD - prefer to merge from the left
                extraInLanesFromRight = extraInLanes / 2;
                extraInLanesFromLeft = extraInLanes - extraInLanesFromRight;
            }

            for(int i = 0; i < extraInLanesFromLeft; ++i)
            {
                inLanesPerOutLane[i] += 1;
            }

            for (int i = 0; i < extraInLanesFromRight; ++i)
            {
                inLanesPerOutLane[outLanes.Count - i - 1] += 1;
            }

            var lanesInfo = new List<LaneInfo>(numLanes);
            for (byte outIndex = 0; outIndex < outLanes.Count; ++outIndex)
            {
                var connectedInLanes = inLanesPerOutLane[outIndex];

                for (int i = 0; i < connectedInLanes; ++i)
                {
                    lanesInfo.Add(new LaneInfo(outLanes[outIndex], outIndex, outIndex));
                }
            }

            return lanesInfo;
        }

        // For junctions with more outgoing (out) lanes than incoming (in) lanes.
        private static List<LaneInfo> AssignLanesMoreOutThanIn(int numLanes, int leftOut, int forwardOut, int rightOut, bool lhd)
        {
            Mod.DebugMessage("Assigning IN < OUT");

            List< NetLane.Flags > outLanes = GetLaneList(leftOut, forwardOut, rightOut);

            int minOutLanesPerInLane = outLanes.Count / numLanes;
            int extraOutLanes = outLanes.Count % numLanes;

            var lanesInfo = new List<LaneInfo>(numLanes);
            {
                int firstOutLaneIndex = 0;
                for(int i = 0; i < numLanes; ++i)
                {
                    int lastOutLaneIndex = firstOutLaneIndex + minOutLanesPerInLane - 1;

                    // Assign extra out lanes to inner (RHD: left, LHD: right) in lanes.
                    if (lhd)
                    {
                        if (numLanes - i <= extraOutLanes)
                        {
                            lastOutLaneIndex += 1;
                        }
                    }
                    else
                    {
                        if (i < extraOutLanes)
                        {
                            lastOutLaneIndex += 1;
                        }
                    }

                    // Don't set direction flags yet
                    lanesInfo.Add(new LaneInfo(NetLane.Flags.None, (byte)firstOutLaneIndex, (byte)lastOutLaneIndex));
                    firstOutLaneIndex = lastOutLaneIndex + 1;
                }
            }

            for (int laneIndex = 0; laneIndex < numLanes; ++laneIndex) // TODO LHD
            {
                var laneInfo = lanesInfo[laneIndex];

                if (outLanes[laneInfo.firstTarget] != outLanes[laneInfo.lastTarget] // this lane has mixed direction
                    && outLanes[laneInfo.firstTarget] == outLanes[laneInfo.lastTarget - 1]) // and removing one lane would make it single-direction
                {
                    bool hasEnoughLanes = true;

                    if (laneInfo.GetLaneCount() == minOutLanesPerInLane)
                    {
                        Mod.DebugMessage("Lane " + laneIndex + " has min allowed number of lanes");
                        // This lane has minimum allowed number of out lanes
                        // Can any lane to the left give up one lane?
                        hasEnoughLanes = false;
                        var requiredLaneType = outLanes[laneInfo.firstTarget];

                        int previousLaneIndex = laneIndex - 1;
                        while (previousLaneIndex >= 0)
                        {
                            var previousLaneInfo = lanesInfo[previousLaneIndex];

                            if (outLanes[previousLaneInfo.lastTarget] != requiredLaneType)
                            {
                                Mod.DebugMessage("No lane to the left can give up a lane");
                                break;
                            }

                            if (previousLaneInfo.GetLaneCount() > minOutLanesPerInLane)
                            {
                                Mod.DebugMessage("Lane " + previousLaneIndex + " will give up a lane");
                                // Give up one lane
                                previousLaneInfo.lastTarget -= 1;
                                laneInfo.firstTarget -= 1;
                                hasEnoughLanes = true;
                                break;
                            }

                            previousLaneIndex -= 1;
                        }
                    }

                    if (!hasEnoughLanes)
                    {
                        // This lane will have to remain mixed
                        continue;
                    }

                    int extraOutLaneRecipient = -1;
                    int nextLaneIndex = laneIndex + 1;
                    while (nextLaneIndex < numLanes)
                    {
                        var nextLaneInfo = lanesInfo[nextLaneIndex];

                        if (nextLaneInfo.GetLaneCount() == minOutLanesPerInLane)
                        {
                            // Can give the extra out lane to this lane
                            extraOutLaneRecipient = nextLaneIndex;
                            break;
                        }
                        nextLaneIndex += 1;
                    }

                    if (extraOutLaneRecipient == -1)
                    {
                        Mod.DebugMessage("No lane to the right can accept an extra lane");
                        // This lane will have to remain mixed
                        continue;
                    }

                    Mod.DebugMessage("Lane " + nextLaneIndex + " will aceept an extra lane");

                    laneInfo.lastTarget -= 1;

                    nextLaneIndex = laneIndex + 1;
                    while (nextLaneIndex < extraOutLaneRecipient)
                    {
                        var nextLaneInfo = lanesInfo[nextLaneIndex];

                        nextLaneInfo.firstTarget -= 1;
                        nextLaneInfo.lastTarget -= 1;

                        nextLaneIndex += 1;
                    }

                    lanesInfo[extraOutLaneRecipient].firstTarget -= 1;
                    // lastTarget unchanged
                }
            }

            // Set direction flags to match
            int debugI = 0;
            foreach(var laneInfo in lanesInfo)
            {
                for(int i = laneInfo.firstTarget; i <= laneInfo.lastTarget; ++i)
                {
                    Mod.DebugMessage("Lane " + debugI + " has direction " + outLanes[i]);
                    laneInfo.direction |= outLanes[i];
                }

                debugI += 1;
            }

            return lanesInfo;
        }

        private static List< LaneInfo > AssignLanes(int inLanes, int leftOut, int forwardOut, int rightOut, bool lhd)
        {
            int totalLanesOut = leftOut + forwardOut + rightOut;

            if (totalLanesOut < inLanes)
            {
                return AssignLanesMoreInThanOut(inLanes, leftOut, forwardOut, rightOut, lhd);
            }
            else if (totalLanesOut == inLanes)
            {
                return AssignLanesOneToOne(inLanes, leftOut, forwardOut, rightOut);
            }
            else // totalLanesOut > numLanes
            {
                return AssignLanesMoreOutThanIn(inLanes, leftOut, forwardOut, rightOut, lhd);
            }
        }

        private static void AssignLanes(List< uint > laneIds, Vector3 outVector, NetSegment data, ushort segmentID, ushort nodeID, NetNode junctionNode, bool lhd)
        {
            int numLanes = laneIds.Count;

            int leftOut = 0;
            int forwardOut = 0;
            int rightOut = 0;
            int irrelevant = 0;

            Mod.DebugMessage("Assinging lanes on Node ID: " + nodeID + " Segment ID: " + segmentID);
            Mod.DebugMessage("Direction to count lanes from is " + outVector);

            var direction = NetInfo.Direction.Forward; // direction of lanes we are counting
            junctionNode.CountLanes(nodeID,
                                    segmentID, 
                                    direction, 
                                    vehicleLaneTypes,
                                    VehicleInfo.VehicleType.Car,
                                    outVector,
                                    ref leftOut, ref forwardOut, ref rightOut,
                                    ref irrelevant, ref irrelevant, ref irrelevant);

            Mod.DebugMessage("Left lanes: " + leftOut + " Fwd lanes: " + forwardOut + " Right lanes: " + rightOut);

            if (leftOut + forwardOut + rightOut == 0)
            {
                Mod.DebugMessage("Unexpected: No out lanes on node " + nodeID + " - skipping.");
                return;
            }

            List<LaneInfo> lanesInfo = AssignLanes(laneIds.Count, leftOut, forwardOut, rightOut, lhd);

            Mod.DebugMessage("Lanes calculated");
            for(int i = 0; i < lanesInfo.Count; ++i)
            {
                Mod.DebugMessage("Lane " + i + " direction: " + lanesInfo[i].direction + ", target lanes: " + lanesInfo[i].firstTarget + " to " + lanesInfo[i].lastTarget);
            }

            NetManager netManager = Singleton<NetManager>.instance;
            for (int i = 0; i < laneIds.Count; ++i)
            {
                var laneInfo = lanesInfo[i];
                var laneId = laneIds[i];

                // Note: NetLane is a value type

                netManager.m_lanes.m_buffer[laneId].m_firstTarget = laneInfo.firstTarget;
                netManager.m_lanes.m_buffer[laneId].m_lastTarget = (byte)(laneInfo.lastTarget + 1);

                NetLane.Flags flags = (NetLane.Flags)netManager.m_lanes.m_buffer[laneId].m_flags;
                flags &= noDirections;
                flags |= laneInfo.direction;
                Mod.DebugMessage("Updated lane " + i + " flags are " + flags);

                netManager.m_lanes.m_buffer[laneId].m_flags = (ushort)flags;
            }
        }

        private static List< uint > ToList(SortedDictionary< float, uint> dict)
        {
            var list = new List<uint>(dict.Count);
            foreach(var e in dict)
            {
                list.Add(e.Value);
            }
            return list;
        }

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

            // Position -> lane ID, sorted by postion (unlike ___m_info.m_lanes which may not be)
            var forwardVehicleLanes = new SortedDictionary<float, uint>();
            var backwardVehicleLaneIds = new SortedDictionary<float, uint>();

            uint laneId = data.m_lanes;
            foreach (var lane in ___m_info.m_lanes)
            {
                bool isVehicleLane = (lane.m_laneType & vehicleLaneTypes) != 0;
                if (!isVehicleLane)
                {
                    laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
                    continue;
                }

                bool isForwardDirection = (lane.m_finalDirection & NetInfo.Direction.Forward) != 0;

                if (isForwardDirection)
                {
                    forwardVehicleLanes.Add(lane.m_position, laneId);
                }
                else
                {
                    backwardVehicleLaneIds.Add(-lane.m_position, laneId);
                }

                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }

            Mod.DebugMessage("Updating lanes on segment " + segmentID);
            Mod.DebugMessage(forwardVehicleLanes.Count + " FWD lanes, " + backwardVehicleLaneIds.Count + " BWD lanes");

            bool lhd = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True;

            // Every other segment is "inverted"
            bool invertedSegment = (netManager.m_segments.m_buffer[segmentID].m_flags & NetSegment.Flags.Invert) != 0;

            Mod.DebugMessage("Segment " + segmentID + " is inverted");
            Mod.DebugMessage("Segment end node is " + data.m_endNode);

            NetNode startNode = netManager.m_nodes.m_buffer[data.m_startNode];
            NetNode endNode = netManager.m_nodes.m_buffer[data.m_endNode];

            if (forwardVehicleLanes.Count > 0)
            {
                Mod.DebugMessage("Segment " + segmentID + " has FWD lanes");
                var actualEndNode = invertedSegment ? startNode : endNode;

                Mod.DebugMessage("Actual end node is " + (invertedSegment ? data.m_startNode : data.m_endNode));
                Mod.DebugMessage("Actual end node is junction: " + IsJunctionNode(actualEndNode));

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_startNode : data.m_endNode;
                    var outVector = invertedSegment ? -data.m_startDirection : -data.m_endDirection;
                    var sortedLaneIds = ToList(forwardVehicleLanes);

                    AssignLanes(sortedLaneIds,outVector, data, segmentID, nodeID, actualEndNode, lhd);
                }
            }

            if (backwardVehicleLaneIds.Count > 0)
            {
                Mod.DebugMessage("Segment " + segmentID + " has BWD lanes");
                var actualEndNode = invertedSegment ? endNode : startNode; // other way around than for forward lanes

                Mod.DebugMessage("Actual end node is " + (invertedSegment ? data.m_endNode : data.m_startNode));
                Mod.DebugMessage("Actual end node is junction: " + IsJunctionNode(actualEndNode));

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_endNode : data.m_startNode;
                    var outVector = invertedSegment ? -data.m_endDirection : -data.m_startDirection;
                    var sortedLaneIds = ToList(backwardVehicleLaneIds);
                    AssignLanes(sortedLaneIds, outVector, data, segmentID, nodeID, actualEndNode, lhd);
                }
            }
        }
    }
}
