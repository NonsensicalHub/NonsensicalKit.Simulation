#if UNITY_EDITOR
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 路段编辑辅助：节点仅提供连线端口，通行方向完全由有向边数据决定。
    /// </summary>
    internal static class ConveyorMapEditorEdgeUtility
    {
        public static string DirectedSegmentKey(string fromId, string toId)
        {
            var from = fromId?.Trim() ?? string.Empty;
            var to = toId?.Trim() ?? string.Empty;
            return $"{from}→{to}";
        }

        public static bool TryParseDirectedSegmentKey(string segmentKey, out string fromId, out string toId)
        {
            fromId = null;
            toId = null;
            if (string.IsNullOrEmpty(segmentKey))
            {
                return false;
            }

            var arrow = segmentKey.IndexOf('→');
            if (arrow <= 0 || arrow >= segmentKey.Length - 1)
            {
                return false;
            }

            fromId = segmentKey.Substring(0, arrow).Trim();
            toId = segmentKey.Substring(arrow + 1).Trim();
            return !string.IsNullOrEmpty(fromId) && !string.IsNullOrEmpty(toId);
        }

        public static bool TryFindEdgeIndex(WarehouseConveyorMap map, string segmentKey, out int edgeIndex)
        {
            edgeIndex = -1;
            if (map.Edges == null || !TryParseDirectedSegmentKey(segmentKey, out var from, out var to))
            {
                return false;
            }

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var e = map.Edges[i];
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    edgeIndex = i;
                    return true;
                }
            }

            return false;
        }

        public static bool HasDirectedEdge(WarehouseConveyorMap map, string fromId, string toId)
        {
            if (map.Edges == null)
            {
                return false;
            }

            var from = fromId?.Trim();
            var to = toId?.Trim();
            foreach (var e in map.Edges)
            {
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasDirectedEdge(System.Collections.Generic.List<SimConveyorMapEdge> edges, string fromId, string toId)
        {
            var from = fromId?.Trim();
            var to = toId?.Trim();
            foreach (var e in edges)
            {
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>数据层是否存在 A→B 与 B→A 两条有向边。</summary>
        public static bool IsBidirectionalSegment(WarehouseConveyorMap map, string nodeIdA, string nodeIdB)
        {
            var a = nodeIdA?.Trim();
            var b = nodeIdB?.Trim();
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            return HasDirectedEdge(map, a, b) && HasDirectedEdge(map, b, a);
        }

        public static void SyncReverseEdge(WarehouseConveyorMap map, SimConveyorMapEdge source)
        {
            if (map.Edges == null)
            {
                return;
            }

            var from = source.FromNodeId?.Trim();
            var to = source.ToNodeId?.Trim();
            if (!IsBidirectionalSegment(map, from, to))
            {
                return;
            }

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var e = map.Edges[i];
                if (e.FromNodeId?.Trim() == to && e.ToNodeId?.Trim() == from)
                {
                    e.DistanceMeters = source.DistanceMeters;
                    e.SpeedOverrideMetersPerSecond = source.SpeedOverrideMetersPerSecond;
                    map.Edges[i] = e;
                    return;
                }
            }
        }

        /// <summary>0=双向，1=primaryFrom→primaryTo，2=反向单向。</summary>
        public static int GetFlowModePopupIndex(WarehouseConveyorMap map, string primaryFrom, string primaryTo)
        {
            var from = primaryFrom?.Trim();
            var to = primaryTo?.Trim();
            if (IsBidirectionalSegment(map, from, to))
            {
                return 0;
            }

            return HasDirectedEdge(map, from, to) ? 1 : 2;
        }

        public static void ApplySegmentFlowMode(
            WarehouseConveyorMap map,
            string primaryFrom,
            string primaryTo,
            int flowModeIndex)
        {
            if (map == null)
            {
                return;
            }

            var from = primaryFrom?.Trim();
            var to = primaryTo?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                return;
            }

            var edges = map.Edges != null
                ? new System.Collections.Generic.List<SimConveyorMapEdge>(map.Edges)
                : new System.Collections.Generic.List<SimConveyorMapEdge>();

            if (!TryGetEdge(edges, from, to, out var template) && !TryGetEdge(edges, to, from, out template))
            {
                template = new SimConveyorMapEdge
                {
                    DistanceMeters = map.DefaultEdgeDistanceMeters > 0f ? map.DefaultEdgeDistanceMeters : 4f,
                };
            }

            RemoveDirectedEdge(edges, from, to);
            RemoveDirectedEdge(edges, to, from);

            if (flowModeIndex == 0)
            {
                edges.Add(CopyEdge(template, from, to));
                edges.Add(CopyEdge(template, to, from));
            }
            else
            {
                var actualFrom = flowModeIndex == 2 ? to : from;
                var actualTo = flowModeIndex == 2 ? from : to;
                edges.Add(CopyEdge(template, actualFrom, actualTo));
            }

            map.Edges = edges.ToArray();
        }

        private static bool TryGetEdge(
            System.Collections.Generic.List<SimConveyorMapEdge> edges,
            string fromId,
            string toId,
            out SimConveyorMapEdge edge)
        {
            var from = fromId?.Trim();
            var to = toId?.Trim();
            foreach (var e in edges)
            {
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    edge = e;
                    return true;
                }
            }

            edge = default;
            return false;
        }

        private static void RemoveDirectedEdge(
            System.Collections.Generic.List<SimConveyorMapEdge> edges,
            string fromId,
            string toId)
        {
            var from = fromId?.Trim();
            var to = toId?.Trim();
            edges.RemoveAll(e => e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to);
        }

        private static SimConveyorMapEdge CopyEdge(SimConveyorMapEdge template, string fromId, string toId) =>
            new()
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                DistanceMeters = template.DistanceMeters,
                SpeedOverrideMetersPerSecond = template.SpeedOverrideMetersPerSecond,
            };
    }
}
#endif
