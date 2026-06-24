using System;
using System.Collections.Generic;
using System.Linq;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>一次仿真运行的汇总结果：成功标志、吞吐与各分位耗时。</summary>
    [Serializable]
    public class SimRunResult
    {
        public bool Success;
        public string Message;
        public int TargetJobCount;

        /// <summary>成功完成并记入 <see cref="Completions"/> 的任务数。</summary>
        public int CompletedJobCount;
        public double TotalSimSeconds;
        public double ThroughputPerHour;

        public double DurationMinSeconds;
        public double DurationMaxSeconds;
        public double DurationMeanSeconds;
        public double DurationP50Seconds;
        public double DurationP95Seconds;

        public double WaitTimeMeanSeconds;
        public double WaitTimeMaxSeconds;

        public List<JobCompletionRecord> Completions = new();
        public Dictionary<string, double> ResourceWaitTotals = new();

        /// <summary>堆垛机、入库口、出库口等设备利用率（由子任务时间轴汇总）。</summary>
        public List<SimResourceUtilizationStat> ResourceUtilizations = new();

        /// <summary>仿真后自检发现的独占资源占用冲突（不同任务时间重叠）。</summary>
        public List<SimOccupancyConflictRecord> OccupancyConflicts = new();

        /// <summary>仿真后自检发现的单任务子任务时间轴重叠或空白。</summary>
        public List<SimSubTaskTimelineIssueRecord> SubTaskTimelineIssues = new();

        /// <summary>占用冲突详细报告（纯文本，含分类统计与全部明细；写入调试 Markdown 报告）。</summary>
        public string OccupancyConflictReportText;

        /// <summary>调试信息 Markdown 报告路径（若已导出）。</summary>
        public string DebugReportPath;

        /// <summary>本次仿真使用的堆垛机放置策略（人类可读）。</summary>
        public string StackerPlacementStrategyLabel;

        /// <summary>本次仿真使用的流程计划摘要（人类可读）。</summary>
        public string FlowPlanSummaryLabel;

        /// <summary>本次仿真使用的入库口选择策略（人类可读）。</summary>
        public string InfeedPortSelectionStrategyLabel;

        /// <summary>本次仿真使用的输送路径策略（人类可读）。</summary>
        public string ConveyorRoutingStrategyLabel;

        /// <summary>可存储货位总数（含排除区后）。</summary>
        public int StorageSlotCount;

        /// <summary>任一堆垛机伸叉范围内可分配的货位总数。</summary>
        public int AllocatableStorageSlotCount;

        /// <summary>仿真开始时已占用的可存储货位数。</summary>
        public int InitialOccupiedStorageSlotCount;

        /// <summary>各失败原因计数（键为 <see cref="SimJobFailureReason"/>）。</summary>
        public Dictionary<SimJobFailureReason, int> FailureCounts = new();

        /// <summary>墙钟性能剖析（需 Runner 开启 <c>CollectWallClockProfile</c>）。</summary>
        public SimWallClockProfileSnapshot WallClockProfile;

        /// <summary>根据 <see cref="Completions"/> 计算耗时分位数、等待均值与每小时吞吐。</summary>
        public void ComputeStatistics()
        {
            if (Completions == null || Completions.Count == 0)
            {
                return;
            }

            var durations = Completions.Select(c => c.Duration).OrderBy(x => x).ToList();
            DurationMinSeconds = durations[0];
            DurationMaxSeconds = durations[^1];
            DurationMeanSeconds = durations.Average();
            DurationP50Seconds = Percentile(durations, 0.5);
            DurationP95Seconds = Percentile(durations, 0.95);

            WaitTimeMeanSeconds = Completions.Average(c => c.WaitTime);
            WaitTimeMaxSeconds = Completions.Max(c => c.WaitTime);

            if (TotalSimSeconds > 1e-6)
            {
                ThroughputPerHour = CompletedJobCount / TotalSimSeconds * 3600.0;
            }
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0)
            {
                return 0;
            }

            if (sorted.Count == 1)
            {
                return sorted[0];
            }

            var idx = (sorted.Count - 1) * p;
            var lo = (int)Math.Floor(idx);
            var hi = (int)Math.Ceiling(idx);
            if (lo == hi)
            {
                return sorted[lo];
            }

            var t = idx - lo;
            return sorted[lo] * (1 - t) + sorted[hi] * t;
        }
    }
}
