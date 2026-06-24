using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>子任务时间轴自检 Markdown 章节。</summary>
    public static class SimSubTaskTimelineSelfCheckMarkdownBuilder
    {
        public static void AppendSection(StringBuilder sb, SimRunResult result)
        {
            SimReportMarkdown.AppendHeading(sb, 2, "子任务时间轴自检");

            var issues = result?.SubTaskTimelineIssues;
            if (issues == null || issues.Count == 0)
            {
                sb.AppendLine("通过：各任务子任务时间轴无重叠、无空白时段（已排除「已完成」瞬时标记）。");
                sb.AppendLine();
                return;
            }

            SimSubTaskTimelineChecker.CountIssuesByKind(issues, out var overlapCount, out var gapCount);

            SimReportMarkdown.AppendKvTable(sb, new[]
            {
                ("结论", "失败"),
                ("问题总数", issues.Count.ToString()),
                ("重叠", overlapCount.ToString()),
                ("空白", gapCount.ToString()),
            });

            sb.AppendLine(
                "说明：空白行中的「前/后」为**该空白时段**结束时刻之前、开始时刻之后最近的子任务（按仿真时刻），" +
                "不是子任务编号相邻；中间可能还有其它子任务，但不覆盖空白区间。");
            sb.AppendLine();

            SimReportMarkdown.AppendHeading(sb, 3, "问题明细");
            AppendIssueTable(sb, issues);
        }

        private static void AppendIssueTable(StringBuilder sb, IReadOnlyList<SimSubTaskTimelineIssueRecord> issues)
        {
            var headers = new[]
            {
                "#", "任务", "类型", "空白/重叠区间(s)", "时长(s)",
                "空白前子任务", "前任务区间", "空白后子任务", "后任务区间",
            };

            var rows = new List<string[]>(issues.Count);
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                rows.Add(new[]
                {
                    (i + 1).ToString(),
                    issue.JobId.ToString(),
                    SimSubTaskTimelineChecker.FormatKindLabel(issue.Kind),
                    $"{issue.IssueStart:F2}~{issue.IssueEnd:F2}",
                    issue.IssueSeconds.ToString("F2"),
                    FormatSubTaskLabel(issue.SubTaskIdA, issue.KindA),
                    FormatInterval(issue.SubTaskAStart, issue.SubTaskAEnd),
                    FormatSubTaskLabel(issue.SubTaskIdB, issue.KindB),
                    FormatInterval(issue.SubTaskBStart, issue.SubTaskBEnd),
                });
            }

            SimReportMarkdown.AppendTable(sb, headers, rows);
        }

        private static string FormatSubTaskLabel(int subTaskId, SimSubTaskKind kind)
        {
            if (subTaskId < 0)
            {
                return "—";
            }

            return $"#{subTaskId} {SimSubTaskQuery.GetKindLabel(kind)}";
        }

        private static string FormatInterval(double start, double end) =>
            $"[{start:F2}, {end:F2})";
    }
}
