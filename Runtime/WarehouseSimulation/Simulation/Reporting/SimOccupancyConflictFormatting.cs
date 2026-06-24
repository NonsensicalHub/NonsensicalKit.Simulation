using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>占用冲突记录的单行摘要格式化（HTML 概要 / 日志 / Markdown 共用）。</summary>
    public static class SimOccupancyConflictFormatting
    {
        public static string FormatCompactLine(in SimOccupancyConflictRecord conflict) =>
            $"[{SimOccupancyConflictReportBuilder.FormatCategoryLabel(conflict.Category)}] {conflict.ResourceLabel} " +
            $"| Job {conflict.JobA} [{conflict.JobAStart:F2},{conflict.JobAEnd:F2}] {SimSubTaskQuery.GetKindLabel(conflict.KindA)} " +
            $"vs Job {conflict.JobB} [{conflict.JobBStart:F2},{conflict.JobBEnd:F2}] {SimSubTaskQuery.GetKindLabel(conflict.KindB)} " +
            $"重叠 {conflict.OverlapStart:F2}~{conflict.OverlapEnd:F2}s";

        public static void AppendSummaryLine(StringBuilder sb, int index, in SimOccupancyConflictRecord conflict)
        {
            sb.Append("  [");
            sb.Append(index);
            sb.Append("] [");
            sb.Append(SimOccupancyConflictReportBuilder.FormatCategoryLabel(conflict.Category));
            sb.Append("] ");
            sb.Append(conflict.ResourceLabel);
            sb.Append(" (");
            sb.Append(conflict.ResourceKey);
            sb.Append("): Job ");
            sb.Append(conflict.JobA);
            sb.Append(" [");
            sb.Append(conflict.JobAStart.ToString("F2"));
            sb.Append(',');
            sb.Append(conflict.JobAEnd.ToString("F2"));
            sb.Append("] ");
            sb.Append(SimSubTaskQuery.GetKindLabel(conflict.KindA));
            sb.Append(" vs Job ");
            sb.Append(conflict.JobB);
            sb.Append(" [");
            sb.Append(conflict.JobBStart.ToString("F2"));
            sb.Append(',');
            sb.Append(conflict.JobBEnd.ToString("F2"));
            sb.Append("] ");
            sb.Append(SimSubTaskQuery.GetKindLabel(conflict.KindB));
            sb.Append(" 重叠 ");
            sb.Append(conflict.OverlapStart.ToString("F2"));
            sb.Append("~");
            sb.Append(conflict.OverlapEnd.ToString("F2"));
            sb.Append("s (");
            sb.Append(conflict.OverlapSeconds.ToString("F2"));
            sb.Append("s)");
        }
    }
}
