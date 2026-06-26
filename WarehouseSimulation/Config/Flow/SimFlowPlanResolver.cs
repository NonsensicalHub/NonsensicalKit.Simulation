using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>解析场景中的流程计划。</summary>
    public static class SimFlowPlanResolver
    {
        public static IReadOnlyList<SimFlowPlanEntry> Resolve(WarehouseSimScenario scenario)
        {
            if (scenario?.FlowPlan != null && scenario.FlowPlan.Length > 0)
            {
                return scenario.FlowPlan;
            }

            return SimFlowPlanDefaults.DefaultWhenEmpty;
        }

        public static int CountTotalQuantity(IReadOnlyList<SimFlowPlanEntry> plan)
        {
            var total = 0;
            for (var i = 0; i < plan.Count; i++)
            {
                total += Math.Max(0, plan[i].Quantity);
            }

            return total;
        }

        public static bool RequiresOutboundPorts(IReadOnlyList<SimFlowPlanEntry> plan)
        {
            for (var i = 0; i < plan.Count; i++)
            {
                if (plan[i].Direction == SimFlowDirection.Outbound && plan[i].Quantity > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool RequiresInboundPorts(IReadOnlyList<SimFlowPlanEntry> plan)
        {
            for (var i = 0; i < plan.Count; i++)
            {
                if (plan[i].Direction == SimFlowDirection.Inbound && plan[i].Quantity > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>报告标题用：入库 / 出库 / 入出库。</summary>
        public static string FormatFlowKindLabel(IReadOnlyList<SimFlowPlanEntry> plan)
        {
            var inbound = RequiresInboundPorts(plan);
            var outbound = RequiresOutboundPorts(plan);
            if (inbound && outbound)
            {
                return "入出库";
            }

            return outbound ? "出库" : "入库";
        }
    }
}
