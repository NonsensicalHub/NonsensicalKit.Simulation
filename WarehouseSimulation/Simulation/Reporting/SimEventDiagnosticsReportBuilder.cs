using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>将 <see cref="SimEventDiagnostics"/> 写入调试信息 Markdown 报告。</summary>
    public static class SimEventDiagnosticsReportBuilder
    {
        public static void AppendMarkdown(StringBuilder sb, SimEventDiagnostics diag)
        {
            if (diag == null)
            {
                return;
            }

            SimReportMarkdown.AppendHeading(sb, 2, "事件诊断");

            var summary = new List<(string Label, string Value)>
            {
                ("已处理事件数", $"{diag.DispatchedEventCount} / {diag.MaxSimEvents}"),
                ("队列剩余", diag.QueuePendingCount.ToString()),
                ("仿真时刻", $"{diag.SimTimeSeconds:F1} s"),
                (
                    "任务进度",
                    $"完成 {diag.CompletedJobCount}，失败 {diag.FailedJobCount}，目标 {diag.TargetJobCount}"),
            };

            if (diag.TotalSlotCount > 0)
            {
                summary.Add(("货位占用", $"{diag.OccupiedSlotCount} / {diag.TotalSlotCount}"));
            }

            SimReportMarkdown.AppendKvTable(sb, summary);
            AppendEventTypeTable(sb, diag);
            AppendJobStateTable(sb, diag);
            AppendNamedCountTable(sb, "入库口预定", diag.InfeedReservations);
            AppendNamedCountTable(sb, "出库口预定", diag.OutfeedReservations);
            AppendNamedCountTable(sb, "堆垛机交互点预定", diag.PickupReservations);
        }

        private static void AppendEventTypeTable(StringBuilder sb, SimEventDiagnostics diag)
        {
            if (diag.EventTypeStats == null || diag.EventTypeStats.Count == 0)
            {
                return;
            }

            SimReportMarkdown.AppendHeading(sb, 3, "事件类型统计（按数量降序）");
            var headers = new[] { "类型", "数量", "占比" };
            var rows = new List<string[]>(diag.EventTypeStats.Count);
            for (var i = 0; i < diag.EventTypeStats.Count; i++)
            {
                var entry = diag.EventTypeStats[i];
                rows.Add(new[]
                {
                    entry.Type.ToString(),
                    entry.Count.ToString(),
                    $"{entry.Percent:F1}%",
                });
            }

            SimReportMarkdown.AppendTable(sb, headers, rows);
        }

        private static void AppendJobStateTable(StringBuilder sb, SimEventDiagnostics diag)
        {
            if (diag.JobStateStats == null || diag.JobStateStats.Count == 0)
            {
                return;
            }

            SimReportMarkdown.AppendHeading(sb, 3, "任务状态分布");
            var headers = new[] { "状态", "数量" };
            var rows = new List<string[]>(diag.JobStateStats.Count);
            for (var i = 0; i < diag.JobStateStats.Count; i++)
            {
                var entry = diag.JobStateStats[i];
                rows.Add(new[] { entry.State.ToString(), entry.Count.ToString() });
            }

            SimReportMarkdown.AppendTable(sb, headers, rows);
        }

        private static void AppendNamedCountTable(
            StringBuilder sb,
            string title,
            List<SimNamedCount> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            SimReportMarkdown.AppendHeading(sb, 3, title);
            var headers = new[] { "名称", "预定数" };
            var tableRows = new List<string[]>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                tableRows.Add(new[] { rows[i].Name, rows[i].Count.ToString() });
            }

            SimReportMarkdown.AppendTable(sb, headers, tableRows);
        }
    }
}
