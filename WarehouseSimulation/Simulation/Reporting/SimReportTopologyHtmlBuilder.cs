using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真报告中的输送地图拓扑可视化（SVG + 明细表）。</summary>
    internal static class SimReportTopologyHtmlBuilder
    {
        /// <summary>目标：相邻节点在 SVG 中大约隔这么多单位，避免世界坐标间距过小时字号/节点框盖住一切。</summary>
        private const float TargetNeighborSpacing = 28f;

        /// <summary>缩放后 SVG 最长边上限，过大时再压一点缩放。</summary>
        private const float MaxSvgDimension = 3200f;

        private const float MinNeighborSpacing = 14f;
        private const float Padding = 24f;
        private const float NodeRadius = 5f;
        private const float KeyNodeRadius = 6.5f;

        public static void AppendSection(StringBuilder sb, WarehouseConveyorMap map, string mapDisplayName = null)
        {
            if (map == null || map.Nodes == null || map.Nodes.Length == 0)
            {
                return;
            }

            var nodeCount = map.Nodes.Length;
            var edgeCount = map.Edges?.Length ?? 0;
            var positions = ResolveNodePositions(map);
            var idToIndex = BuildIdToIndex(map);

            sb.AppendLine("<details id=\"topology\" class=\"section-fold card\">");
            sb.Append("<summary><span class=\"fold-title\">地图拓扑</span>");
            sb.Append("<span class=\"fold-meta\">");
            sb.Append(nodeCount);
            sb.Append(" 节点 · ");
            sb.Append(edgeCount);
            sb.Append(" 路段");
            if (!string.IsNullOrEmpty(mapDisplayName))
            {
                sb.Append(" · ");
                sb.Append(SimReportHtml.Escape(mapDisplayName));
            }

            sb.AppendLine("</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            sb.AppendLine("<div id=\"topology-root\"><p class=\"meta\">正在解压地图拓扑…</p></div>");
            sb.AppendLine("<noscript><p><em>请启用 JavaScript 以查看地图拓扑。</em></p></noscript>");

            var body = new StringBuilder(64 * 1024);
            AppendSvgGraph(body, map, positions, idToIndex);
            AppendNodeTable(body, map);
            AppendEdgeTable(body, map, idToIndex);
            SimReportGzipHtml.AppendGzipHtmlScript(sb, "topology-data", body.ToString());
            sb.AppendLine("<script>");
            sb.AppendLine(SimReportGzipHtml.GunzipScript);
            sb.AppendLine(@"(async function(){
  var dataEl=document.getElementById('topology-data');
  var root=document.getElementById('topology-root');
  if(!dataEl||!root)return;
  try{
    var enc=dataEl.getAttribute('data-encoding')||'';
    var raw=(dataEl.textContent||'').trim();
    var html=enc==='gzip-base64'?await nkGunzipB64(raw):raw;
    root.innerHTML=html;
    var toggle=root.querySelector('#topology-label-toggle');
    var svg=root.querySelector('.topology-svg svg');
    if(toggle&&svg){
      toggle.addEventListener('change',function(){
        if(toggle.checked)svg.classList.add('show-labels');
        else svg.classList.remove('show-labels');
      });
    }
  }catch(e){
    root.innerHTML='<p><em>地图拓扑解压失败：'+String(e&&e.message?e.message:e)+'</em></p>';
  }
})();");
            sb.AppendLine("</script>");

            sb.AppendLine("</div></details>");
        }

        private static Dictionary<string, int> BuildIdToIndex(WarehouseConveyorMap map)
        {
            var idToIndex = new Dictionary<string, int>(map.Nodes.Length, StringComparer.Ordinal);
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    idToIndex[id] = i;
                }
            }

            return idToIndex;
        }

        private static Vector2f[] ResolveNodePositions(WarehouseConveyorMap map)
        {
            var positions = new Vector2f[map.Nodes.Length];
            var hasLayout = false;
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                positions[i] = new Vector2f(map.Nodes[i].EditorPosition.x, map.Nodes[i].EditorPosition.y);
                if (positions[i].X != 0f || positions[i].Y != 0f)
                {
                    hasLayout = true;
                }
            }

            if (hasLayout)
            {
                return positions;
            }

            ApplyLayerLayout(map, positions);
            return positions;
        }

        private static void ApplyLayerLayout(WarehouseConveyorMap map, Vector2f[] positions)
        {
            var layers = ConveyorMapLayerLayoutUtility.ComputeLayers(map);

            const float layerGapX = 220f;
            const float nodeGapY = 90f;
            foreach (var kv in layers)
            {
                for (var row = 0; row < kv.Value.Count; row++)
                {
                    var nodeIndex = kv.Value[row];
                    positions[nodeIndex] = new Vector2f(60f + kv.Key * layerGapX, 60f + row * nodeGapY);
                }
            }
        }

        private static void AppendSvgGraph(
            StringBuilder sb,
            WarehouseConveyorMap map,
            Vector2f[] positions,
            Dictionary<string, int> idToIndex)
        {
            ComputeBounds(positions, out var minX, out var minY, out var maxX, out var maxY);
            var spanX = Math.Max(1e-3f, maxX - minX);
            var spanY = Math.Max(1e-3f, maxY - minY);
            var medianNn = EstimateMedianNearestNeighbor(positions);
            var scale = ResolveScale(medianNn, spanX, spanY);

            var width = spanX * scale + Padding * 2f;
            var height = spanY * scale + Padding * 2f;
            // 编辑器坐标 Y 向下与 SVG 一致时无需翻转；翻转后更接近俯视习惯（小 y 在上）。
            // Dual 地图 y 大的一端是入库侧，保持原方向即可。

            sb.AppendLine("<div class=\"topology-toolbar\">");
            sb.AppendLine("<label class=\"topology-toggle\"><input type=\"checkbox\" id=\"topology-label-toggle\"/> 显示关键节点标签</label>");
            sb.AppendLine("<span class=\"hint\">默认小圆点；悬停节点/路段看详情。勾选后只给入库/出库/交互/加工/提升机加短标签。</span>");
            sb.AppendLine("</div>");
            AppendLegend(sb);

            sb.Append("<div class=\"topology-svg\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
            sb.Append(width.ToString("F1"));
            sb.Append(' ');
            sb.Append(height.ToString("F1"));
            sb.Append("\" role=\"img\" aria-label=\"输送地图拓扑\" class=\"topology-graph\">");
            sb.AppendLine();
            sb.AppendLine("<defs><marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"8\" refY=\"5\" markerWidth=\"5\" markerHeight=\"5\" orient=\"auto-start-reverse\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#64748b\"/></marker></defs>");

            if (map.Edges != null)
            {
                for (var i = 0; i < map.Edges.Length; i++)
                {
                    var edge = map.Edges[i];
                    if (!idToIndex.TryGetValue(edge.FromNodeId?.Trim() ?? string.Empty, out var from)
                        || !idToIndex.TryGetValue(edge.ToNodeId?.Trim() ?? string.Empty, out var to))
                    {
                        continue;
                    }

                    var fromPt = ToSvg(positions[from], minX, minY, scale);
                    var toPt = ToSvg(positions[to], minX, minY, scale);
                    var dx = toPt.X - fromPt.X;
                    var dy = toPt.Y - fromPt.Y;
                    var len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-3f)
                    {
                        continue;
                    }

                    var shrink = Math.Min(NodeRadius + 2f, len * 0.35f);
                    var x1 = fromPt.X + dx / len * shrink;
                    var y1 = fromPt.Y + dy / len * shrink;
                    var x2 = toPt.X - dx / len * shrink;
                    var y2 = toPt.Y - dy / len * shrink;

                    sb.Append("<line x1=\"");
                    sb.Append(x1.ToString("F1"));
                    sb.Append("\" y1=\"");
                    sb.Append(y1.ToString("F1"));
                    sb.Append("\" x2=\"");
                    sb.Append(x2.ToString("F1"));
                    sb.Append("\" y2=\"");
                    sb.Append(y2.ToString("F1"));
                    sb.Append("\" class=\"topology-edge\" marker-end=\"url(#arrow)\">");
                    sb.Append("<title>");
                    sb.Append(SimReportHtml.Escape(BuildEdgeTooltip(map, edge)));
                    sb.Append("</title></line>");
                    sb.AppendLine();
                }
            }

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                var pt = ToSvg(positions[i], minX, minY, scale);
                var fill = KindFill(node.Kind);
                var nodeId = SimEntityNaming.FormatLogicalId(in node, i);
                var isKey = IsKeyNode(node.Kind);
                var radius = isKey ? KeyNodeRadius : NodeRadius;
                var title = $"{nodeId} · {KindLabel(node.Kind)} · {DescribeNode(node)}";

                sb.Append("<g class=\"topology-node");
                if (isKey)
                {
                    sb.Append(" topology-node-key");
                }

                sb.Append("\" data-kind=\"");
                sb.Append(SimReportHtml.Escape(node.Kind.ToString()));
                sb.Append("\">");
                sb.Append("<circle cx=\"");
                sb.Append(pt.X.ToString("F1"));
                sb.Append("\" cy=\"");
                sb.Append(pt.Y.ToString("F1"));
                sb.Append("\" r=\"");
                sb.Append(radius.ToString("F1"));
                sb.Append("\" fill=\"");
                sb.Append(fill);
                sb.Append("\" stroke=\"#1e293b\" stroke-width=\"1\"/>");
                sb.Append("<title>");
                sb.Append(SimReportHtml.Escape(title));
                sb.Append("</title>");

                if (isKey)
                {
                    sb.Append("<text class=\"topology-node-label\" x=\"");
                    sb.Append(pt.X.ToString("F1"));
                    sb.Append("\" y=\"");
                    sb.Append((pt.Y - radius - 3f).ToString("F1"));
                    sb.Append("\" text-anchor=\"middle\">");
                    sb.Append(SimReportHtml.Escape(ShortLabel(nodeId)));
                    sb.Append("</text>");
                }

                sb.AppendLine("</g>");
            }

            sb.AppendLine("</svg></div>");
        }

        private static void AppendLegend(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"topology-legend\" aria-hidden=\"true\">");
            AppendLegendItem(sb, "#35bf73", "入库口");
            AppendLegendItem(sb, "#f2c740", "出库口");
            AppendLegendItem(sb, "#45a5f2", "堆垛机交互点");
            AppendLegendItem(sb, "#b86cf0", "加工站点");
            AppendLegendItem(sb, "#59c8d1", "垂直提升机");
            AppendLegendItem(sb, "#94a3b8", "路口/其它");
            sb.AppendLine("</div>");
        }

        private static void AppendLegendItem(StringBuilder sb, string color, string label)
        {
            sb.Append("<span class=\"topology-legend-item\"><span class=\"topology-legend-dot\" style=\"background:");
            sb.Append(color);
            sb.Append("\"></span>");
            sb.Append(SimReportHtml.Escape(label));
            sb.AppendLine("</span>");
        }

        private static Vector2f ToSvg(Vector2f pos, float minX, float minY, float scale) =>
            new(Padding + (pos.X - minX) * scale, Padding + (pos.Y - minY) * scale);

        private static void ComputeBounds(
            Vector2f[] positions,
            out float minX,
            out float minY,
            out float maxX,
            out float maxY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            maxX = float.MinValue;
            maxY = float.MinValue;
            for (var i = 0; i < positions.Length; i++)
            {
                minX = Math.Min(minX, positions[i].X);
                minY = Math.Min(minY, positions[i].Y);
                maxX = Math.Max(maxX, positions[i].X);
                maxY = Math.Max(maxY, positions[i].Y);
            }

            if (minX == float.MaxValue)
            {
                minX = minY = 0f;
                maxX = maxY = 1f;
            }
        }

        private static float ResolveScale(float medianNn, float spanX, float spanY)
        {
            float scale;
            if (medianNn < 1e-4f)
            {
                scale = 1f;
            }
            else if (medianNn >= TargetNeighborSpacing * 0.75f)
            {
                // 分层回退布局本身已是像素级间距，不必再放大。
                scale = 1f;
            }
            else
            {
                scale = TargetNeighborSpacing / medianNn;
            }

            var maxSpan = Math.Max(spanX, spanY);
            if (maxSpan * scale > MaxSvgDimension)
            {
                scale = MaxSvgDimension / maxSpan;
            }

            if (medianNn > 1e-4f && medianNn * scale < MinNeighborSpacing)
            {
                scale = MinNeighborSpacing / medianNn;
            }

            return scale;
        }

        private static float EstimateMedianNearestNeighbor(Vector2f[] positions)
        {
            if (positions == null || positions.Length < 2)
            {
                return TargetNeighborSpacing;
            }

            // 节点很多时只抽样，避免导出时 O(n²) 过重。
            var sampleCount = Math.Min(positions.Length, 96);
            var step = Math.Max(1, positions.Length / sampleCount);
            var dists = new List<float>(sampleCount);
            for (var i = 0; i < positions.Length && dists.Count < sampleCount; i += step)
            {
                var best = float.MaxValue;
                for (var j = 0; j < positions.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var dx = positions[i].X - positions[j].X;
                    var dy = positions[i].Y - positions[j].Y;
                    var d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > 1e-6f && d < best)
                    {
                        best = d;
                    }
                }

                if (best < float.MaxValue)
                {
                    dists.Add(best);
                }
            }

            if (dists.Count == 0)
            {
                return TargetNeighborSpacing;
            }

            dists.Sort();
            return dists[dists.Count / 2];
        }

        private static bool IsKeyNode(SimConveyorNodeKind kind) => kind switch
        {
            SimConveyorNodeKind.InfeedPort => true,
            SimConveyorNodeKind.OutfeedPort => true,
            SimConveyorNodeKind.PickupPoint => true,
            SimConveyorNodeKind.ProcessStation => true,
            SimConveyorNodeKind.VerticalTransfer => true,
            _ => false,
        };

        private static string ShortLabel(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return "?";
            }

            return nodeId.Length <= 10 ? nodeId : nodeId.Substring(0, 9) + "…";
        }

        private static string BuildEdgeTooltip(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var cap = map.GetEdgeCapacity(edge);
            var sec = map.GetEdgeTransitSeconds(edge);
            var fromId = edge.FromNodeId?.Trim() ?? string.Empty;
            var toId = edge.ToNodeId?.Trim() ?? string.Empty;
            var gateLabel = edge.GateWaitSeconds > 1e-4f ? $" · 门{edge.GateWaitSeconds:0.#}s" : string.Empty;
            var processTag = edge.ProcessTag?.Trim();
            var processLabel = string.IsNullOrEmpty(processTag) ? string.Empty : $" · 加工 {processTag}";
            return $"{SimEntityNaming.FormatSegmentLabel(map, fromId, toId)} · {edge.DistanceMeters:0.#}m · 容量{cap} · {sec:0.#}s{gateLabel}{processLabel}";
        }

        private static string KindFill(SimConveyorNodeKind kind) => kind switch
        {
            SimConveyorNodeKind.InfeedPort => "#35bf73",
            SimConveyorNodeKind.PickupPoint => "#45a5f2",
            SimConveyorNodeKind.ProcessStation => "#b86cf0",
            SimConveyorNodeKind.VerticalTransfer => "#59c8d1",
            SimConveyorNodeKind.OutfeedPort => "#f2c740",
            _ => "#94a3b8",
        };

        private static string KindLabel(SimConveyorNodeKind kind) => kind switch
        {
            SimConveyorNodeKind.InfeedPort => "入库口",
            SimConveyorNodeKind.PickupPoint => "堆垛机交互点",
            SimConveyorNodeKind.ProcessStation => "加工站点",
            SimConveyorNodeKind.VerticalTransfer => "垂直提升机",
            SimConveyorNodeKind.OutfeedPort => "出库口",
            _ => "路口",
        };

        private static void AppendNodeTable(StringBuilder sb, WarehouseConveyorMap map)
        {
            sb.AppendLine("<h4>节点列表</h4>");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "逻辑 ID", "类型", "说明");
            sb.AppendLine("<tbody>");

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                sb.AppendLine("<tr>");
                AppendTd(sb, SimEntityNaming.FormatLogicalId(in node, i));
                AppendTd(sb, KindLabel(node.Kind));
                AppendTd(sb, DescribeNode(node));
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
        }

        private static void AppendEdgeTable(
            StringBuilder sb,
            WarehouseConveyorMap map,
            Dictionary<string, int> idToIndex)
        {
            if (map.Edges == null || map.Edges.Length == 0)
            {
                return;
            }

            sb.AppendLine("<h4>路段列表</h4>");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "路段", "起点", "终点", "距离(m)", "输送时间(s)", "容量", "中间门");
            sb.AppendLine("<tbody>");

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var edge = map.Edges[i];
                var fromId = edge.FromNodeId?.Trim() ?? string.Empty;
                var toId = edge.ToNodeId?.Trim() ?? string.Empty;
                sb.AppendLine("<tr>");
                AppendTd(sb, SimEntityNaming.FormatSegmentLabel(map, fromId, toId));
                AppendTd(sb, fromId.Length > 0 ? SimEntityNaming.FormatLogicalId(map, fromId) : "—");
                AppendTd(sb, toId.Length > 0 ? SimEntityNaming.FormatLogicalId(map, toId) : "—");
                AppendTd(sb, edge.DistanceMeters.ToString("F1"), numeric: true);
                AppendTd(sb, map.GetEdgeTransitSeconds(edge).ToString("F2"), numeric: true);
                AppendTd(sb, map.GetEdgeCapacity(edge).ToString(), numeric: true);
                AppendTd(sb, DescribeEdgeGate(edge));
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
        }

        private static string DescribeEdgeGate(in SimConveyorMapEdge edge)
        {
            if (edge.GateWaitSeconds <= 1e-4f)
            {
                return "—";
            }

            var percent = ConveyorMapMath.ResolveGateNormalizedPosition(in edge) * 100f;
            return $"{edge.GateWaitSeconds:0.#}s @ {percent:0.#}%";
        }

        private static string DescribeNode(SimConveyorMapNode node) => node.Kind switch
        {
            SimConveyorNodeKind.InfeedPort => "输送入口",
            SimConveyorNodeKind.PickupPoint =>
                SimConveyorNodeBinding.HasForkDepth(in node)
                    ? $"堆垛机{SimEntityNaming.FormatStacker(node.StackerId)} · 伸叉 {node.ForkDepthMeters:0.##} m · 排{node.PickupRow}"
                    : $"堆垛机{SimEntityNaming.FormatStacker(node.StackerId)} · 列{node.PickupColumn} · 排{node.PickupRow}",
            SimConveyorNodeKind.ProcessStation =>
                $"{node.ProcessMode} · 标签 {node.ProcessTag?.Trim() ?? "—"} · 服务 {node.ProcessServiceSeconds:F0}s",
            SimConveyorNodeKind.VerticalTransfer =>
                $"组 {node.TransferGroupId?.Trim() ?? "—"} · 提升 {node.TransferSeconds:F0}s · 目标 {node.TransferTargetLogicalId?.Trim() ?? "路径下一节点"}",
            SimConveyorNodeKind.OutfeedPort => "出库发运",
            _ => string.IsNullOrEmpty(node.LogicalId?.Trim())
                ? "ZPA 停留点"
                : $"停留点 {node.LogicalId.Trim()}",
        };

        private static void AppendTd(StringBuilder sb, string text, bool numeric = false)
        {
            sb.Append("<td");
            if (numeric)
            {
                sb.Append(" class=\"num\"");
            }

            sb.Append(">");
            sb.Append(SimReportHtml.Escape(text));
            sb.Append("</td>");
        }

        private readonly struct Vector2f
        {
            public float X { get; }
            public float Y { get; }

            public Vector2f(float x, float y)
            {
                X = x;
                Y = y;
            }
        }
    }
}
