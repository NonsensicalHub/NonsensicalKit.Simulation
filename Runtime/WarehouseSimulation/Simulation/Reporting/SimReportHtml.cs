using System;
using System.Net;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真报告 HTML 片段与转义辅助。</summary>
    internal static class SimReportHtml
    {
        public const string Styles = @"
            :root {
              --bg: #f4f6f9;
              --card: #fff;
              --text: #1a1d26;
              --muted: #5c6370;
              --border: #dde2eb;
              --accent: #2563eb;
              --ok: #059669;
              --fail: #dc2626;
              --row-alt: #f8fafc;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              font-family: 'Segoe UI', 'Microsoft YaHei UI', 'PingFang SC', sans-serif;
              font-size: 14px;
              line-height: 1.5;
              color: var(--text);
              background: var(--bg);
            }
            .page { max-width: 1280px; margin: 0 auto; padding: 24px 20px 48px; }
            h1 { font-size: 1.75rem; margin: 0 0 8px; }
            h2 { font-size: 1.25rem; margin: 32px 0 12px; padding-bottom: 6px; border-bottom: 2px solid var(--accent); }
            h3 { font-size: 1.05rem; margin: 0; }
            h4 { font-size: 0.95rem; margin: 16px 0 8px; color: var(--muted); }
            .meta { color: var(--muted); font-size: 13px; }
            .meta dl { display: grid; grid-template-columns: auto 1fr; gap: 4px 16px; margin: 12px 0 0; }
            .meta dt { font-weight: 600; }
            .badge {
              display: inline-block;
              padding: 2px 10px;
              border-radius: 999px;
              font-size: 12px;
              font-weight: 600;
            }
            .badge-ok { background: #d1fae5; color: var(--ok); }
            .badge-fail { background: #fee2e2; color: var(--fail); }
            .card {
              background: var(--card);
              border: 1px solid var(--border);
              border-radius: 10px;
              padding: 16px 18px;
              margin-bottom: 16px;
              box-shadow: 0 1px 2px rgba(0,0,0,.04);
            }
            .grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
            @media (max-width: 800px) { .grid-2 { grid-template-columns: 1fr; } }
            table {
              width: 100%;
              border-collapse: collapse;
              font-size: 13px;
              background: var(--card);
            }
            th, td {
              border: 1px solid var(--border);
              padding: 8px 10px;
              text-align: left;
              vertical-align: top;
            }
            th {
              background: #eef2ff;
              font-weight: 600;
              white-space: nowrap;
            }
            tbody tr:nth-child(even) { background: var(--row-alt); }
            .table-wrap { overflow-x: auto; border-radius: 8px; border: 1px solid var(--border); }
            .num { text-align: right; font-variant-numeric: tabular-nums; }
            .toolbar {
              position: sticky;
              top: 0;
              z-index: 10;
              background: rgba(244,246,249,.92);
              backdrop-filter: blur(6px);
              padding: 12px 0;
              margin-bottom: 8px;
              display: flex;
              flex-wrap: wrap;
              gap: 12px;
              align-items: center;
            }
            .toolbar input {
              flex: 1;
              min-width: 200px;
              max-width: 320px;
              padding: 8px 12px;
              border: 1px solid var(--border);
              border-radius: 8px;
              font-size: 14px;
            }
            .toolbar a { color: var(--accent); text-decoration: none; font-size: 13px; }
            .toolbar a:hover { text-decoration: underline; }
            details.job {
              background: var(--card);
              border: 1px solid var(--border);
              border-radius: 8px;
              margin-bottom: 8px;
            }
            details.job summary {
              cursor: pointer;
              padding: 12px 14px;
              font-weight: 600;
              list-style: none;
            }
            details.job summary::-webkit-details-marker { display: none; }
            details.job summary::before {
              content: '▸ ';
              color: var(--accent);
              display: inline-block;
              width: 1em;
            }
            details.job[open] summary::before { content: '▾ '; }
            details.job .job-body { padding: 0 14px 14px; border-top: 1px solid var(--border); }
            details.section-fold { margin-bottom: 16px; }
            details.section-fold > summary {
              cursor: pointer;
              list-style: none;
              display: flex;
              flex-wrap: wrap;
              align-items: baseline;
              gap: 8px 12px;
              padding: 4px 0 8px;
              border-bottom: 2px solid var(--accent);
            }
            details.section-fold > summary::-webkit-details-marker { display: none; }
            details.section-fold > summary::before {
              content: '▸ ';
              color: var(--accent);
              font-weight: 700;
            }
            details.section-fold[open] > summary::before { content: '▾ '; }
            details.section-fold > summary .fold-title {
              font-size: 1.25rem;
              font-weight: 700;
              margin: 0;
            }
            details.section-fold > summary .fold-meta {
              color: var(--muted);
              font-size: 13px;
              font-weight: 400;
            }
            details.section-fold .fold-body { padding-top: 12px; }
            details.section-fold.card { padding-top: 12px; }
            details.section-fold.card > summary { margin: 0 18px; padding-bottom: 10px; }
            details.section-fold.card .fold-body { padding: 0 18px 16px; }
            .topology-svg {
              overflow: auto;
              border: 1px solid var(--border);
              border-radius: 8px;
              background: #fafbfc;
              margin-bottom: 16px;
            }
            .topology-svg svg { display: block; min-width: 100%; height: auto; }
            .topology-svg .topology-edge {
              stroke: #64748b;
              stroke-width: 1.5;
              fill: none;
            }
            .topology-svg .topology-edge-label rect {
              fill: rgba(255,255,255,.94);
              stroke: #cbd5e1;
              stroke-width: 1;
            }
            .topology-svg .topology-edge-label text {
              fill: #475569;
              font-family: 'Segoe UI', 'Microsoft YaHei UI', sans-serif;
              font-variant-numeric: tabular-nums;
            }
            .kv { width: auto; }
            .kv th { width: 140px; background: #f8fafc; }
            .hint { color: var(--muted); font-size: 13px; margin: 8px 0 0; }
            ";

        public const string FilterScript = @"
            (function () {
              var input = document.getElementById('job-filter');
              if (!input) return;
              input.addEventListener('input', function () {
                var q = input.value.trim().toLowerCase();
                document.querySelectorAll('details.job').forEach(function (el) {
                  var text = (el.getAttribute('data-search') || '').toLowerCase();
                  el.style.display = !q || text.indexOf(q) >= 0 ? '' : 'none';
                });
              });
            })();
            ";

        public static string Escape(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlEncode(text);

        /// <summary>转义 HTML 属性值（不破坏 file:// 等 URI 中的斜杠与冒号）。</summary>
        public static string EscapeAttribute(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("&", "&amp;").Replace("\"", "&quot;");
        }

        public static string EscapeOrDash(string text) =>
            string.IsNullOrEmpty(text) ? "—" : Escape(text);

        public static void AppendDocumentStart(StringBuilder sb, string title)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"zh-CN\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>");
            sb.Append(Escape(title));
            sb.AppendLine("</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(Styles);
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<div class=\"page\">");
        }

        public static void AppendDocumentEnd(StringBuilder sb)
        {
            sb.AppendLine("</div>");
            sb.AppendLine("<script>");
            sb.AppendLine(FilterScript);
            sb.AppendLine("</script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
        }

        public static void AppendTableHead(StringBuilder sb, params string[] headers)
        {
            sb.AppendLine("<thead><tr>");
            for (var i = 0; i < headers.Length; i++)
            {
                sb.Append("<th>");
                sb.Append(Escape(headers[i]));
                sb.AppendLine("</th>");
            }

            sb.AppendLine("</tr></thead>");
        }

        public static void AppendTableStart(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"table-wrap\"><table>");
        }

        public static void AppendTableEnd(StringBuilder sb)
        {
            sb.AppendLine("</table></div>");
        }

        public static void AppendKvRow(
            StringBuilder sb,
            string label,
            string value,
            bool numeric = false,
            bool rawHtml = false)
        {
            sb.Append("<tr><th>");
            sb.Append(Escape(label));
            sb.Append("</th><td");
            if (numeric)
            {
                sb.Append(" class=\"num\"");
            }

            sb.Append(">");
            if (rawHtml)
            {
                sb.Append(value);
            }
            else
            {
                sb.Append(Escape(value));
            }

            sb.AppendLine("</td></tr>");
        }
    }
}
