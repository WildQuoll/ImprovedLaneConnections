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

    class LaneCounts
    {
        public int sharpLeft = 0;
        public int left = 0;
        public int forward = 0;
        public int right = 0;
        public int sharpRight = 0;

        public void AddLanes(int count, Direction direction)
        {
            switch (direction)
            {
                case Direction.SharpLeft:
                    sharpLeft += count;
                    break;
                case Direction.Left:
                    left += count;
                    break;
                case Direction.Forward:
                    forward += count;
                    break;
                case Direction.Right:
                    right += count;
                    break;
                case Direction.SharpRight:
                    sharpRight += count;
                    break;
                default:
                    break;
            }
        }
    }

    class JunctionInfo
    {
        public LaneCounts laneCounts = new LaneCounts();
    }
}
