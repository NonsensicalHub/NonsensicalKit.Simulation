using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 车头沿路段往前开时，推算车尾何时离开身后的那一段。
    /// 自检和路段令牌用同一套推进，避免一边放行、一边仍判占用。
    /// </summary>
    public static class ConveyorBodyClearance
    {
        /// <summary>
        /// 车头从当前位置开到目标点。
        /// 目标时刻晚于现在，则先停在原地，再开过这段距离。
        /// 还没开到目标点，剩余车身就已经让出时返回 true，并给出让出时刻。
        /// </summary>
        public static bool TryAdvance(
            ref double time,
            ref double cursorMeters,
            ref double remainingMeters,
            double arriveTime,
            double targetMeters,
            double speedMetersPerSecond,
            out double clearTime)
        {
            clearTime = time;
            var distance = targetMeters - cursorMeters;
            if (distance <= 1e-4d)
            {
                if (arriveTime > time)
                {
                    time = arriveTime;
                }

                return false;
            }

            var speed = speedMetersPerSecond < 0.01d ? 0.01d : speedMetersPerSecond;
            var geo = distance / speed;
            double moveStart;
            double moveEnd;
            if (arriveTime <= time + 1e-9d)
            {
                moveStart = time;
                moveEnd = time + geo;
            }
            else
            {
                moveStart = arriveTime - geo;
                if (moveStart < time)
                {
                    moveStart = time;
                }

                moveEnd = arriveTime;
                if (moveEnd < moveStart + 1e-9d)
                {
                    moveEnd = moveStart + geo;
                }
            }

            var span = moveEnd - moveStart;
            if (remainingMeters <= distance + 1e-4d)
            {
                var frac = distance > 1e-6d ? remainingMeters / distance : 1d;
                if (frac < 0d)
                {
                    frac = 0d;
                }
                else if (frac > 1d)
                {
                    frac = 1d;
                }

                clearTime = moveStart + span * frac;
                time = clearTime;
                remainingMeters = 0d;
                return true;
            }

            remainingMeters -= distance;
            cursorMeters = targetMeters;
            time = moveEnd;
            return false;
        }
    }
}
