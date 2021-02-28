using System.Collections.Generic;

namespace ImprovedLaneConnections
{
    class LaneInfo
    {
        // Position -> lane ID, sorted by postion (unlike ___m_info.m_lanes which may not be)
        public SortedDictionary<float, uint> lanes = new SortedDictionary<float, uint>();
        public List<uint> busLaneIds = new List<uint>();
    }

    class SegmentLanes
    {
        public LaneInfo forward = new LaneInfo();
        public LaneInfo backward = new LaneInfo();
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
                    if (lanes.forward.lanes.ContainsKey(lane.m_position))
                    {
                        Mod.LogMessage("Segment " + segmentID + " lane " + laneId + " has the same position as another lane and will be skipped");
                    }
                    else
                    {
                        lanes.forward.lanes.Add(lane.m_position, laneId);

                        if (IsBusLane(lane))
                        {
                            lanes.forward.busLaneIds.Add(laneId);
                        }
                    }
                }
                else
                {
                    if (lanes.backward.lanes.ContainsKey(-lane.m_position))
                    {
                        Mod.LogMessage("Segment " + segmentID + " lane " + laneId + " has the same position as another lane and will be skipped");
                    }
                    else
                    {
                        lanes.backward.lanes.Add(-lane.m_position, laneId);

                        if (IsBusLane(lane))
                        {
                            lanes.backward.busLaneIds.Add(laneId);
                        }
                    }
                }

                laneId = netManager.m_lanes.m_buffer[laneId].m_nextLane;
            }

            return lanes;
        }

        private static bool IsBusLane(NetInfo.Lane lane)
        {
            return lane.m_laneType == NetInfo.LaneType.TransportVehicle;
        }
    }
}
