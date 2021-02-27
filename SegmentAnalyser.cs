using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImprovedLaneConnections
{
    class SegmentLanes
    {
        // Position -> lane ID, sorted by postion (unlike ___m_info.m_lanes which may not be)
        public SortedDictionary<float, uint> forward = new SortedDictionary<float, uint>();
        public SortedDictionary<float, uint> backward = new SortedDictionary<float, uint>();

        public List<uint> GetForwardLaneIds()
        {
            return ToList(forward);
        }

        public List<uint> GetBackwardLaneIds()
        {
            return ToList(backward);
        }

        private static List<uint> ToList(SortedDictionary<float, uint> dict)
        {
            var list = new List<uint>(dict.Count);
            foreach (var e in dict)
            {
                list.Add(e.Value);
            }
            return list;
        }
    }

    static class SegmentAnalyser
    {
        private const NetInfo.LaneType vehicleLaneTypes = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;

        public static SegmentLanes IdentifyLanes(NetManager netManager, NetInfo netInfo, ushort segmentID, uint firstLaneId)
        {
            var lanes = new SegmentLanes();

            uint laneId = firstLaneId;
            foreach (var lane in netInfo.m_lanes)
            {
                bool isVehicleLane = (lane.m_laneType & vehicleLaneTypes) != 0;
                bool isCarLane = (lane.m_vehicleType & VehicleInfo.VehicleType.Car) != 0;
                if (!isVehicleLane || !isCarLane)
                {
                    // Pedestrian lanes, parking lanes, bicycle lanes etc. - ignore
                    laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
                    continue;
                }

                bool isForwardDirection = (lane.m_finalDirection & NetInfo.Direction.Forward) != 0;

                if (isForwardDirection)
                {
                    if (lanes.forward.ContainsKey(lane.m_position))
                    {
                        Mod.LogMessage("Segment " + segmentID + " lane " + laneId + " has the same position as another lane and will be skipped");
                    }
                    else
                    {
                        lanes.forward.Add(lane.m_position, laneId);
                    }
                }
                else
                {
                    if (lanes.backward.ContainsKey(-lane.m_position))
                    {
                        Mod.LogMessage("Segment " + segmentID + " lane " + laneId + " has the same position as another lane and will be skipped");
                    }
                    else
                    {
                        lanes.backward.Add(-lane.m_position, laneId);
                    }
                }

                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }

            return lanes;
        }
    }
}
