using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>节点占用自检在用户仿真 HTML 报告概要中的结论与错误摘要。</summary>
    public static class SimOccupancySelfCheckHtmlBuilder
    {
        private const int MaxErrorRows = 10;

        public static void AppendToSummary(StringBuilder sb, SimRunResult result)
        {
            var conflicts = result?.OccupancyConflicts;
            if (conflicts == null || conflicts.Count == 0)
            {
                SimReportHtml.AppendKvRow(sb, "独占资源占用校验", "通过");
                return;
            }

            SimReportHtml.AppendKvRow(sb, "独占资源占用校验", $"失败 — {conflicts.Count} 处冲突");
            var show = conflicts.Count < MaxErrorRows ? conflicts.Count : MaxErrorRows;
            for (var i = 0; i < show; i++)
            {
                SimReportHtml.AppendKvRow(sb, $"  冲突 {i + 1}", SimOccupancyConflictFormatting.FormatCompactLine(conflicts[i]));
            }

            if (conflicts.Count > MaxErrorRows)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "  …",
                    $"另有 {conflicts.Count - MaxErrorRows} 处（见调试 Markdown 报告）");
            }
        }
    }
}
