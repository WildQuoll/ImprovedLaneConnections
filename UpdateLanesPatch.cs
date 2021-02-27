using ColossalFramework;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace ImprovedLaneConnections
{
    class LaneDirections
    {
        public int sharpLeft = 0;
        public int left = 0;
        public int forward = 0;
        public int right = 0;
        public int sharpRight = 0;
    }

    class LaneConnectionInfo
    {
        public LaneConnectionInfo(NetLane.Flags direction, byte firstTarget, byte lastTarget)
        {
            this.direction = direction;
            this.firstTarget = firstTarget;
            this.lastTarget = lastTarget;
        }

        // Lane directions - Left, Forward and/or Right. Determines the displayed arrow(s), but does not directly affect traffic
        public NetLane.Flags direction;

        // Indices of the outgoing lanes on the end junction, which this lane connects to (leftmost lane is 0, both in LHT and RHT, then increases clockwise).
        public byte firstTarget;
        public byte lastTarget; // inclusive, unlike NetLane.m_lastTarget which is exclusive

        public int GetLaneCount() { return lastTarget - firstTarget + 1; }

        public override string ToString() { return direction.ToString() + " (lanes: " + firstTarget + " to " + lastTarget + ")"; }
    }

    class LaneSetupFeatures
    {
        // False if minimum requirements aren't met. Other members may not be initialised in that case.
        public bool valid = true;

        public bool hasLeftFwdRightLane = false;
        public bool hasLeftFwdLane = false;
        public bool hasLeftRightLane = false;
        public bool hasFwdRightLane = false;
        public int numFwdLanes = 0; // how many lanes have a fwd direction (incl. fwd + left, fwd + right, fwd + l + r)
        public int fwdConnectionImbalance = 0; // difference between min and max number of fwd connections among all lanes with fwd direction (incl. those with mixed directions)
        public int leftConnectionImbalance = 0; // as above but for left
        public int rightConnectionImbalance = 0; // as above but for right

        // (Left out / Left in) / (Right out / Right in). Inverted if < 1 (to become >= 1). Irrelevant if left and/or right turning lanes are not present.
        // The higher the value the more "unfair" the distribution of left and right turning lanes is.
        public float leftRightOutInRatioImbalance = 1.0f;

        // Returns true if the lane setup described by this object is preferred over 'other'. Assumes RHT
        public bool IsBetterThan(LaneSetupFeatures other)
        {
            // Invalid lane setups should never be used.
            if (valid != other.valid)
            {
                return valid;
            }

            // Avoid L+F+R lanes if possible.
            if (hasLeftFwdRightLane != other.hasLeftFwdRightLane)
            {
                return !hasLeftFwdRightLane;
            }

            // Avoid L+R if possible.
            if (hasLeftRightLane != other.hasLeftRightLane)
            {
                return !hasLeftRightLane;
            }

            // Avoid L+F lanes if possible.
            if (hasLeftFwdLane != other.hasLeftFwdLane)
            {
                return !hasLeftFwdLane;
            }

            // Avoid F+R lanes if possible.
            if (hasFwdRightLane != other.hasFwdRightLane)
            {
                return !hasFwdRightLane;
            }

            // Prefer setups with a larger number of incoming lanes with forward direction (i.e. limit lane splitting on forward connections).
            if (numFwdLanes != other.numFwdLanes)
            {
                return numFwdLanes > other.numFwdLanes;
            }

            // Prefer setups where the number of forward connections is (more) evenly distributed among incoming lanes (including L+F and F+R lanes).
            if (fwdConnectionImbalance != other.fwdConnectionImbalance)
            {
                return fwdConnectionImbalance < other.fwdConnectionImbalance;
            }

            if ((rightConnectionImbalance < 2) != (other.rightConnectionImbalance < 2))
            {
                return rightConnectionImbalance < 2;
            }

            if ((leftConnectionImbalance < 2) != (other.leftConnectionImbalance < 2))
            {
                return leftConnectionImbalance < 2;
            }

            // Prefer setups where the number of left connections is (more) evenly distributed among incoming lanes (including L+F and L+R lanes).
            // Difference of 1 is OK. In practice, we are only trying to avoid imbalance between Left-only lanes and Left+Fwd or Left+Right lanes.
            if ((leftConnectionImbalance < 2) != (other.leftConnectionImbalance < 2))
            {
                return leftConnectionImbalance < 2;
            }

            // As above, but for right-turning lanes.
            if ((rightConnectionImbalance < 2) != (other.rightConnectionImbalance < 2))
            {
                return rightConnectionImbalance < 2;
            }

            // Prefer setups where left and right turning lanes are (more) proportionally distributed
            if (leftRightOutInRatioImbalance != other.leftRightOutInRatioImbalance)
            {
                return leftRightOutInRatioImbalance < other.leftRightOutInRatioImbalance;
            }

            // equivalent
            return false;
        }

        public override string ToString()
        {
            return "Is valid: " + valid + "\n"
                + "Has LFR lane: " + hasLeftFwdRightLane + "\n"
                + "Has LF lane: " + hasLeftFwdLane + "\n"
                + "Has LR lane: " + hasLeftRightLane + "\n"
                + "Has FR lane: " + hasFwdRightLane + "\n"
                + "All Fwd lanes: " + numFwdLanes + "\n"
                + "Fwd imbalance: " + fwdConnectionImbalance + "\n"
                + "L imbalance: " + leftConnectionImbalance + "\n"
                + "R imbalance: " + rightConnectionImbalance + "\n"
                + "L/R out/in imbalance: " + leftRightOutInRatioImbalance;
        }
    }

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

            if (lanes.forward.Count > 0)
            {
                var actualEndNode = invertedSegment ? startNode : endNode;

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_startNode : data.m_endNode;
                    var outVector = invertedSegment ? -data.m_startDirection : -data.m_endDirection;
                    var sortedLaneIds = lanes.GetForwardLaneIds();

                    LaneConnector.AssignLanes(sortedLaneIds,outVector, segmentID, nodeID, actualEndNode, lht);
                }
            }

            if (lanes.backward.Count > 0)
            {
                var actualEndNode = invertedSegment ? endNode : startNode; // other way around than for forward lanes

                if (IsJunctionNode(actualEndNode))
                {
                    var nodeID = invertedSegment ? data.m_endNode : data.m_startNode;
                    var outVector = invertedSegment ? -data.m_endDirection : -data.m_startDirection;
                    var sortedLaneIds = lanes.GetBackwardLaneIds();

                    LaneConnector.AssignLanes(sortedLaneIds, outVector, segmentID, nodeID, actualEndNode, lht);
                }
            }
        }
    }
}
