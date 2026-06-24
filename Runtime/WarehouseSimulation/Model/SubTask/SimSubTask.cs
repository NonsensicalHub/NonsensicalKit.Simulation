using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>入出库流程中的原子子任务类型（均含明确的开始/结束仿真时刻）。</summary>
    public enum SimSubTaskKind
    {
        /// <summary>入库口放货并服务（放上入库口、扫描建单等，时长含口上排队等待）。</summary>
        InfeedPlace,
        /// <summary>离开入库口，移动到首段入口停留点（ZPA hop）。</summary>
        InfeedMove,
        /// <summary>离开堆垛机交互点，移动到首段入口停留点（ZPA hop）。</summary>
        OutboundMove,
        /// <summary>输送路段行驶（节点 A → B，无停留点细分时的整段）。</summary>
        SegmentTransit,
        /// <summary>停留点之间的输送移动（ZPA 单步 hop）。</summary>
        SegmentHopMove,
        /// <summary>在路段停留点上的等待（ZPA 积放阻塞）。</summary>
        SegmentStopDwell,
        /// <summary>输送路段排队等待驶入。</summary>
        SegmentQueue,
        /// <summary>从路段 S0 驶入路口中心。</summary>
        JunctionEnter,
        /// <summary>在路口中心等待下一段入口停留点空闲。</summary>
        JunctionWait,
        /// <summary>从路口中心驶出至下一段入口停留点。</summary>
        JunctionExit,
        /// <summary>等待堆垛机资源。</summary>
        StackerWait,
        /// <summary>堆垛机从当前位置驶向取货点（入库）或源货位（出库）。</summary>
        StackerApproach,
        /// <summary>堆垛机取货（叉入）。</summary>
        StackerPick,
        /// <summary>堆垛机移动（轨道 + 升降）。</summary>
        StackerMove,
        /// <summary>堆垛机放货（叉出）。</summary>
        StackerPlace,
        /// <summary>出库口发运服务（排队 + 扫描出库等）。</summary>
        OutfeedService,
        /// <summary>任务完成。</summary>
        Completed,
    }

    /// <summary>
    /// 入库任务的一条子任务记录，带开始/结束仿真时刻，支持从任意时刻求值回放状态。
    /// </summary>
    [Serializable]
    public struct SimSubTask
    {
        private const double TimeEpsilon = 1e-6;
        public int SubTaskId;
        public int JobId;
        public SimSubTaskKind Kind;
        public double StartSimTime;
        public double EndSimTime;

        public int StackerId;
        public GridIndex Slot;
        public int InfeedPortIndex;
        public int OutfeedPortIndex;
        public int PickupPointIndex;
        public int FromNodeIndex;
        public int ToNodeIndex;
        public int SegmentSlotIndex;

        /// <summary>堆垛机运动起点（驶向/取货/移动/放货）；Column 为货叉列，货叉收回时为轨道列。</summary>
        public GridIndex StackerFromSlot;

        /// <summary>堆垛机运动终点（驶向作业点/取货/移动/放货）。</summary>
        public GridIndex StackerToSlot;

        /// <summary>本阶段堆垛机轨道列（关节标定用）。</summary>
        public int StackerRailColumn;

        /// <summary>是否已写入 <see cref="StackerFromSlot"/> / <see cref="StackerToSlot"/>。</summary>
        public bool HasStackerPose;
        public int[] PathNodeIndices;

        /// <summary>输送路段预约表（在首次路径相关子任务上附带）。</summary>
        public ConveyorSegmentScheduleEntry[] SegmentSchedule;

        // 统一按半开区间 [start, end) 判定活动态，避免边界时刻被前后两个子任务同时命中。
        public bool ContainsTime(double simTime)
        {
            if (EndSimTime - StartSimTime <= TimeEpsilon)
            {
                return Math.Abs(simTime - StartSimTime) <= TimeEpsilon;
            }

            return simTime >= StartSimTime - TimeEpsilon && simTime < EndSimTime - TimeEpsilon;
        }

        public float NormalizedProgress(double simTime)
        {
            var span = EndSimTime - StartSimTime;
            if (span <= TimeEpsilon)
            {
                return 1f;
            }

            return (float)Math.Clamp((simTime - StartSimTime) / span, 0, 1);
        }
    }

    /// <summary>子任务时间轴查询辅助。</summary>
    public static class SimSubTaskQuery
    {
        private const double TimeEpsilon = 1e-6;

        public static bool TryGetActive(
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            double simTime,
            out SimSubTask active)
        {
            active = default;
            var found = false;
            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.JobId != jobId || !task.ContainsTime(simTime))
                {
                    continue;
                }

                if (!found || task.StartSimTime >= active.StartSimTime)
                {
                    active = task;
                    found = true;
                }
            }

            return found;
        }

        public static bool TryGetActiveForStacker(
            IReadOnlyList<SimSubTask> subTasks,
            int stackerId,
            double simTime,
            out SimSubTask active)
        {
            active = default;
            var found = false;
            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.StackerId != stackerId || !IsStackerMotionKind(task.Kind))
                {
                    continue;
                }

                if (!task.ContainsTime(simTime))
                {
                    continue;
                }

                if (!found || task.StartSimTime >= active.StartSimTime)
                {
                    active = task;
                    found = true;
                }
            }

            return found;
        }

        public static bool HasStarted(IReadOnlyList<SimSubTask> subTasks, int jobId, double simTime)
        {
            for (var i = 0; i < subTasks.Count; i++)
            {
                if (subTasks[i].JobId == jobId && simTime >= subTasks[i].StartSimTime - TimeEpsilon)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetJobContext(
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            out int[] path,
            out ConveyorSegmentScheduleEntry[] schedule)
        {
            path = null;
            schedule = null;
            for (var i = 0; i < subTasks.Count; i++)
            {
                if (subTasks[i].JobId != jobId)
                {
                    continue;
                }

                if (subTasks[i].PathNodeIndices != null && subTasks[i].PathNodeIndices.Length > 0)
                {
                    path = subTasks[i].PathNodeIndices;
                }
            }

            if (TryGetBestSegmentSchedule(subTasks, jobId, out schedule))
            {
                return path != null || schedule != null;
            }

            return path != null;
        }

        /// <summary>
        /// 从子任务快照中选取最完整的路段预约表（按段数、末段占用结束时刻择优，与回放一致）。
        /// 勿用首次附带的 schedule：逐步 DES 记录时该副本常缺少后续路段与路口校正。
        /// </summary>
        public static bool TryGetBestSegmentSchedule(
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            out ConveyorSegmentScheduleEntry[] schedule)
        {
            schedule = null;
            if (subTasks == null || subTasks.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < subTasks.Count; i++)
            {
                var candidate = subTasks[i];
                if (candidate.JobId != jobId
                    || candidate.SegmentSchedule == null
                    || candidate.SegmentSchedule.Length == 0)
                {
                    continue;
                }

                if (schedule == null
                    || schedule.Length == 0
                    || candidate.SegmentSchedule.Length > schedule.Length
                    || candidate.SegmentSchedule[^1].OccupancyEndSimTime
                       > schedule[^1].OccupancyEndSimTime + TimeEpsilon)
                {
                    schedule = candidate.SegmentSchedule;
                }
            }

            return schedule != null && schedule.Length > 0;
        }

        public static double GetTimelineStart(IReadOnlyList<SimSubTask> subTasks)
        {
            if (subTasks == null || subTasks.Count == 0)
            {
                return 0d;
            }

            var start = subTasks[0].StartSimTime;
            for (var i = 1; i < subTasks.Count; i++)
            {
                if (subTasks[i].StartSimTime < start)
                {
                    start = subTasks[i].StartSimTime;
                }
            }

            return start;
        }

        public static double GetTimelineEnd(IReadOnlyList<SimSubTask> subTasks)
        {
            var end = 0d;
            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.EndSimTime > end)
                {
                    end = task.EndSimTime;
                }

                var schedule = task.SegmentSchedule;
                if (schedule == null || schedule.Length == 0)
                {
                    continue;
                }

                var occEnd = schedule[^1].OccupancyEndSimTime;
                if (occEnd > end)
                {
                    end = occEnd;
                }
            }

            return end;
        }

        public static string GetKindLabel(in SimSubTask task)
        {
            if (SimSubTaskKinds.IsPickupArrivalHop(task))
            {
                return "驶入堆垛机交互点";
            }

            if (SimSubTaskKinds.IsOutboundPickupDepartMove(task))
            {
                return "交互点驶离";
            }

            if (SimSubTaskKinds.IsOutboundPickupQueue(task))
            {
                return "交互点等待出库口";
            }

            return GetKindLabel(task.Kind);
        }

        public static string GetKindLabel(SimSubTaskKind kind) =>
            kind switch
            {
                SimSubTaskKind.InfeedPlace => "入库放货",
                SimSubTaskKind.InfeedMove => "入库口移动",
                SimSubTaskKind.OutboundMove => "交互点驶离",
                SimSubTaskKind.SegmentTransit => "路段行驶",
                SimSubTaskKind.SegmentHopMove => "停留点间移动",
                SimSubTaskKind.SegmentStopDwell => "停留点等待",
                SimSubTaskKind.SegmentQueue => "路段排队",
                SimSubTaskKind.JunctionEnter => "驶入路口",
                SimSubTaskKind.JunctionWait => "路口等待",
                SimSubTaskKind.JunctionExit => "驶出路口",
                SimSubTaskKind.StackerWait => "等待堆垛机",
                SimSubTaskKind.StackerApproach => "堆垛机驶向作业点",
                SimSubTaskKind.StackerPick => "堆垛机取货",
                SimSubTaskKind.StackerMove => "堆垛机移动",
                SimSubTaskKind.StackerPlace => "堆垛机放货",
                SimSubTaskKind.OutfeedService => "出库发运",
                SimSubTaskKind.Completed => "已完成",
                _ => kind.ToString(),
            };

        /// <summary>单任务子任务列表是否属于出库（无入库放货、且有堆垛机作业）。</summary>
        public static bool IsOutboundJob(IReadOnlyList<SimSubTask> jobTasks)
        {
            if (jobTasks == null || jobTasks.Count == 0)
            {
                return false;
            }

            var hasInfeedPlace = false;
            var hasStackerWork = false;
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.Kind == SimSubTaskKind.InfeedPlace)
                {
                    hasInfeedPlace = true;
                }

                if (task.PickupPointIndex >= 0
                    && task.Kind is SimSubTaskKind.StackerPick
                        or SimSubTaskKind.StackerMove
                        or SimSubTaskKind.StackerPlace)
                {
                    hasStackerWork = true;
                }
            }

            return hasStackerWork && !hasInfeedPlace;
        }

        /// <summary>
        /// 出库任务在堆垛机放货完成之后、任务 Completed 之前：输送线回放应接管料箱可视，堆垛机回放不得回收。
        /// </summary>
        public static bool IsInOutboundConveyorVisualWindow(
            IReadOnlyList<SimSubTask> jobTasks,
            double simTime)
        {
            if (!IsOutboundJob(jobTasks))
            {
                return false;
            }

            var placeEnd = 0d;
            var hasPlace = false;
            var completedStart = double.MaxValue;
            var hasCompleted = false;

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                if (task.Kind == SimSubTaskKind.StackerPlace && task.EndSimTime > placeEnd)
                {
                    placeEnd = task.EndSimTime;
                    hasPlace = true;
                }

                if (task.Kind == SimSubTaskKind.Completed)
                {
                    hasCompleted = true;
                    if (task.StartSimTime < completedStart)
                    {
                        completedStart = task.StartSimTime;
                    }
                }
            }

            if (!hasPlace || simTime < placeEnd - TimeEpsilon)
            {
                return false;
            }

            return !hasCompleted || simTime < completedStart - TimeEpsilon;
        }

        /// <summary>一次遍历构建各 Job 的最完整路段预约表，供占用自检等批量查询使用。</summary>
        public static Dictionary<int, ConveyorSegmentScheduleEntry[]> BuildBestScheduleByJob(
            IReadOnlyList<SimSubTask> subTasks)
        {
            var result = new Dictionary<int, ConveyorSegmentScheduleEntry[]>();
            if (subTasks == null || subTasks.Count == 0)
            {
                return result;
            }

            for (var i = 0; i < subTasks.Count; i++)
            {
                var candidate = subTasks[i];
                if (candidate.SegmentSchedule == null || candidate.SegmentSchedule.Length == 0)
                {
                    continue;
                }

                var jobId = candidate.JobId;
                if (!result.TryGetValue(jobId, out var existing)
                    || existing == null
                    || existing.Length == 0
                    || candidate.SegmentSchedule.Length > existing.Length
                    || candidate.SegmentSchedule[^1].OccupancyEndSimTime
                       > existing[^1].OccupancyEndSimTime + TimeEpsilon)
                {
                    result[jobId] = candidate.SegmentSchedule;
                }
            }

            return result;
        }

        /// <summary>从全局子任务时间轴筛出出库任务 JobId（与占用自检逻辑一致）。</summary>
        public static HashSet<int> BuildOutboundJobIds(IReadOnlyList<SimSubTask> subTasks)
        {
            var hasInfeedPlace = new HashSet<int>();
            var hasStackerWork = new HashSet<int>();
            if (subTasks == null)
            {
                return hasStackerWork;
            }

            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.Kind == SimSubTaskKind.InfeedPlace)
                {
                    hasInfeedPlace.Add(task.JobId);
                }

                if (task.PickupPointIndex >= 0
                    && task.Kind is SimSubTaskKind.StackerPick
                        or SimSubTaskKind.StackerMove
                        or SimSubTaskKind.StackerPlace)
                {
                    hasStackerWork.Add(task.JobId);
                }
            }

            hasStackerWork.ExceptWith(hasInfeedPlace);
            return hasStackerWork;
        }

        private static bool IsStackerMotionKind(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.StackerWait
                or SimSubTaskKind.StackerApproach
                or SimSubTaskKind.StackerPick
                or SimSubTaskKind.StackerMove
                or SimSubTaskKind.StackerPlace;
    }
}
