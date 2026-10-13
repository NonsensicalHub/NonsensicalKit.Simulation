using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>路段同时占用自检的 Markdown 与 HTML 概要。</summary>
    public static class SimSegmentCoOccupancyMarkdownBuilder
    {
        private const int MaxHtmlRows = 10;

        public static void AppendSection(StringBuilder sb, SimRunResult result)
        {
            SimReportMarkdown.AppendHeading(sb, 2, "路段同时占用自检");
            var overlaps = result?.SegmentCoOccupancies;
            if (overlaps == null || overlaps.Count == 0)
            {
                sb.AppendLine("通过：同向同时在段的货物数不超过路段容量，对向在段时间互不重叠。");
                sb.AppendLine();
                return;
            }

            SimSegmentCoOccupancyChecker.CountByDirection(overlaps, out var opposite, out var sameDirection);
            var summaryRows = new List<(string Label, string Value)>
            {
                ("结论", "失败"),
                ("重叠总数", overlaps.Count.ToString()),
                ("对向", opposite.ToString()),
                ("同向", sameDirection.ToString()),
            };
            SimReportMarkdown.AppendKvTable(sb, summaryRows);
            sb.AppendLine("同向在段时间：从开始驶入，到车头到达下游节点。人数可以达到路段容量。");
            sb.AppendLine("对向还要算上车体：停在下游节点期间，以及车头再开出一个车身、尾端离开之前，这段路仍被占着。");
            sb.AppendLine("两条路在同一路口连成直线时，车身靠近路口的时间也不能重叠。");
            sb.AppendLine("超过容量，或对向同时占着这段路，记为冲突。");
            sb.AppendLine();
            SimReportMarkdown.AppendHeading(sb, 3, "重叠明细");
            AppendTable(sb, overlaps);
        }

        public static void AppendHtmlSummary(StringBuilder sb, SimRunResult result)
        {
            var overlaps = result?.SegmentCoOccupancies;
            if (overlaps == null || overlaps.Count == 0)
            {
                SimReportHtml.AppendKvRow(sb, "路段同时占用校验", "通过");
                return;
            }

            SimSegmentCoOccupancyChecker.CountByDirection(overlaps, out var opposite, out var sameDirection);
            SimReportHtml.AppendKvRow(
                sb,
                "路段同时占用校验",
                $"失败 — {overlaps.Count} 处（对向 {opposite}，同向 {sameDirection}）");
            var show = overlaps.Count < MaxHtmlRows ? overlaps.Count : MaxHtmlRows;
            for (var i = 0; i < show; i++)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    $"  路段重叠 {i + 1}",
                    SimSegmentCoOccupancyChecker.FormatCompactLine(overlaps[i]));
            }

            if (overlaps.Count > MaxHtmlRows)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "  …",
                    $"另有 {overlaps.Count - MaxHtmlRows} 处（见调试 Markdown 报告）");
            }
        }

        private static void AppendTable(StringBuilder sb, IReadOnlyList<SimSegmentCoOccupancyRecord> overlaps)
        {
            var headers = new[]
            {
                "#", "关系", "路段", "容量",
                "任务 A", "A 方向", "A 区间",
                "任务 B", "B 方向", "B 区间",
                "重叠区间", "重叠(s)",
            };
            var rows = new List<string[]>(overlaps.Count);
            for (var i = 0; i < overlaps.Count; i++)
            {
                var overlap = overlaps[i];
                rows.Add(new[]
                {
                    (i + 1).ToString(),
                    overlap.OppositeDirection ? "对向" : "同向",
                    overlap.SegmentLabel,
                    overlap.Capacity.ToString(),
                    overlap.JobA.ToString(),
                    overlap.DirectionA,
                    $"[{overlap.JobAStart:F2}, {overlap.JobAEnd:F2}]",
                    overlap.JobB.ToString(),
                    overlap.DirectionB,
                    $"[{overlap.JobBStart:F2}, {overlap.JobBEnd:F2}]",
                    $"{overlap.OverlapStart:F2}~{overlap.OverlapEnd:F2}",
                    overlap.OverlapSeconds.ToString("F2"),
                });
            }

            SimReportMarkdown.AppendTable(sb, headers, rows);
        }
    }
}
