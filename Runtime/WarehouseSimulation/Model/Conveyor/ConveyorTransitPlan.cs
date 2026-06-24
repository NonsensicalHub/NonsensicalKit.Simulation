using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>
    /// 一次输送规划的结果快照：包含整条路径的段时序与关键完成时刻。
    /// 阶段 A 先承载现有调度结果，后续可扩展为“纯计划 + 事务提交”双阶段。
    /// </summary>
    [Serializable]
    public sealed class ConveyorTransitPlan
    {
        [Serializable]
        public struct SegmentMetricsEntry
        {
            public int FromNodeIndex;
            public int ToNodeIndex;
            public double DesiredEntrySimTime;
            public double EntrySimTime;
            public double ExitSimTime;
            public double OccupancyEndSimTime;
            public double EntryWaitSeconds;
            public string EntryWaitResourceId;
            public double DownstreamWaitSeconds;
            public string DownstreamWaitResourceId;
            public double ServiceSeconds;
        }

        public double StartSimTime;
        public double EndSimTime;
        public double InfeedPhysicalReleaseSimTime;

        /// <summary>路径上每段连边均成功预约时为 true；缺边时中止规划。</summary>
        public bool PathComplete = true;
        public List<ConveyorSegmentScheduleEntry> SegmentSchedule = new();
        public List<SegmentMetricsEntry> SegmentMetrics = new();
    }
}

