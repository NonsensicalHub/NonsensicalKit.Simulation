using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>仿真策略的人类可读标签（报告与日志）。</summary>
    public static class SimStrategyLabels
    {
        public static string FormatStackerPlacement(StackerSlotPlacementStrategy strategy) =>
            strategy switch
            {
                StackerSlotPlacementStrategy.FillColumnFirst => "按列放置（放满一列再放下一列）",
                _ => "就近放置（距取货点更近优先）",
            };

        public static string FormatFlowPlan(IReadOnlyList<SimFlowPlanEntry> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return "—";
            }

            if (plan.Count == 1)
            {
                return FormatFlowEntry(plan[0]);
            }

            var parts = new System.Text.StringBuilder();
            for (var i = 0; i < plan.Count; i++)
            {
                if (i > 0)
                {
                    parts.Append("；");
                }

                parts.Append(FormatFlowEntry(plan[i]));
            }

            return parts.ToString();
        }

        private static string FormatStaggeredSchedule(SimFlowPlanEntry entry)
        {
            var qtyMin = Math.Max(1, entry.RandomQuantityMin);
            var qtyMax = Math.Max(qtyMin, entry.RandomQuantityMax);
            var intervalMin = Math.Max(0.01f, entry.RandomIntervalMinSeconds);
            var intervalMax = Math.Max(intervalMin, entry.RandomIntervalMaxSeconds);

            var qtyPart = qtyMin == qtyMax
                ? $"每次 {qtyMin} 箱"
                : $"每次 {qtyMin}–{qtyMax} 箱";
            var intervalPart = Math.Abs(intervalMin - intervalMax) < 1e-6f
                ? $"每 {intervalMin:G} 秒"
                : $"间隔 {intervalMin:G}–{intervalMax:G} 秒";
            return $"{intervalPart}，{qtyPart}";
        }

        private static string FormatFlowEntry(SimFlowPlanEntry entry)
        {
            if (entry == null)
            {
                return "—";
            }

            var direction = entry.Direction == SimFlowDirection.Outbound ? "出库" : "入库";
            var schedule = entry.ScheduleMode == SimFlowScheduleMode.Staggered
                ? FormatStaggeredSchedule(entry)
                : "瞬间到达";

            var delay = entry.StartDelaySeconds > 1e-6f
                ? $"，延迟 {entry.StartDelaySeconds:G} 秒"
                : string.Empty;
            return $"{direction} {entry.Quantity} 箱（{schedule}{delay}）";
        }

        public static string FormatInfeedPortSelection(InfeedPortSelectionStrategy strategy) =>
            strategy switch
            {
                InfeedPortSelectionStrategy.ShortestQueue => "最短队列（预定最少的端口优先）",
                _ => "轮询（可用端口依次分配）",
            };

        public static string FormatConveyorRouting(ConveyorRoutingStrategy strategy) =>
            strategy switch
            {
                ConveyorRoutingStrategy.LeastCongestion => "最少拥堵（距离+路段占用加权）",
                ConveyorRoutingStrategy.PreferAssignedStacker => "优先已分配堆垛机交互点",
                _ => "最短输送时间",
            };

        public static string FormatInitialOccupancy(float ratio, bool random)
        {
            if (ratio <= 0f)
            {
                return "全空";
            }

            var label = ratio >= 1f ? "全满" : ratio.ToString("P0");
            return random ? $"{label}（位置随机）" : $"{label}（顺序填充）";
        }

        public static void ApplyToResult(SimRunResult result, WarehouseSimStrategyProfile strategy)
        {
            if (result == null)
            {
                return;
            }

            strategy ??= SimStrategyDefaults.Instance;
            result.StackerPlacementStrategyLabel = FormatStackerPlacement(strategy.StackerPlacementStrategy);
            result.InfeedPortSelectionStrategyLabel = FormatInfeedPortSelection(strategy.InfeedPortSelectionStrategy);
            result.ConveyorRoutingStrategyLabel = FormatConveyorRouting(strategy.ConveyorRoutingStrategy);
        }
    }
}
