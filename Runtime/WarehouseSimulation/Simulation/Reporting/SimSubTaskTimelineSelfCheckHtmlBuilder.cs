using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>子任务时间轴自检在用户仿真 HTML 报告概要中的结论与错误摘要。</summary>
    public static class SimSubTaskTimelineSelfCheckHtmlBuilder
    {
        private const int MaxErrorRows = 10;

        public static void AppendToSummary(StringBuilder sb, SimRunResult result)
        {
            var issues = result?.SubTaskTimelineIssues;
            if (issues == null || issues.Count == 0)
            {
                SimReportHtml.AppendKvRow(sb, "子任务时间轴校验", "通过");
                return;
            }

            SimSubTaskTimelineChecker.CountIssuesByKind(issues, out var overlap, out var gap);

            SimReportHtml.AppendKvRow(
                sb,
                "子任务时间轴校验",
                $"失败 — {issues.Count} 处（重叠 {overlap}，空白 {gap}）");

            var show = issues.Count < MaxErrorRows ? issues.Count : MaxErrorRows;
            for (var i = 0; i < show; i++)
            {
                SimReportHtml.AppendKvRow(sb, $"  问题 {i + 1}", FormatIssue(issues[i]));
            }

            if (issues.Count > MaxErrorRows)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "  …",
                    $"另有 {issues.Count - MaxErrorRows} 处（见调试 Markdown 报告）");
            }
        }

        private static string FormatIssue(in SimSubTaskTimelineIssueRecord issue)
        {
            if (issue.Kind == SimSubTaskTimelineIssueKind.Overlap)
            {
                return
                    $"Job {issue.JobId} 重叠 {issue.IssueStart:F2}~{issue.IssueEnd:F2}s | " +
                    $"#{issue.SubTaskIdA} {SimSubTaskQuery.GetKindLabel(issue.KindA)} " +
                    $"vs #{issue.SubTaskIdB} {SimSubTaskQuery.GetKindLabel(issue.KindB)}";
            }

            return
                $"Job {issue.JobId} 空白 {issue.IssueStart:F2}~{issue.IssueEnd:F2}s ({issue.IssueSeconds:F2}s) | " +
                $"#{issue.SubTaskIdA} {SimSubTaskQuery.GetKindLabel(issue.KindA)} " +
                $"→ #{issue.SubTaskIdB} {SimSubTaskQuery.GetKindLabel(issue.KindB)}";
        }
    }
}
