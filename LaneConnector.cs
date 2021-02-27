using ColossalFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine;

namespace ImprovedLaneConnections
{
    static class LaneConnector
    {
        public static void AssignLanes(List<uint> laneIds, Vector3 outVector, NetSegment data, ushort segmentID, ushort nodeID, NetNode junctionNode)
        {
            var outLaneDirs = CountNodeLanes(junctionNode,
                                             nodeID,
                                             segmentID,
                                             NetInfo.Direction.Forward,
                                             vehicleLaneTypes,
                                             VehicleInfo.VehicleType.Car,
                                             outVector);

            int connectableLaneCount = outLaneDirs.left + outLaneDirs.forward + outLaneDirs.right;
            int totalLaneCount = connectableLaneCount + outLaneDirs.sharpLeft + outLaneDirs.sharpRight;
            if (totalLaneCount == 0)
            {
                // This can happen if multiple one-way roads meet creating a dead end.
                return;
            }

            NetManager netManager = Singleton<NetManager>.instance;

            // If the number of connectable lanes is lower than the number of incoming lanes, sharp left/right lanes re-assign some or all of them into "normal" left/right.
            while (laneIds.Count > connectableLaneCount && connectableLaneCount != totalLaneCount)
            {
                if (outLaneDirs.sharpLeft >= outLaneDirs.sharpRight)
                {
                    outLaneDirs.sharpLeft -= 1;
                    outLaneDirs.left += 1;
                }
                else
                {
                    outLaneDirs.sharpRight -= 1;
                    outLaneDirs.right += 1;
                }

                connectableLaneCount += 1;
            }

            List<LaneConnectionInfo> lanesInfo = AssignLanes(laneIds.Count, outLaneDirs.left, outLaneDirs.forward, outLaneDirs.right);

            AccountForSharpTurnLanes(ref lanesInfo, (byte)outLaneDirs.sharpLeft, (byte)outLaneDirs.sharpRight);

            for (int i = 0; i < laneIds.Count; ++i)
            {
                var laneInfo = lanesInfo[i];
                var laneId = laneIds[i];

                // Note: NetLane is a value type 

                netManager.m_lanes.m_buffer[laneId].m_firstTarget = (byte)(laneInfo.firstTarget);
                netManager.m_lanes.m_buffer[laneId].m_lastTarget = (byte)(laneInfo.lastTarget + 1);

                NetLane.Flags flags = (NetLane.Flags)netManager.m_lanes.m_buffer[laneId].m_flags;
                flags &= noDirections;
                flags |= laneInfo.direction;

                netManager.m_lanes.m_buffer[laneId].m_flags = (ushort)flags;
            }
        }

        // For junctions with an equal number of incoming (in) lanes and outgoing (out) lanes.
        private static List<LaneConnectionInfo> AssignLanesOneToOne(int numLanes, int leftOut, int forwardOut, int rightOut)
        {
            var lanesInfo = new List<LaneConnectionInfo>(numLanes);

            byte laneIndex = 0;
            for (int i = 0; i < leftOut; ++i)
            {
                lanesInfo.Add(new LaneConnectionInfo(NetLane.Flags.Left, laneIndex, laneIndex));
                laneIndex += 1;
            }

            for (int i = 0; i < forwardOut; ++i)
            {
                lanesInfo.Add(new LaneConnectionInfo(NetLane.Flags.Forward, laneIndex, laneIndex));
                laneIndex += 1;
            }

            for (int i = 0; i < rightOut; ++i)
            {
                lanesInfo.Add(new LaneConnectionInfo(NetLane.Flags.Right, laneIndex, laneIndex));
                laneIndex += 1;
            }

            return lanesInfo;
        }

        // For junctions with more incoming (in) lanes than outgoing (out) lanes.
        private static List<LaneConnectionInfo> AssignLanesMoreInThanOut(int numLanes, int leftOut, int forwardOut, int rightOut)
        {
            var outLanes = CreateLaneList(leftOut, forwardOut, rightOut);

            int minInLanesPerOutLane = numLanes / outLanes.Count;
            int extraInLanes = numLanes % outLanes.Count;

            var inLanesPerOutLane = new int[outLanes.Count];
            for (int i = 0; i < outLanes.Count; ++i)
            {
                inLanesPerOutLane[i] = minInLanesPerOutLane;
            }

            // If the number of extra lanes is odd, assign the extra lane to the innermost lane (left in RHT, right in LHT).
            int extraInLanesFromRight = extraInLanes / 2;
            int extraInLanesFromLeft = extraInLanes - extraInLanesFromRight;

            bool lht = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True;
            if (lht && extraInLanes % 2 == 1)
            {
                extraInLanesFromRight += 1;
                extraInLanesFromLeft -= 1;
            }

            for (int i = 0; i < extraInLanesFromLeft; ++i)
            {
                inLanesPerOutLane[i] += 1;
            }

            for (int i = 0; i < extraInLanesFromRight; ++i)
            {
                inLanesPerOutLane[outLanes.Count - i - 1] += 1;
            }

            var lanesInfo = new List<LaneConnectionInfo>(numLanes);
            for (byte outIndex = 0; outIndex < outLanes.Count; ++outIndex)
            {
                var connectedInLanes = inLanesPerOutLane[outIndex];

                for (int i = 0; i < connectedInLanes; ++i)
                {
                    lanesInfo.Add(new LaneConnectionInfo(outLanes[outIndex], outIndex, outIndex));
                }
            }

            return lanesInfo;
        }

        // For junctions with more outgoing (out) lanes than incoming (in) lanes.
        private static List<LaneConnectionInfo> AssignLanesMoreOutThanIn(int inLanes, int leftOut, int forwardOut, int rightOut)
        {
            List<NetLane.Flags> outLanes = CreateLaneList(leftOut, forwardOut, rightOut);

            List<List<int>> possibleLaneArrangements = GetAllPossibleLaneConfigurations(inLanes, outLanes.Count);

            bool lht = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True;

            List<LaneConnectionInfo> bestLanesInfo = GetLaneSetup(outLanes, possibleLaneArrangements[0]);
            LaneSetupFeatures bestFeatures = EvaluateLaneSetup(bestLanesInfo, outLanes, lht);

            // Mod.LogMessage("Initial setup:\n" + ToString(bestLanesInfo) + "\nevaluated as:\n" + bestFeatures.ToString());

            for (int i = 1; i < possibleLaneArrangements.Count; ++i)
            {
                var lanesInfo = GetLaneSetup(outLanes, possibleLaneArrangements[i]);
                var features = EvaluateLaneSetup(lanesInfo, outLanes, lht);

                // Mod.LogMessage(ToString(lanesInfo) + "\nevaluated as:\n" + features.ToString());

                if (features.IsBetterThan(bestFeatures, lht))
                {
                    // Mod.LogMessage("This is better than previous best");
                    bestFeatures = features;
                    bestLanesInfo = lanesInfo;
                }
            }

            if (!bestFeatures.valid)
            {
                // Impossible (in theory)
                Mod.LogMessage("Selected setup does not meet minimum requirements! "
                    + string.Join(", ", bestLanesInfo.Select(x => x.ToString()).ToArray())
                    + "Lanes in: " + inLanes
                    + "Lanes out: " + leftOut + "L/" + forwardOut + "F/" + rightOut + "R");
            }

            return bestLanesInfo;
        }

        private static List<LaneConnectionInfo> AssignLanes(int inLanes, int leftOut, int forwardOut, int rightOut)
        {
            int totalLanesOut = leftOut + forwardOut + rightOut;

            if (totalLanesOut < inLanes)
            {
                return AssignLanesMoreInThanOut(inLanes, leftOut, forwardOut, rightOut);
            }
            else if (totalLanesOut == inLanes)
            {
                return AssignLanesOneToOne(inLanes, leftOut, forwardOut, rightOut);
            }
            else // totalLanesOut > numLanes
            {
                return AssignLanesMoreOutThanIn(inLanes, leftOut, forwardOut, rightOut);
            }
        }

        private static List<NetLane.Flags> CreateLaneList(int left, int forward, int right)
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

        // Returns all possible combinations of k elements from the given set.
        private static IEnumerable<IEnumerable<T>> GetCombinations<T>(this IEnumerable<T> set, int k)
        {
            return k == 0 ?
                   new[] { new T[0] } :
                   set.SelectMany((e, i) => set.Skip(i + 1).GetCombinations(k - 1).Select(c => (new[] { e }).Concat(c)));
        }

        // Returns all possible bit array permutations of the specified length and the specified number of set (1) bits.
        private static List<BitArray> GetBitArrayPermutations(int numElements, int numSetBits)
        {
            var extraOutLaneCombinations = GetCombinations(Enumerable.Range(0, numElements), numSetBits);

            var perms = new List<BitArray>();

            foreach (var c in extraOutLaneCombinations)
            {
                var bits = new BitArray(numElements);

                foreach (var i in c)
                {
                    bits[i] = true;
                }

                perms.Add(bits);
            }

            return perms;
        }

        // <summary>
        // Returns all possible lane configurations for the given number of incoming and outgoing lanes.
        // </summary>
        // <param name="inLanes">Number of incoming lanes</param>
        // <param name="outLanes">Number of outgoing lanes</param>
        // <returns>
        // A list of possible configurations.
        // Each configuration is defined by a List<int>, where indices correspond to outgoing lanes (left-to-right in RHT),
        // and values indicate indices of the connected incoming lanes.
        // </returns>
        private static List<List<int>> GetAllPossibleLaneConfigurations(int inLanes, int outLanes)
        {
            int minOutLanesPerInLane = outLanes / inLanes;
            int extraOutLanes = outLanes % inLanes;

            var bitPerms = GetBitArrayPermutations(inLanes, extraOutLanes);

            var perms = new List<List<int>>(bitPerms.Count);

            foreach (var bits in bitPerms)
            {
                var p = new List<int>();

                int inLaneIndex = 0;
                for (int i = 0; i < bits.Count; ++i)
                {
                    for (int j = 0; j < minOutLanesPerInLane; ++j)
                    {
                        p.Add(inLaneIndex);
                    }

                    if (bits[i])
                    {
                        // Add extra lane
                        p.Add(inLaneIndex);
                    }

                    inLaneIndex += 1;
                }

                perms.Add(p);
            }

            return perms;
        }

        // <summary>
        // Calculates lane setup.
        // </summary>
        // <param name="outLanes">A list of outgoing lanes and their directions (left, fwd or right).</param>
        // <param name="laneCorrespondence"> For each outgoing lane (index), defines the connected incoming lane (value).</param>
        // <returns>
        // A list of all incoming lanes with information about them (direction, indices of connected outgoing lanes).
        // </returns>
        private static List<LaneConnectionInfo> GetLaneSetup(List<NetLane.Flags> outLanes, List<int> laneCorrespondence)
        {
            var lanesInfo = new List<LaneConnectionInfo>();

            int currentInLaneIndex = 0;

            int firstLaneIndex = 0;
            var directions = NetLane.Flags.None;
            for (int i = 0; i < outLanes.Count; ++i)
            {
                if (laneCorrespondence[i] > currentInLaneIndex)
                {
                    currentInLaneIndex += 1;

                    int lastLaneIndex = i - 1;
                    lanesInfo.Add(new LaneConnectionInfo(directions, (byte)firstLaneIndex, (byte)lastLaneIndex));
                    firstLaneIndex = i;
                    directions = NetLane.Flags.None;
                }

                directions |= outLanes[i];
            }

            lanesInfo.Add(new LaneConnectionInfo(directions, (byte)firstLaneIndex, (byte)(outLanes.Count - 1)));

            return lanesInfo;
        }

        // Identifies all features of a lane setup, which may be needed to determine which setup is best.
        private static LaneSetupFeatures EvaluateLaneSetup(List<LaneConnectionInfo> lanesInfo, List<NetLane.Flags> outLanes, bool lht)
        {
            var features = new LaneSetupFeatures();
            // If two in lanes with the same direction (e.g. two forward-only lanes) connect to a different
            // number of out lanes, the lane with more connections MUST be to the left (LHT: right) of the other lane.
            if (lht)
            {
                for (int i = lanesInfo.Count - 1; i > 0; --i)
                {
                    if (lanesInfo[i - 1].direction == lanesInfo[i].direction)
                    {
                        if (lanesInfo[i - 1].GetLaneCount() > lanesInfo[i].GetLaneCount())
                        {
                            features.valid = false;
                            return features;
                        }
                    }
                }
            }
            else
            {
                for (int i = 1; i < lanesInfo.Count; ++i)
                {
                    if (lanesInfo[i - 1].direction == lanesInfo[i].direction)
                    {
                        if (lanesInfo[i - 1].GetLaneCount() < lanesInfo[i].GetLaneCount())
                        {
                            features.valid = false;
                            return features;
                        }
                    }
                }
            }

            int minLeftLaneConns = 255;
            int maxLeftLaneConns = 0;
            int minFwdLaneConns = 255;
            int maxFwdLaneConns = 0;
            int minRightLaneConns = 255;
            int maxRightLaneConns = 0;

            int numLeftInLanes = 0;
            int numRightInLanes = 0;

            foreach (var laneInfo in lanesInfo)
            {
                features.hasFwdRightLane |= (laneInfo.direction == NetLane.Flags.ForwardRight);
                features.hasLeftFwdLane |= (laneInfo.direction == NetLane.Flags.LeftForward);
                features.hasLeftRightLane |= (laneInfo.direction == NetLane.Flags.LeftRight);
                features.hasLeftFwdRightLane |= (laneInfo.direction == NetLane.Flags.LeftForwardRight);

                int numFwdConns = 0;
                int numLeftConns = 0;
                int numRightConns = 0;

                for (int i = laneInfo.firstTarget; i <= laneInfo.lastTarget; ++i)
                {
                    var connectionDir = outLanes[i];

                    switch (connectionDir)
                    {
                        case NetLane.Flags.Left:
                            numLeftConns += 1;
                            break;
                        case NetLane.Flags.Forward:
                            numFwdConns += 1;
                            break;
                        case NetLane.Flags.Right:
                            numRightConns += 1;
                            break;
                        default:
                            break;
                    }
                }

                if (numFwdConns > 0)
                {
                    minFwdLaneConns = Math.Min(minFwdLaneConns, numFwdConns);
                    maxFwdLaneConns = Math.Max(maxFwdLaneConns, numFwdConns);

                    features.numFwdLanes += 1;
                }
                if (numLeftConns > 0)
                {
                    minLeftLaneConns = Math.Min(minLeftLaneConns, numLeftConns);
                    maxLeftLaneConns = Math.Max(maxLeftLaneConns, numLeftConns);

                    numLeftInLanes += 1;
                }
                if (numRightConns > 0)
                {
                    minRightLaneConns = Math.Min(minRightLaneConns, numRightConns);
                    maxRightLaneConns = Math.Max(maxRightLaneConns, numRightConns);

                    numRightInLanes += 1;
                }
            }

            features.leftConnectionImbalance = Math.Max(0, maxLeftLaneConns - minLeftLaneConns);
            features.fwdConnectionImbalance = Math.Max(0, maxFwdLaneConns - minFwdLaneConns);
            features.rightConnectionImbalance = Math.Max(0, maxRightLaneConns - minRightLaneConns);

            if (numLeftInLanes > 0 && numRightInLanes > 0)
            {
                int numLeftOutLanes = 0;
                int numRightOutLanes = 0;

                foreach (var outLane in outLanes)
                {
                    if (outLane == NetLane.Flags.Left)
                    {
                        numLeftOutLanes += 1;
                    }
                    else if (outLane == NetLane.Flags.Right)
                    {
                        numRightOutLanes += 1;
                    }
                }

                float leftOutInRatio = (float)numLeftOutLanes / numLeftInLanes;
                float rightOutInRatio = (float)numRightOutLanes / numRightInLanes;

                features.leftRightOutInRatioImbalance = Math.Max(leftOutInRatio, rightOutInRatio) / Math.Min(leftOutInRatio, rightOutInRatio);
            }

            return features;
        }

        // Adapted from NetSegment.CountLanes 
        private static void CountSegmentLanes(NetSegment segment,
                                              NetInfo.Direction direction,
                                              NetInfo.LaneType laneTypes,
                                              VehicleInfo.VehicleType vehicleTypes,
                                              Vector3 directionVector,
                                              ref LaneDirections laneDirs)
        {
            if (segment.m_flags == NetSegment.Flags.None)
            {
                return;
            }

            if (segment.Info == null || segment.Info.m_lanes == null)
            {
                return;
            }

            const float sharpAngleThresholdDeg = 50.1f;
            const float sharpRightThresholdDeg = 180.0f - sharpAngleThresholdDeg;
            const float sharpLeftThresholdDeg = -sharpRightThresholdDeg;
            const float rightThresholdDeg = 30.1f; // The base game threshold is 30
            const float leftThresholdDeg = -30.1f;

            Vector3 vector = (direction == NetInfo.Direction.Forward) ? segment.m_startDirection : segment.m_endDirection;

            float dot = vector.x * directionVector.x + vector.z * directionVector.z;
            float det = vector.x * directionVector.z - vector.z * directionVector.x;
            float angleDeg = Mathf.Atan2(det, dot) * 180.0f / Mathf.PI;

            foreach (NetInfo.Lane lane in segment.Info.m_lanes)
            {
                if (!lane.CheckType(laneTypes, vehicleTypes))
                {
                    continue;
                }
                NetInfo.Direction laneDirection = lane.m_finalDirection;
                if ((segment.m_flags & NetSegment.Flags.Invert) != 0)
                {
                    laneDirection = NetInfo.InvertDirection(laneDirection);
                }

                if ((laneDirection & direction) != 0)
                {
                    if (angleDeg < sharpLeftThresholdDeg)
                    {
                        laneDirs.sharpLeft++;
                    }
                    else if (angleDeg <= leftThresholdDeg)
                    {
                        laneDirs.left++;
                    }
                    else if (angleDeg > sharpRightThresholdDeg)
                    {
                        laneDirs.sharpRight++;
                    }
                    else if (angleDeg >= rightThresholdDeg)
                    {
                        laneDirs.right++;
                    }
                    else
                    {
                        laneDirs.forward++;
                    }
                }
            }
        }

        // Adapted from NetNode.CountLanes
        private static LaneDirections CountNodeLanes(NetNode node,
                                                     ushort nodeID,
                                                     ushort ignoreSegment,
                                                     NetInfo.Direction direction,
                                                     NetInfo.LaneType laneTypes,
                                                     VehicleInfo.VehicleType vehicleTypes,
                                                     Vector3 directionVector)
        {
            var laneDirs = new LaneDirections();

            if (node.m_flags == NetNode.Flags.None)
            {
                return laneDirs;
            }
            NetManager netManager = Singleton<NetManager>.instance;
            for (int i = 0; i < 8; i++)
            {
                ushort segment = node.GetSegment(i);
                if (segment == 0 || segment == ignoreSegment)
                {
                    continue;
                }
                NetInfo.Direction actualDirection = direction;
                if (netManager.m_segments.m_buffer[segment].m_endNode == nodeID)
                {
                    switch (actualDirection)
                    {
                        case NetInfo.Direction.Forward:
                            actualDirection = NetInfo.Direction.Backward;
                            break;
                        case NetInfo.Direction.Backward:
                            actualDirection = NetInfo.Direction.Forward;
                            break;
                    }
                }
                CountSegmentLanes(netManager.m_segments.m_buffer[segment], actualDirection, laneTypes, vehicleTypes, directionVector, ref laneDirs);
            }

            return laneDirs;
        }

        private const NetInfo.LaneType vehicleLaneTypes = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
        private const NetLane.Flags noDirections = ~NetLane.Flags.LeftForwardRight;

        private static void AccountForSharpTurnLanes(ref List<LaneConnectionInfo> lanesInfo, byte sharpLeftLanes, byte sharpRightLanes)
        {
            if (sharpLeftLanes > 0)
            {
                for (int i = 0; i < lanesInfo.Count; ++i)
                {
                    var laneInfo = lanesInfo[i];
                    laneInfo.firstTarget += sharpLeftLanes;
                    laneInfo.lastTarget += sharpLeftLanes;
                }
                lanesInfo[0].firstTarget = 0;
                lanesInfo[0].direction |= NetLane.Flags.Left;
            }

            if (sharpRightLanes > 0)
            {
                lanesInfo[lanesInfo.Count - 1].lastTarget += sharpRightLanes;
                lanesInfo[lanesInfo.Count - 1].direction |= NetLane.Flags.Right;
            }
        }
    }
}
