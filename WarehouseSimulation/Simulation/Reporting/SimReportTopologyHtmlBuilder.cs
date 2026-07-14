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
        private const float NodeWidth = 128f;
        private const float NodeHeight = 44f;
        private const float Padding = 32f;

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

            AppendSvgGraph(sb, map, positions, idToIndex);
            AppendNodeTable(sb, map);
            AppendEdgeTable(sb, map, idToIndex);

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
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;

            for (var i = 0; i < positions.Length; i++)
            {
                var pos = positions[i];
                minX = Math.Min(minX, pos.X);
                minY = Math.Min(minY, pos.Y);
                maxX = Math.Max(maxX, pos.X + NodeWidth);
                maxY = Math.Max(maxY, pos.Y + NodeHeight);
            }

            if (minX == float.MaxValue)
            {
                minX = minY = 0f;
                maxX = NodeWidth;
                maxY = NodeHeight;
            }

            var width = maxX - minX + Padding * 2f;
            var height = maxY - minY + Padding * 2f;
            var offsetX = Padding - minX;
            var offsetY = Padding - minY;

            sb.Append("<div class=\"topology-svg\"><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
            sb.Append(width.ToString("F1"));
            sb.Append(' ');
            sb.Append(height.ToString("F1"));
            sb.AppendLine("\" role=\"img\" aria-label=\"输送地图拓扑\">");
            sb.AppendLine("<defs><marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\"><path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"#64748b\"/></marker></defs>");

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

                    var fromCenter = NodeCenter(positions[from], offsetX, offsetY);
                    var toCenter = NodeCenter(positions[to], offsetX, offsetY);
                    var dx = toCenter.X - fromCenter.X;
                    var dy = toCenter.Y - fromCenter.Y;
                    var len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-3f)
                    {
                        continue;
                    }

                    var shrink = Math.Min(NodeWidth * 0.35f, len * 0.35f);
                    var x1 = fromCenter.X + dx / len * shrink;
                    var y1 = fromCenter.Y + dy / len * shrink;
                    var x2 = toCenter.X - dx / len * shrink;
                    var y2 = toCenter.Y - dy / len * shrink;

                    sb.Append("<line x1=\"");
                    sb.Append(x1.ToString("F1"));
                    sb.Append("\" y1=\"");
                    sb.Append(y1.ToString("F1"));
                    sb.Append("\" x2=\"");
                    sb.Append(x2.ToString("F1"));
                    sb.Append("\" y2=\"");
                    sb.Append(y2.ToString("F1"));
                    sb.AppendLine("\" class=\"topology-edge\" marker-end=\"url(#arrow)\"/>");

                    var midX = (x1 + x2) * 0.5f;
                    var midY = (y1 + y2) * 0.5f;
                    var labelOffset = HasReverseEdge(map, edge) ? (from < to ? 14f : -14f) : 0f;
                    var labelX = midX - dy / len * labelOffset;
                    var labelY = midY + dx / len * labelOffset;
                    AppendEdgeLabel(sb, map, edge, from, to, labelX, labelY);
                }
            }

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                var pos = positions[i];
                var x = pos.X + offsetX;
                var y = pos.Y + offsetY;
                var fill = KindFill(node.Kind);
                var nodeId = SimEntityNaming.FormatLogicalId(in node, i);

                sb.Append("<rect x=\"");
                sb.Append(x.ToString("F1"));
                sb.Append("\" y=\"");
                sb.Append(y.ToString("F1"));
                sb.Append("\" width=\"");
                sb.Append(NodeWidth.ToString("F1"));
                sb.Append("\" height=\"");
                sb.Append(NodeHeight.ToString("F1"));
                sb.Append("\" rx=\"6\" fill=\"");
                sb.Append(fill);
                sb.Append("\" stroke=\"#334155\" stroke-width=\"1\"/>");
                sb.Append("<text x=\"");
                sb.Append((x + NodeWidth * 0.5f).ToString("F1"));
                sb.Append("\" y=\"");
                sb.Append((y + 18f).ToString("F1"));
                sb.Append("\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"600\" fill=\"#fff\">");
                sb.Append(SimReportHtml.Escape(nodeId));
                sb.Append("</text><text x=\"");
                sb.Append((x + NodeWidth * 0.5f).ToString("F1"));
                sb.Append("\" y=\"");
                sb.Append((y + 34f).ToString("F1"));
                sb.Append("\" text-anchor=\"middle\" font-size=\"10\" fill=\"rgba(255,255,255,.85)\">");
                sb.Append(SimReportHtml.Escape(KindLabel(node.Kind)));
                sb.AppendLine("</text>");
            }

            sb.AppendLine("</svg></div>");
        }

        private static Vector2f NodeCenter(Vector2f pos, float offsetX, float offsetY) =>
            new(pos.X + offsetX + NodeWidth * 0.5f, pos.Y + offsetY + NodeHeight * 0.5f);

        private static bool HasReverseEdge(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            if (map.Edges == null)
            {
                return false;
            }

            var from = edge.FromNodeId?.Trim() ?? string.Empty;
            var to = edge.ToNodeId?.Trim() ?? string.Empty;
            for (var i = 0; i < map.Edges.Length; i++)
            {
                var other = map.Edges[i];
                if (other.FromNodeId?.Trim() == to && other.ToNodeId?.Trim() == from)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendEdgeLabel(
            StringBuilder sb,
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            int fromIndex,
            int toIndex,
            float x,
            float y)
        {
            var cap = map.GetEdgeCapacity(edge);
            var sec = map.GetEdgeTransitSeconds(edge);
            var fromId = edge.FromNodeId?.Trim() ?? string.Empty;
            var toId = edge.ToNodeId?.Trim() ?? string.Empty;
            var label = $"{SimEntityNaming.FormatSegmentLabel(map, fromId, toId)} · {edge.DistanceMeters:0.#}m · {cap}? · {sec:0.#}s";
            var textWidth = label.Length * 5.6f + 10f;
            var textHeight = 14f;
            var rectX = x - textWidth * 0.5f;
            var rectY = y - textHeight * 0.5f;

            sb.Append("<g class=\"topology-edge-label\">");
            sb.Append("<rect x=\"");
            sb.Append(rectX.ToString("F1"));
            sb.Append("\" y=\"");
            sb.Append(rectY.ToString("F1"));
            sb.Append("\" width=\"");
            sb.Append(textWidth.ToString("F1"));
            sb.Append("\" height=\"");
            sb.Append(textHeight.ToString("F1"));
            sb.AppendLine("\" rx=\"3\"/>");
            sb.Append("<text x=\"");
            sb.Append(x.ToString("F1"));
            sb.Append("\" y=\"");
            sb.Append((y + 3.5f).ToString("F1"));
            sb.Append("\" text-anchor=\"middle\" font-size=\"10\">");
            sb.Append(SimReportHtml.Escape(label));
            sb.AppendLine("</text></g>");
        }

        private static string KindFill(SimConveyorNodeKind kind) => kind switch
        {
            SimConveyorNodeKind.InfeedPort => "#35bf73",
            SimConveyorNodeKind.PickupPoint => "#45a5f2",
            SimConveyorNodeKind.ProcessStation => "#b86cf0",
            SimConveyorNodeKind.VerticalTransfer => "#59c8d1",
            _ => "#f2c740",
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
            SimReportHtml.AppendTableHead(sb, "路段", "起点", "终点", "距离(m)", "输送时间(s)", "容量");
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
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
        }

        private static string DescribeNode(SimConveyorMapNode node) => node.Kind switch
        {
            SimConveyorNodeKind.InfeedPort => "输送入口",
            SimConveyorNodeKind.PickupPoint =>
                $"堆垛机{SimEntityNaming.FormatStacker(node.StackerId)} · 列{node.PickupColumn} · 排{node.PickupRow}",
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
