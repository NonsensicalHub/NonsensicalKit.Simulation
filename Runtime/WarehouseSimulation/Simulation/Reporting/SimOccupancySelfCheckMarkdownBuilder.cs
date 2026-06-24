using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>节点占用自检 Markdown 章节（用于调试信息报告）。</summary>
    public static class SimOccupancySelfCheckMarkdownBuilder
    {
        public static void AppendSection(StringBuilder sb, SimRunResult result)
        {
            SimReportMarkdown.AppendHeading(sb, 2, "节点占用自检");

            var conflicts = result?.OccupancyConflicts;
            if (conflicts == null || conflicts.Count == 0)
            {
                sb.AppendLine("通过：未发现跨任务独占资源时间重叠。");
                sb.AppendLine();
                return;
            }

            var summaryRows = new List<(string Label, string Value)>
            {
                ("结论", "失败"),
                ("冲突总数", conflicts.Count.ToString()),
            };
            AppendCategorySummaryRows(summaryRows, conflicts);
            SimReportMarkdown.AppendKvTable(sb, summaryRows);
            SimReportMarkdown.AppendHeading(sb, 3, "冲突明细");
            AppendConflictTable(sb, conflicts);
        }

        private static void AppendCategorySummaryRows(
            List<(string Label, string Value)> rows,
            IReadOnlyList<SimOccupancyConflictRecord> conflicts)
        {
            var categoryCounts = new Dictionary<SimOccupancyResourceCategory, int>();
            for (var i = 0; i < conflicts.Count; i++)
            {
                var cat = conflicts[i].Category;
                categoryCounts.TryGetValue(cat, out var n);
                categoryCounts[cat] = n + 1;
            }

            foreach (SimOccupancyResourceCategory cat in Enum.GetValues(typeof(SimOccupancyResourceCategory)))
            {
                if (cat == SimOccupancyResourceCategory.Unknown || !categoryCounts.TryGetValue(cat, out var n) || n == 0)
                {
                    continue;
                }

                rows.Add(($"{SimOccupancyConflictReportBuilder.FormatCategoryLabel(cat)}冲突", n.ToString()));
            }
        }

        private static void AppendConflictTable(
            StringBuilder sb,
            IReadOnlyList<SimOccupancyConflictRecord> conflicts)
        {
            var headers = new[]
            {
                "#", "类型", "资源", "资源键",
                "任务 A", "A 区间", "A 子任务",
                "任务 B", "B 区间", "B 子任务",
                "重叠区间", "重叠(s)",
            };

            var rows = new List<string[]>(conflicts.Count);
            for (var i = 0; i < conflicts.Count; i++)
            {
                var c = conflicts[i];
                rows.Add(new[]
                {
                    (i + 1).ToString(),
                    SimOccupancyConflictReportBuilder.FormatCategoryLabel(c.Category),
                    c.ResourceLabel,
                    c.ResourceKey,
                    c.JobA.ToString(),
                    $"[{c.JobAStart:F2}, {c.JobAEnd:F2}]",
                    SimSubTaskQuery.GetKindLabel(c.KindA),
                    c.JobB.ToString(),
                    $"[{c.JobBStart:F2}, {c.JobBEnd:F2}]",
                    SimSubTaskQuery.GetKindLabel(c.KindB),
                    $"{c.OverlapStart:F2}~{c.OverlapEnd:F2}",
                    c.OverlapSeconds.ToString("F2"),
                });
            }

            SimReportMarkdown.AppendTable(sb, headers, rows);
        }
    }
}
