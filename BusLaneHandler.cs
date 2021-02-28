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
            if (inLanes.busLaneIds.Count == inLanes.lanes.Count)
            {
                return;
            }

            int i = 0;

            var posToRemove = new List<float>();

            foreach(var lane in inLanes.lanes)
            {
                if (inLanes.busLaneIds.Contains(lane.Value))
                {
                    busLanes.Add(lane.Key, lane.Value);
                    busLaneIndices.Add(i);

                    inLanes.busLaneIds.Remove(lane.Value);

                    posToRemove.Add(lane.Key);
                }
                i += 1;
            }

            foreach(var pos in posToRemove)
            {
                inLanes.lanes.Remove(pos);
            }
        }

        public void PostProcess(ref LaneInfo inLanes, ref List<LaneConnectionInfo> info)
        {
            foreach(var busLaneIndex in busLaneIndices)
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

            foreach(var busLane in busLanes)
            {
                inLanes.lanes.Add(busLane.Key, busLane.Value);
            }
        }

        private List<int> busLaneIndices = new List<int>();
        private SortedDictionary<float, uint> busLanes = new SortedDictionary<float, uint>();
    }
}
