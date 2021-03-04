using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImprovedLaneConnections
{
    enum Direction
    {
        SharpLeft,
        Left,
        Forward,
        Right,
        SharpRight
    }

    class JunctionInfo
    {
        private Direction GetDirectionFromAngle(float angleDeg)
        {
            const float sharpAngleThresholdDeg = 50.1f;
            const float sharpRightThresholdDeg = 180.0f - sharpAngleThresholdDeg;
            const float sharpLeftThresholdDeg = -sharpRightThresholdDeg;
            const float rightThresholdDeg = 30.1f; // Note: The default game threshold is 30
            const float leftThresholdDeg = -30.1f;

            if (angleDeg < sharpLeftThresholdDeg)
            {
                return Direction.SharpLeft;
            }
            else if (angleDeg <= leftThresholdDeg)
            {
                return Direction.Left;
            }
            else if (angleDeg > sharpRightThresholdDeg)
            {
                return Direction.SharpRight;
            }
            else if (angleDeg >= rightThresholdDeg)
            {
                return Direction.Right;
            }
            else
            {
                return Direction.Forward;
            }
        }

        public int GetBusLaneCount()
        {
            int count = 0;

            foreach (var road in roads)
            {
                count += road.Value.GetBusLaneCount();
            }

            return count;
        }

        public int GetLaneCount()
        {
            int count = 0;

            foreach(var road in roads)
            {
                count += road.Value.GetLaneCount();
            }

            return count;
        }

        public void AddLanes(LaneInfo laneInfo, float angleDeg)
        {
            if (roads.ContainsKey(angleDeg))
            {
                // Two roads with same angle, could be Road Anarchy + Vanilla Overpass Project
                roads[angleDeg].MergeWith(laneInfo);
            }
            else
            {
                roads.Add(angleDeg, laneInfo);
            }

            laneCounts[GetDirectionFromAngle(angleDeg)] += laneInfo.lanes.Count;
        }

        public List< int> GetBusLaneIndices()
        {
            var allIndices = new List<int>();

            int indexOffset = 0;
            foreach(var road in roads)
            {
                var indices = road.Value.GetBusLaneIndices();

                foreach(var index in indices)
                {
                    allIndices.Add(index + indexOffset);
                }
                indexOffset += road.Value.GetLaneCount();
            }

            return allIndices;
        }

        private SortedDictionary<float, LaneInfo> roads = new SortedDictionary<float, LaneInfo>();

        public Dictionary<Direction, int> laneCounts = new Dictionary<Direction, int>
            {
                { Direction.SharpLeft, 0 },
                { Direction.Left, 0 },
                { Direction.Forward, 0 },
                { Direction.Right, 0 },
                { Direction.SharpRight, 0 }
            };

        private Direction GetLaneDirection(int index)
        {
            if (index < laneCounts[Direction.SharpLeft])
            {
                return Direction.SharpLeft;
            }

            index -= laneCounts[Direction.SharpLeft];

            if (index < laneCounts[Direction.Left])
            {
                return Direction.Left;
            }

            index -= laneCounts[Direction.Left];

            if (index < laneCounts[Direction.Forward])
            {
                return Direction.Forward;
            }

            index -= laneCounts[Direction.Forward];

            if (index < laneCounts[Direction.Right])
            {
                return Direction.Right;
            }

            return Direction.SharpRight;
        }

        public List<Direction> RemoveLanes(List<int> indices)
        {
            var removedDirections = new List<Direction>();

            foreach (var index in indices)
            {
                var direction = GetLaneDirection(index);
                removedDirections.Add(direction);
            }

            // Separate loop to not invalidate indices
            foreach (var dir in removedDirections)
            {
                laneCounts[dir] -= 1;
            }

            return removedDirections;
        }
        private static void SwapValues<T, U>(ref Dictionary<T, U> source, T index1, T index2)
        {
            U temp = source[index1];
            source[index1] = source[index2];
            source[index2] = temp;
        }

        public void Mirror()
        {
            SwapValues(ref laneCounts, Direction.Left, Direction.Right);
            SwapValues(ref laneCounts, Direction.SharpLeft, Direction.SharpRight);

            var mirroredRoads = new SortedDictionary<float, LaneInfo>();
            foreach(var road in roads)
            {
                mirroredRoads.Add(-road.Key, road.Value);
                road.Value.Mirror();
            }

            roads = mirroredRoads;
        }
    }
}
