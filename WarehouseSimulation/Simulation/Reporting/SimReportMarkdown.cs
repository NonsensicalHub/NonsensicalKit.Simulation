using System.Collections.Generic;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真调试 Markdown 报告片段与转义辅助。</summary>
    internal static class SimReportMarkdown
    {
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
        }

        public static void AppendHeading(StringBuilder sb, int level, string text)
        {
            for (var i = 0; i < level; i++)
            {
                sb.Append('#');
            }

            sb.Append(' ');
            sb.AppendLine(text);
            sb.AppendLine();
        }

        public static void AppendKvTable(StringBuilder sb, IReadOnlyList<(string Label, string Value)> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("| --- | --- |");
            for (var i = 0; i < rows.Count; i++)
            {
                sb.Append("| ");
                sb.Append(Escape(rows[i].Label));
                sb.Append(" | ");
                sb.Append(Escape(rows[i].Value));
                sb.AppendLine(" |");
            }

            sb.AppendLine();
        }

        public static void AppendTable(StringBuilder sb, string[] headers, IReadOnlyList<string[]> rows)
        {
            if (headers == null || headers.Length == 0)
            {
                return;
            }

            sb.Append("| ");
            for (var h = 0; h < headers.Length; h++)
            {
                if (h > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(Escape(headers[h]));
            }

            sb.AppendLine(" |");
            sb.Append("| ");
            for (var h = 0; h < headers.Length; h++)
            {
                if (h > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append("---");
            }

            sb.AppendLine(" |");

            if (rows == null)
            {
                sb.AppendLine();
                return;
            }

            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                sb.Append("| ");
                for (var c = 0; c < headers.Length; c++)
                {
                    if (c > 0)
                    {
                        sb.Append(" | ");
                    }

                    var cell = row != null && c < row.Length ? row[c] : string.Empty;
                    sb.Append(Escape(cell));
                }

                sb.AppendLine(" |");
            }

            sb.AppendLine();
        }
    }
}
