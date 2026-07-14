using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送地图有向图的分层拓扑排序（编辑器排版与报告 SVG 共用）。</summary>
    public static class ConveyorMapLayerLayoutUtility
    {
        /// <summary>Kahn 拓扑分层：层号 → 节点下标列表；环路或未入队节点归入第 0 层。</summary>
        public static Dictionary<int, List<int>> ComputeLayers(WarehouseConveyorMap map)
        {
            var layers = new Dictionary<int, List<int>>();
            if (map?.Nodes == null || map.Nodes.Length == 0)
            {
                return layers;
            }

            var idToIndex = BuildIdToIndex(map);
            var n = map.Nodes.Length;
            var inDegree = new int[n];

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

                    inDegree[to]++;
                }
            }

            var remaining = (int[])inDegree.Clone();
            var queue = new Queue<int>();
            for (var i = 0; i < n; i++)
            {
                if (remaining[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            var depth = new int[n];
            var placed = new HashSet<int>();

            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                placed.Add(u);
                if (!layers.TryGetValue(depth[u], out var list))
                {
                    list = new List<int>();
                    layers[depth[u]] = list;
                }

                list.Add(u);

                if (map.Edges == null)
                {
                    continue;
                }

                var fromId = map.Nodes[u].NodeId?.Trim();
                for (var i = 0; i < map.Edges.Length; i++)
                {
                    var edge = map.Edges[i];
                    if (edge.FromNodeId != fromId
                        || !idToIndex.TryGetValue(edge.ToNodeId?.Trim() ?? string.Empty, out var to))
                    {
                        continue;
                    }

                    remaining[to]--;
                    depth[to] = System.Math.Max(depth[to], depth[u] + 1);
                    if (remaining[to] == 0)
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (placed.Contains(i))
                {
                    continue;
                }

                if (!layers.TryGetValue(0, out var orphanLayer))
                {
                    orphanLayer = new List<int>();
                    layers[0] = orphanLayer;
                }

                orphanLayer.Add(i);
            }

            return layers;
        }

        private static Dictionary<string, int> BuildIdToIndex(WarehouseConveyorMap map)
        {
            var idToIndex = new Dictionary<string, int>();
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
    }
}
