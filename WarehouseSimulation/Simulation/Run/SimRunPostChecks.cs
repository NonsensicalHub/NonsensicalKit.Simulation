using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>循环结束后的时间轴整理与 Reporting 自检。不参与事件分发。</summary>
    internal static class SimRunPostChecks
    {
        private const int SubTaskLimit = 15000;

        public static void Run(SimRunContext ctx, SimRunResult result)
        {
            if (ctx.Timeline.SubTasks.Count > SubTaskLimit)
            {
                WarehouseSimLog.Warn(
                    $"子任务 {ctx.Timeline.SubTasks.Count} 条，已超过自检上限 {SubTaskLimit}，跳过仿真后自检。");
                return;
            }

            ctx.Timeline.NormalizeConveyorSubTaskTimeline();
            RunOccupancySelfCheck(ctx, result);
            RunSegmentCoOccupancySelfCheck(ctx, result);
            RunCargoSpacingSelfCheck(ctx, result);
            RunSubTaskTimelineSelfCheck(ctx, result);
        }

        public static Dictionary<string, double> BuildWaitTotals(IReadOnlyList<JobCompletionRecord> completions)
        {
            var dict = new Dictionary<string, double>();
            foreach (var completion in completions)
            {
                if (string.IsNullOrEmpty(completion.BottleneckResource))
                {
                    continue;
                }

                if (!dict.ContainsKey(completion.BottleneckResource))
                {
                    dict[completion.BottleneckResource] = 0;
                }

                dict[completion.BottleneckResource] += completion.WaitTime;
            }

            return dict;
        }

        private static void RunOccupancySelfCheck(SimRunContext ctx, SimRunResult result)
        {
            if (result == null || ctx.Topology == null || ctx.Bindings?.ConveyorMap == null)
            {
                return;
            }

            SimOccupancyConflictChecker.CheckResult check;
            using (ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.PostOccupancyCheck))
            {
                check = SimOccupancyConflictChecker.Run(
                    ctx.Timeline.SubTasks,
                    ctx.Bindings.ConveyorMap,
                    ctx.Topology,
                    result.TotalSimSeconds,
                    ctx.Bindings);
            }

            result.OccupancyConflicts = check.Conflicts;
            result.OccupancyConflictReportText = check.Conflicts.Count > 0 ? check.FullReportText : null;
            if (result.OccupancyConflicts == null || result.OccupancyConflicts.Count == 0)
            {
                return;
            }

            WarehouseSimLog.Error(
                $"仿真后自检发现 {result.OccupancyConflicts.Count} 处节点占用冲突（导出调试 Markdown 报告查看明细）。");
            MarkFailed(result, $"自检：节点占用冲突 {result.OccupancyConflicts.Count} 处。");
        }

        private static void RunSegmentCoOccupancySelfCheck(SimRunContext ctx, SimRunResult result)
        {
            if (result == null || ctx.Topology == null || ctx.Bindings?.ConveyorMap == null)
            {
                return;
            }

            var overlaps = SimSegmentCoOccupancyChecker.FindOverlaps(
                ctx.Timeline.SubTasks,
                ctx.Bindings.ConveyorMap,
                ctx.Topology);
            result.SegmentCoOccupancies = overlaps;
            if (overlaps.Count == 0)
            {
                return;
            }

            SimSegmentCoOccupancyChecker.CountByDirection(overlaps, out var opposite, out var sameDirection);
            WarehouseSimLog.Error(
                $"仿真后自检发现 {overlaps.Count} 处路段同时占用（对向 {opposite}，同向 {sameDirection}；导出调试 Markdown 报告查看明细）。");
            MarkFailed(
                result,
                $"自检：路段同时占用 {overlaps.Count} 处（对向 {opposite}，同向 {sameDirection}）。");
        }

        private static void RunCargoSpacingSelfCheck(SimRunContext ctx, SimRunResult result)
        {
            if (result == null || ctx.Topology == null || ctx.Bindings?.ConveyorMap == null)
            {
                return;
            }

            var violations = SimCargoSpacingChecker.FindViolations(
                ctx.Jobs.Values,
                ctx.Bindings.ConveyorMap,
                ctx.Topology);
            result.CargoSpacingViolations = violations;
            if (violations.Count == 0)
            {
                return;
            }

            WarehouseSimLog.Error(
                $"仿真后自检发现 {violations.Count} 段货物间距不足（中心距 < 单箱长度，回放会重叠；导出调试 Markdown 报告查看明细）。");
            MarkFailed(result, $"自检：货物间距不足 {violations.Count} 段。");
        }

        private static void RunSubTaskTimelineSelfCheck(SimRunContext ctx, SimRunResult result)
        {
            if (result == null)
            {
                return;
            }

            SimSubTaskTimelineChecker.CheckResult check;
            using (ctx.WallClockProfiler.Begin(SimWallClockProfilePhases.PostTimelineCheck))
            {
                check = SimSubTaskTimelineChecker.Run(ctx.Timeline.SubTasks);
            }

            result.SubTaskTimelineIssues = check.Issues;
            if (result.SubTaskTimelineIssues == null || result.SubTaskTimelineIssues.Count == 0)
            {
                return;
            }

            SimSubTaskTimelineChecker.CountIssuesByKind(
                result.SubTaskTimelineIssues,
                out var overlap,
                out var gap);
            WarehouseSimLog.Error(
                $"仿真后自检发现 {result.SubTaskTimelineIssues.Count} 处子任务时间轴问题（重叠 {overlap}，空白 {gap}；导出调试 Markdown 报告查看明细）。");
            MarkFailed(
                result,
                $"自检：子任务时间轴问题 {result.SubTaskTimelineIssues.Count} 处（重叠 {overlap}，空白 {gap}）。");
        }

        private static void MarkFailed(SimRunResult result, string fragment)
        {
            result.Success = false;
            result.Message = string.IsNullOrEmpty(result.Message)
                ? fragment
                : $"{result.Message} {fragment}";
        }
    }
}
