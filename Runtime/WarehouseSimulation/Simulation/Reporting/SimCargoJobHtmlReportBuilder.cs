using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>将 <see cref="SimCargoJobValidationReport"/> 渲染为HTML 任务章节。</summary>
    internal static class SimCargoJobHtmlReportBuilder
    {
        public static void AppendJobSection(
            StringBuilder sb,
            SimCargoJobValidationReport report,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            var search = new StringBuilder(64);
            search.Append("任务 ");
            search.Append(report.JobId);
            search.Append(' ');
            search.Append(report.TargetSlot);
            search.Append(" 堆垛机");
            search.Append(SimReportFormatting.FormatStacker(report.StackerId));
            if (report.IsOutbound)
            {
                search.Append(" 出库口");
                search.Append(SimReportFormatting.FormatOutfeedPort(map, topology, report.OutfeedPortIndex));
            }
            else
            {
                search.Append(" 入库口");
                search.Append(SimReportFormatting.FormatInfeedPort(map, topology, report.InfeedPortIndex));
            }

            sb.Append("<details class=\"job\" data-search=\"");
            sb.Append(SimReportHtml.Escape(search.ToString()));
            sb.AppendLine("\">");
            sb.Append("<summary>任务 ");
            sb.Append(report.JobId);
            sb.Append(" · ");
            sb.Append(SimReportHtml.Escape(report.TargetSlot.ToString()));
            sb.Append(" · ");
            sb.Append(report.JobDurationSeconds.ToString("F2"));
            sb.AppendLine(" s</summary>");
            sb.AppendLine("<div class=\"job-body\">");

            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "字段", "值");
            sb.AppendLine("<tbody class=\"kv\">");
            SimReportHtml.AppendKvRow(sb, report.IsOutbound ? "源货位" : "目标货位", report.TargetSlot.ToString());
            if (report.IsOutbound)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "出库口",
                    SimReportFormatting.FormatOutfeedPort(map, topology, report.OutfeedPortIndex));
            }
            else
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "入库口",
                    SimReportFormatting.FormatInfeedPort(map, topology, report.InfeedPortIndex));
            }
            SimReportHtml.AppendKvRow(sb, "堆垛机交互点", SimReportFormatting.FormatPickupPoint(map, report.PickupPointIndex));
            SimReportHtml.AppendKvRow(sb, "堆垛机", SimReportFormatting.FormatStacker(report.StackerId));
            SimReportHtml.AppendKvRow(
                sb,
                "任务时间",
                $"{report.JobStartSimTime:F2} s → {report.JobEndSimTime:F2} s（{report.JobDurationSeconds:F2} s）");
            if (report.PathNodeCount > 0)
            {
                SimReportHtml.AppendKvRow(sb, "输送路径", $"{report.PathNodeCount} 节点");
                if (!string.IsNullOrEmpty(report.PathNodeSummary))
                {
                    SimReportHtml.AppendKvRow(sb, "路径节点", report.PathNodeSummary);
                }
            }

            if (report.SegmentCount > 0)
            {
                SimReportHtml.AppendKvRow(sb, "输送路段数", report.SegmentCount.ToString());
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            AppendSegmentSchedule(sb, report.SegmentSchedule, map);
            AppendSubTaskTable(sb, report.SubTasks);
            sb.AppendLine("</div></details>");
        }

        private static void AppendSubTaskTable(StringBuilder sb, List<SimCargoSubTaskDisplayEntry> subTasks)
        {
            if (subTasks == null || subTasks.Count == 0)
            {
                return;
            }

            sb.AppendLine("<h4>子任务时间轴</h4>");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(
                sb, "#", "子任务ID", "类型", "开始(s)", "结束(s)", "时长(s)", "说明");
            sb.AppendLine("<tbody>");

            for (var i = 0; i < subTasks.Count; i++)
            {
                var entry = subTasks[i];
                sb.AppendLine("<tr>");
                sb.Append("<td class=\"num\">");
                sb.Append(entry.Sequence);
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(entry.SubTaskId);
                sb.AppendLine("</td>");
                sb.Append("<td>");
                sb.Append(SimReportHtml.Escape(entry.KindLabel));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(entry.StartSimTime.ToString("F2"));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(entry.EndSimTime.ToString("F2"));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(entry.DurationSeconds.ToString("F2"));
                sb.AppendLine("</td>");
                sb.Append("<td>");
                sb.Append(SimReportHtml.EscapeOrDash(entry.Detail));
                sb.AppendLine("</td></tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
        }

        private static void AppendSegmentSchedule(
            StringBuilder sb,
            ConveyorSegmentScheduleEntry[] schedule,
            WarehouseConveyorMap map)
        {
            if (schedule == null || schedule.Length == 0)
            {
                return;
            }

            sb.AppendLine("<h4>输送路段预约</h4>");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "#", "路段", "槽位", "进入(s)", "离开(s)", "占用止(s)");
            sb.AppendLine("<tbody>");

            for (var i = 0; i < schedule.Length; i++)
            {
                var seg = schedule[i];
                sb.AppendLine("<tr>");
                sb.Append("<td class=\"num\">");
                sb.Append(i + 1);
                sb.AppendLine("</td>");
                sb.Append("<td>");
                sb.Append(SimReportHtml.Escape(
                    SimEntityNaming.FormatSegment(map, seg.FromNodeIndex, seg.ToNodeIndex)));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(seg.SlotIndex);
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(seg.EntrySimTime.ToString("F2"));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(seg.ExitSimTime.ToString("F2"));
                sb.AppendLine("</td>");
                sb.Append("<td class=\"num\">");
                sb.Append(seg.OccupancyEndSimTime.ToString("F2"));
                sb.AppendLine("</td></tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
        }
    }
}
