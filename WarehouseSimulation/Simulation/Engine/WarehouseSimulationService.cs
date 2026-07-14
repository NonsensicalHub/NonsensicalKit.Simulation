using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 仿真运行门面（Facade），供 <c>WarehouseSimRunner</c>、编辑器菜单等 Unity 侧代码调用。
    /// </summary>
    /// <remarks>
    /// 业务逻辑全部在 <see cref="StackerWarehouseSimulator"/> 及其 partial 中实现；
    /// 本类不持有仿真状态，仅负责：构造仿真器 → 调用 <see cref="StackerWarehouseSimulator.Run"/> →
    /// 可选的后台线程日志隔离 → 将 <see cref="SimRunResult"/> 摘要输出到 Unity Console。
    /// </remarks>
    public static class WarehouseSimulationService
    {
        /// <summary>主线程同步运行仓库仿真。</summary>
        public static (StackerWarehouseSimulator Simulator, SimRunResult Result) Run(
            WarehouseSimScenario scenario,
            ISlotAllocator slotAllocator,
            SimRunOptions? runOptions = null)
        {
            var simulator = new StackerWarehouseSimulator(slotAllocator);
            return (simulator, simulator.Run(scenario, runOptions));
        }

        /// <summary>供 <see cref="System.Threading.Tasks.Task.Run"/> 调用；禁止在工作线程使用 Unity API 打日志。</summary>
        public static (StackerWarehouseSimulator Simulator, SimRunResult Result) RunOnBackgroundThread(
            WarehouseSimScenario scenario,
            ISlotAllocator slotAllocator,
            SimRunOptions? runOptions = null)
        {
            WarehouseSimLog.EnterBackgroundThread();
            try
            {
                return Run(scenario, slotAllocator, runOptions);
            }
            finally
            {
                WarehouseSimLog.ExitBackgroundThread();
            }
        }

        public static void LogSummary(SimRunResult result)
        {
            if (result == null)
            {
                return;
            }

            var msg =
                $"[WarehouseSimulation] {(result.Success ? "成功" : "失败")}: {result.Message}\n" +
                $"  完成 {result.CompletedJobCount}/{result.TargetJobCount}，总仿真时长 {result.TotalSimSeconds:F1}s ({SimReportFormatting.FormatDuration(result.TotalSimSeconds)})\n" +
                $"  吞吐 {result.ThroughputPerHour:F1} 箱/小时\n" +
                $"  货位：可存储 {result.StorageSlotCount}，堆垛机可达可分配 {result.AllocatableStorageSlotCount}，初始占用 {result.InitialOccupiedStorageSlotCount}\n" +
                $"  流程计划：{result.FlowPlanSummaryLabel ?? "—"}\n" +
                $"  堆垛机策略：{result.StackerPlacementStrategyLabel ?? "—"}\n" +
                $"  入库口策略：{result.InfeedPortSelectionStrategyLabel ?? "—"}\n" +
                $"  输送策略：{result.ConveyorRoutingStrategyLabel ?? "—"}\n" +
                $"  单箱耗时 — 最小 {result.DurationMinSeconds:F1}s，均值 {result.DurationMeanSeconds:F1}s，P50 {result.DurationP50Seconds:F1}s，P95 {result.DurationP95Seconds:F1}s，最大 {result.DurationMaxSeconds:F1}s\n" +
                $"  等待 — 均值 {result.WaitTimeMeanSeconds:F1}s，最大 {result.WaitTimeMaxSeconds:F1}s";

            if (result.FailureCounts != null && result.FailureCounts.Count > 0)
            {
                msg += "\n  失败统计：" + SimJobFailureReasonLabels.FormatSummary(result.FailureCounts);
            }

            var profileSummary = SimWallClockProfileSnapshot.FormatSummary(result.WallClockProfile);
            if (!string.IsNullOrEmpty(profileSummary))
            {
                msg += "\n  墙钟性能剖析（仿真引擎 CPU）：\n" + profileSummary;
            }

            if (result.Success)
            {
                Debug.Log(msg);
            }
            else
            {
                Debug.LogWarning(msg);
            }

            if (result.OccupancyConflicts != null && result.OccupancyConflicts.Count > 0)
            {
                var head = SimOccupancyConflictChecker.FormatSummary(
                    result.OccupancyConflicts.Count > 20
                        ? result.OccupancyConflicts.GetRange(0, 20)
                        : result.OccupancyConflicts);
                Debug.LogWarning(
                    $"[WarehouseSimulation] 节点占用自检 {result.OccupancyConflicts.Count} 处冲突。" +
                    (result.OccupancyConflicts.Count > 20
                        ? " 以下仅前 20 条，完整明细请导出调试 Markdown 报告。"
                        : string.Empty) +
                    $"\n{head}");
            }

            if (result.SubTaskTimelineIssues != null && result.SubTaskTimelineIssues.Count > 0)
            {
                var head = SimSubTaskTimelineChecker.FormatSummary(
                    result.SubTaskTimelineIssues.Count > 20
                        ? result.SubTaskTimelineIssues.GetRange(0, 20)
                        : result.SubTaskTimelineIssues);
                Debug.LogWarning(
                    $"[WarehouseSimulation] 子任务时间轴自检 {result.SubTaskTimelineIssues.Count} 处问题。" +
                    (result.SubTaskTimelineIssues.Count > 20
                        ? " 以下仅前 20 条，完整明细请导出调试 Markdown 报告。"
                        : string.Empty) +
                    $"\n{head}");
            }

            if (!string.IsNullOrEmpty(result.DebugReportPath)
                && ((result.OccupancyConflicts != null && result.OccupancyConflicts.Count > 0)
                    || (result.SubTaskTimelineIssues != null && result.SubTaskTimelineIssues.Count > 0)))
            {
                Debug.LogWarning($"[WarehouseSimulation] 调试报告：{result.DebugReportPath}");
            }
        }
    }
}
