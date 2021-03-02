﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ImprovedLaneConnections
{
    static class LHTHandler
    {
        public static void Mirror(ref JunctionInfo nodeInfo)
        {
            SwapValues(ref nodeInfo.laneCounts, Direction.Left, Direction.Right);
            SwapValues(ref nodeInfo.laneCounts, Direction.SharpLeft, Direction.SharpRight);
        }

        public static void Mirror(ref List<LaneConnectionInfo> info)
        {
            info.Reverse();

            byte firstTarget = 0;

            foreach(var element in info)
            {
                Mirror(ref element.direction);

                byte lastTarget = (byte)(firstTarget + element.GetLaneCount() - 1);

                element.firstTarget = firstTarget;
                element.lastTarget = lastTarget;

                firstTarget = (byte)(lastTarget + 1);
            }
        }

        private static void SwapValues<T, U>(ref Dictionary< T, U > source, T index1, T index2)
        {
            U temp = source[index1];
            source[index1] = source[index2];
            source[index2] = temp;
        }

        private static void Mirror(ref NetLane.Flags direction)
        {
            switch(direction)
            {
                case NetLane.Flags.Left:
                    direction = NetLane.Flags.Right;
                    break;

                case NetLane.Flags.Right:
                    direction = NetLane.Flags.Left;
                    break;

                case NetLane.Flags.LeftForward:
                    direction = NetLane.Flags.ForwardRight;
                    break;

                case NetLane.Flags.ForwardRight:
                    direction = NetLane.Flags.LeftForward;
                    break;

                default:
                    break;
            }
        }
    }
}
