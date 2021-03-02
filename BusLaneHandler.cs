using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImprovedLaneConnections
{
    class BusLaneHandler
    {
        public void PreProcess(ref LaneInfo inLanes, ref JunctionInfo nodeInfo)
        {
            int inLaneCount = inLanes.GetLaneCount();
            int inBusLaneCount = inLanes.GetBusLaneCount();

            if (inLaneCount == inBusLaneCount)
            {
                //If all IN lanes are bus lanes, all IN and OUT lanes are treated as normal car lanes.
                return;
            }

            int outLaneCount = nodeInfo.GetLaneCount();
            int outBusLaneCount = nodeInfo.GetBusLaneCount();

            if(outLaneCount == outBusLaneCount)
            {
                //If all OUT lanes are bus lanes, all IN and OUT lanes are also treated as normal car lanes.
                return;
            }

            // If the number of IN car lanes is greater than OUT car lanes, then OUT bus lanes are also treated as normal car lanes.
            bool applyOutLaneRules = (inLaneCount - inBusLaneCount) <= (outLaneCount - outBusLaneCount);

            // Pre-process IN lanes
            int i = 0;

            var posToRemove = new List<float>();
            
            foreach(var lane in inLanes.lanes)
            {
                if (inLanes.busLaneIds.Contains(lane.Value))
                {
                    inBusLanes.Add(lane.Key, lane.Value);
                    inBusLaneIndices.Add(i);

                    inLanes.busLaneIds.Remove(lane.Value);

                    posToRemove.Add(lane.Key);
                }
                i += 1;
            }

            foreach(var pos in posToRemove)
            {
                inLanes.lanes.Remove(pos);
            }

            // Pre-process OUT lanes
            if (applyOutLaneRules)
            {
                outBusLaneIndices = nodeInfo.GetBusLaneIndices();
                outBusLaneDirections = nodeInfo.RemoveLanes(outBusLaneIndices);
            }
        }

        public void PostProcess(ref LaneInfo inLanes, ref List<LaneConnectionInfo> info)
        {
            // Post-process OUT lanes
            for(int i = 0; i < outBusLaneIndices.Count; ++i)
            {
                var busLaneIndex = outBusLaneIndices[i];
                var busLaneDir = ToArrowDirection(outBusLaneDirections[i]);

                var lastInLane = info[info.Count - 1];

                if (busLaneIndex > lastInLane.lastTarget)
                {
                    // If the bus lane is the rightmost OUT lane, add it to the rightmost IN lane.
                    lastInLane.lastTarget += 1;
                    lastInLane.direction |= busLaneDir;
                }
                else
                {
                    for(int k =0; k < info.Count; ++k)
                    {
                        var lane = info[k];

                        if (lane.lastTarget < busLaneIndex)
                        {
                            // The bus lane is to the right of this IN lane's target (with 1 possible exception, see below)
                            continue;
                        }
                        else if (lane.firstTarget > busLaneIndex)
                        {
                            // The bus lane is to the left of this IN lane's target
                            lane.firstTarget += 1;
                            lane.lastTarget += 1;
                        }
                        else
                        {
                            // The bus lane will be assigned to the same IN lane as the OUT lane to the right,
                            // unless the bus left turns left, and the IN lane does not already allow turning left, 
                            // in which case it will be assigned to the IN lane to the left (which should already be turning left).
                            if (busLaneDir == NetLane.Flags.Left &&
                                (lane.direction & busLaneDir) == 0 &&
                                 k > 0)
                            {
                                var previousInLane = info[k - 1];
                                previousInLane.lastTarget += 1;
                                previousInLane.direction |= busLaneDir;

                                lane.firstTarget += 1;
                                lane.lastTarget += 1;
                                continue;
                            }

                            // The bus lane is within this IN lane's target.
                            lane.lastTarget += 1;
                            lane.direction |= busLaneDir;
                        }
                    }
                }
            }

            // Post-process IN lanes
            foreach (var busLaneIndex in inBusLaneIndices)
            {
                var busLane = new LaneConnectionInfo(NetLane.Flags.None, 255, 0);

                if (busLaneIndex > 0)
                {
                    var laneToTheLeft = info[busLaneIndex - 1];
                    //busLane.direction = laneToTheLeft.direction; // Hidden
                    busLane.firstTarget = laneToTheLeft.firstTarget;
                    busLane.lastTarget = laneToTheLeft.lastTarget;
                }

                if (busLaneIndex < info.Count)
                {
                    var laneToTheRight = info[busLaneIndex];
                    //busLane.direction |= laneToTheRight.direction; // Hidden
                    busLane.firstTarget = Math.Min(busLane.firstTarget, laneToTheRight.firstTarget);
                    busLane.lastTarget = Math.Max(busLane.lastTarget, laneToTheRight.lastTarget);
                }

                info.Insert(busLaneIndex, busLane);
            }

            foreach(var busLane in inBusLanes)
            {
                inLanes.lanes.Add(busLane.Key, busLane.Value);
            }
        }
        private static NetLane.Flags ToArrowDirection(Direction dir)
        {
            switch (dir)
            {
                case Direction.SharpLeft:
                case Direction.Left:
                    return NetLane.Flags.Left;
                case Direction.Forward:
                    return NetLane.Flags.Forward;
                case Direction.Right:
                case Direction.SharpRight:
                    return NetLane.Flags.Right;
            }

            return NetLane.Flags.None;
        }

        private readonly List<int> inBusLaneIndices = new List<int>();
        private readonly SortedDictionary<float, uint> inBusLanes = new SortedDictionary<float, uint>();

        private List<int> outBusLaneIndices = new List<int>();
        private List<Direction> outBusLaneDirections = new List<Direction>();
    }
}
