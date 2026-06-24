using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>回放时判断任务在输送/取货点的时间窗（无 Unity 依赖）。</summary>
    internal static class ConveyorPlaybackJobTimeline
    {
        public static bool IsOnConveyor(
            ConveyorSegmentScheduleEntry[] schedule,
            IReadOnlyList<SimSubTask> jobTasks,
            double simTime)
        {
            if (schedule == null || schedule.Length == 0)
            {
                return false;
            }

            var start = schedule[0].DesiredEntrySimTime;
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if ((task.Kind == SimSubTaskKind.InfeedMove || task.Kind == SimSubTaskKind.OutboundMove)
                    && task.StartSimTime < start)
                {
                    start = task.StartSimTime;
                }
            }

            var end = schedule[^1].OccupancyEndSimTime;
            return simTime >= start - 1e-9 && simTime <= end + 1e-9;
        }

        public static bool IsWaitingAtPickup(
            IReadOnlyList<SimSubTask> jobTasks,
            double simTime,
            out int pickupIndex)
        {
            pickupIndex = -1;
            if (SimSubTaskQuery.IsOutboundJob(jobTasks))
            {
                return false;
            }

            var conveyorEnd = 0d;

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];

                if (SimSubTaskKinds.ExtendsConveyorTimeline(task.Kind)
                    && task.EndSimTime > conveyorEnd)
                {
                    conveyorEnd = task.EndSimTime;
                }

                if (task.Kind == SimSubTaskKind.StackerWait && task.StartSimTime > conveyorEnd)
                {
                    conveyorEnd = task.StartSimTime;
                }

                if (task.PickupPointIndex >= 0)
                {
                    pickupIndex = task.PickupPointIndex;
                }
            }

            if (pickupIndex < 0 || simTime < conveyorEnd - 1e-9)
            {
                return false;
            }

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.Kind == SimSubTaskKind.StackerPick
                    && simTime >= task.StartSimTime - 1e-9)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 入库：堆垛机在货位放货完成后，输送线可视应结束。
        /// 出库：堆垛机在取货点放货后仍需继续输送至出库口，此处不得判定为已完成。
        /// </summary>
        public static bool HasCompletedPlacement(IReadOnlyList<SimSubTask> jobTasks, double simTime)
        {
            if (SimSubTaskQuery.IsOutboundJob(jobTasks))
            {
                return false;
            }

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.Kind == SimSubTaskKind.StackerPlace
                    && simTime >= task.EndSimTime - 1e-9)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>出库：堆垛机已在取货点放货，输送尚未接走（无输送子任务或路段表尚未开始）。</summary>
        public static bool IsWaitingAtOutboundPickup(
            IReadOnlyList<SimSubTask> jobTasks,
            ConveyorSegmentScheduleEntry[] schedule,
            double simTime,
            out int pickupIndex)
        {
            pickupIndex = -1;
            if (!SimSubTaskQuery.IsInOutboundConveyorVisualWindow(jobTasks, simTime))
            {
                return false;
            }

            var placeEnd = 0d;
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.PickupPointIndex >= 0)
                {
                    pickupIndex = task.PickupPointIndex;
                }

                if (task.Kind == SimSubTaskKind.StackerPlace && task.EndSimTime > placeEnd)
                {
                    placeEnd = task.EndSimTime;
                }
            }

            if (pickupIndex < 0 || simTime < placeEnd - 1e-9)
            {
                return false;
            }

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (SimSubTaskKinds.IsOutboundPickupQueue(task) && task.ContainsTime(simTime))
                {
                    return true;
                }
            }

            var conveyorStart = double.MaxValue;
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (!SimSubTaskKinds.ExtendsConveyorTimeline(task.Kind))
                {
                    continue;
                }

                if (task.StartSimTime < conveyorStart)
                {
                    conveyorStart = task.StartSimTime;
                }
            }

            if (schedule != null && schedule.Length > 0 && schedule[0].DesiredEntrySimTime < conveyorStart)
            {
                conveyorStart = schedule[0].DesiredEntrySimTime;
            }

            if (conveyorStart < double.MaxValue && simTime >= conveyorStart - 1e-9)
            {
                return false;
            }

            return true;
        }

        /// <summary>出库：货物已抵达出库口路段终点，正在等待发运服务完成。</summary>
        public static bool IsWaitingAtOutfeed(
            ConveyorSegmentScheduleEntry[] schedule,
            IReadOnlyList<SimSubTask> jobTasks,
            double simTime,
            out int outfeedNodeIndex)
        {
            outfeedNodeIndex = -1;
            if (schedule == null
                || schedule.Length == 0
                || !SimSubTaskQuery.IsInOutboundConveyorVisualWindow(jobTasks, simTime))
            {
                return false;
            }

            var scheduleEnd = schedule[^1].OccupancyEndSimTime;
            if (simTime < scheduleEnd - 1e-9)
            {
                return false;
            }

            outfeedNodeIndex = schedule[^1].ToNodeIndex;
            return outfeedNodeIndex >= 0;
        }
    }
}
