#if UNITY_EDITOR
using System.Collections.Generic;
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
                    e.GateWaitSeconds = source.GateWaitSeconds;
                    e.GateNormalizedPosition = ConveyorMapMath.MirrorGateNormalizedPosition(source.GateNormalizedPosition);
                    e.ProcessTag = source.ProcessTag;
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

        /// <summary>
        /// 按物理路段（忽略当前单向/双向）找最短路。边权用距离（米）。
        /// </summary>
        public static bool TryFindUndirectedShortestPath(
            WarehouseConveyorMap map,
            int fromIndex,
            int toIndex,
            out List<int> path)
        {
            path = null;
            if (map?.Nodes == null
                || fromIndex < 0
                || toIndex < 0
                || fromIndex >= map.Nodes.Length
                || toIndex >= map.Nodes.Length)
            {
                return false;
            }

            if (fromIndex == toIndex)
            {
                path = new List<int> { fromIndex };
                return true;
            }

            var nodeCount = map.Nodes.Length;
            var neighbors = new List<(int to, float weight)>[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                neighbors[i] = new List<(int, float)>(4);
            }

            var idToIndex = new Dictionary<string, int>(nodeCount);
            for (var i = 0; i < nodeCount; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id) && !idToIndex.ContainsKey(id))
                {
                    idToIndex.Add(id, i);
                }
            }

            var defaultDistance = map.DefaultEdgeDistanceMeters > 0f ? map.DefaultEdgeDistanceMeters : 1f;
            var edges = map.Edges;
            if (edges != null)
            {
                for (var i = 0; i < edges.Length; i++)
                {
                    var fromId = edges[i].FromNodeId?.Trim();
                    var toId = edges[i].ToNodeId?.Trim();
                    if (string.IsNullOrEmpty(fromId)
                        || string.IsNullOrEmpty(toId)
                        || !idToIndex.TryGetValue(fromId, out var from)
                        || !idToIndex.TryGetValue(toId, out var to)
                        || from == to)
                    {
                        continue;
                    }

                    var weight = edges[i].DistanceMeters > 1e-4f ? edges[i].DistanceMeters : defaultDistance;
                    neighbors[from].Add((to, weight));
                    neighbors[to].Add((from, weight));
                }
            }

            var dist = new float[nodeCount];
            var prev = new int[nodeCount];
            var used = new bool[nodeCount];
            for (var i = 0; i < nodeCount; i++)
            {
                dist[i] = float.PositiveInfinity;
                prev[i] = -1;
            }

            dist[fromIndex] = 0f;
            for (var step = 0; step < nodeCount; step++)
            {
                var u = -1;
                var best = float.PositiveInfinity;
                for (var i = 0; i < nodeCount; i++)
                {
                    if (!used[i] && dist[i] < best)
                    {
                        best = dist[i];
                        u = i;
                    }
                }

                if (u < 0 || u == toIndex)
                {
                    break;
                }

                used[u] = true;
                var list = neighbors[u];
                for (var i = 0; i < list.Count; i++)
                {
                    var v = list[i].to;
                    var next = dist[u] + list[i].weight;
                    if (next + 1e-6f < dist[v])
                    {
                        dist[v] = next;
                        prev[v] = u;
                    }
                }
            }

            if (float.IsInfinity(dist[toIndex]))
            {
                return false;
            }

            var stack = new List<int>(16);
            for (var cursor = toIndex; cursor >= 0; cursor = prev[cursor])
            {
                stack.Add(cursor);
                if (cursor == fromIndex)
                {
                    break;
                }
            }

            if (stack.Count == 0 || stack[stack.Count - 1] != fromIndex)
            {
                return false;
            }

            stack.Reverse();
            path = stack;
            return true;
        }

        /// <summary>把路径上每一跳改成 from→to 单向，返回实际改写的路段数。</summary>
        public static int ApplyOneWayAlongPath(WarehouseConveyorMap map, IReadOnlyList<int> path)
        {
            if (map?.Nodes == null || path == null || path.Count < 2)
            {
                return 0;
            }

            var changed = 0;
            for (var i = 0; i < path.Count - 1; i++)
            {
                var from = path[i];
                var to = path[i + 1];
                if ((uint)from >= (uint)map.Nodes.Length || (uint)to >= (uint)map.Nodes.Length)
                {
                    continue;
                }

                var fromId = map.Nodes[from].NodeId?.Trim();
                var toId = map.Nodes[to].NodeId?.Trim();
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
                {
                    continue;
                }

                if (GetFlowModePopupIndex(map, fromId, toId) == 1)
                {
                    continue;
                }

                ApplySegmentFlowMode(map, fromId, toId, 1);
                changed++;
            }

            return changed;
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

        private static SimConveyorMapEdge CopyEdge(SimConveyorMapEdge template, string fromId, string toId)
        {
            var sameDirection = template.FromNodeId?.Trim() == fromId?.Trim()
                                && template.ToNodeId?.Trim() == toId?.Trim();
            return new SimConveyorMapEdge
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                DistanceMeters = template.DistanceMeters,
                SpeedOverrideMetersPerSecond = template.SpeedOverrideMetersPerSecond,
                GateWaitSeconds = template.GateWaitSeconds,
                GateNormalizedPosition = sameDirection
                    ? template.GateNormalizedPosition
                    : ConveyorMapMath.MirrorGateNormalizedPosition(template.GateNormalizedPosition),
                ProcessTag = template.ProcessTag,
            };
        }
    }
}
#endif
