using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>运行时输送图：邻接表、入库口/堆垛机交互点索引、路径与可达性查询。</summary>
    public sealed class ConveyorMapTopology
    {
        private readonly Dictionary<string, int> _nodeIdToIndex = new();
        private readonly List<(int to, float transit)>[] _outEdges;
        private readonly List<(int from, float transit)>[] _inEdges;
        private readonly Dictionary<(int from, int to), float> _edgeTransit = new();
        private readonly Dictionary<(int from, int to), SimConveyorMapEdge> _edgeData = new();
        private readonly Dictionary<(int from, int to), int[]> _transitShortestPathCache = new();
        private readonly HashSet<(int from, int to)> _transitShortestPathMiss = new();
        private readonly Dictionary<int, int[]> _reachablePickupCache = new();

        public WarehouseConveyorMap Map { get; }
        public IReadOnlyList<int> InfeedNodeIndices { get; }
        public IReadOnlyList<int> PickupNodeIndices { get; }
        public IReadOnlyList<int> OutfeedNodeIndices { get; }

        private ConveyorMapTopology(
            WarehouseConveyorMap map,
            List<int> infeeds,
            List<int> pickups,
            List<int> outfeeds,
            List<(int to, float transit)>[] outEdges,
            List<(int from, float transit)>[] inEdges,
            Dictionary<(int from, int to), SimConveyorMapEdge> edgeData)
        {
            Map = map;
            InfeedNodeIndices = infeeds;
            PickupNodeIndices = pickups;
            OutfeedNodeIndices = outfeeds;
            _outEdges = outEdges;
            _inEdges = inEdges;
            _edgeData = edgeData;

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    _nodeIdToIndex[id] = i;
                }
            }

            foreach (var kv in _edgeData)
            {
                _edgeTransit[kv.Key] = map.GetEdgeTransitSeconds(kv.Value);
            }
        }

        /// <summary>从 ScriptableObject 构建邻接表，并校验节点 ID、取货点数量与入库口可达性。</summary>
        public static bool TryBuild(WarehouseConveyorMap map, out ConveyorMapTopology topology, out string error) =>
            TryBuild(map, null, out topology, out error);

        /// <summary>从 ScriptableObject 构建邻接表；提供 Fleet 时可按单向/双向伸叉校验交互点数量与列域。</summary>
        public static bool TryBuild(
            WarehouseConveyorMap map,
            IStackerFleetDescriptor fleet,
            out ConveyorMapTopology topology,
            out string error)
        {
            topology = null;
            error = null;

            if (map == null || map.Nodes == null || map.Nodes.Length == 0)
            {
                error = "ConveyorMap 未配置任何节点。";
                return false;
            }

            var nodeIds = new HashSet<string>();
            var logicalIds = new HashSet<string>();
            var infeeds = new List<int>();
            var pickups = new List<int>();
            var outfeeds = new List<int>();
            var pickupsPerStacker = new Dictionary<int, List<int>>();

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                var id = node.NodeId?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    error = $"节点下标 {i} 缺少 NodeId。";
                    return false;
                }

                if (!nodeIds.Add(id))
                {
                    error = $"重复的 NodeId：{id}";
                    return false;
                }

                var logicalId = node.LogicalId?.Trim();
                if (string.IsNullOrEmpty(logicalId))
                {
                    error = $"节点 {id} 缺少逻辑 ID。";
                    return false;
                }

                if (!logicalIds.Add(logicalId))
                {
                    error = $"重复的逻辑 ID：{logicalId}";
                    return false;
                }

                switch (node.Kind)
                {
                    case SimConveyorNodeKind.InfeedPort:
                        infeeds.Add(i);
                        break;
                    case SimConveyorNodeKind.OutfeedPort:
                        outfeeds.Add(i);
                        break;
                    case SimConveyorNodeKind.PickupPoint:
                        pickups.Add(i);
                        if (!pickupsPerStacker.TryGetValue(node.StackerId, out var stackerPickups))
                        {
                            stackerPickups = new List<int>();
                            pickupsPerStacker[node.StackerId] = stackerPickups;
                        }

                        stackerPickups.Add(i);

                        if (node.PickupColumn <= 0)
                        {
                            error = $"堆垛机交互点节点 {logicalId} 需要配置 PickupColumn（侧别由列号与巷道左列推导）。";
                            return false;
                        }

                        break;
                }
            }

            if (infeeds.Count == 0)
            {
                error = "地图中至少需要一个 InfeedPort 节点。";
                return false;
            }

            if (pickups.Count == 0)
            {
                error = "地图中至少需要一个堆垛机交互点（PickupPoint）节点。";
                return false;
            }

            if (!ValidatePickupPointsPerStacker(map, fleet, pickupsPerStacker, out error))
            {
                return false;
            }

            var outEdges = new List<(int to, float transit)>[map.Nodes.Length];
            var inEdges = new List<(int from, float transit)>[map.Nodes.Length];
            for (var i = 0; i < outEdges.Length; i++)
            {
                outEdges[i] = new List<(int, float)>();
                inEdges[i] = new List<(int, float)>();
            }

            var edgeData = new Dictionary<(int from, int to), SimConveyorMapEdge>();

            if (map.Edges != null)
            {
                var idToIndex = new Dictionary<string, int>();
                for (var i = 0; i < map.Nodes.Length; i++)
                {
                    idToIndex[map.Nodes[i].NodeId.Trim()] = i;
                }

                foreach (var edge in map.Edges)
                {
                    var fromId = edge.FromNodeId?.Trim();
                    var toId = edge.ToNodeId?.Trim();
                    if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
                    {
                        error = "存在缺少 From/To 的边。";
                        return false;
                    }

                    if (!idToIndex.TryGetValue(fromId, out var from) || !idToIndex.TryGetValue(toId, out var to))
                    {
                        error =
                            $"边 {SimEntityNaming.FormatLogicalId(map, fromId)} → {SimEntityNaming.FormatLogicalId(map, toId)} 引用了不存在的节点。";
                        return false;
                    }

                    var transit = map.GetEdgeTransitSeconds(edge);
                    outEdges[from].Add((to, transit));
                    inEdges[to].Add((from, transit));
                    edgeData[(from, to)] = edge;
                }
            }

            topology = new ConveyorMapTopology(map, infeeds, pickups, outfeeds, outEdges, inEdges, edgeData);

            foreach (var infeed in infeeds)
            {
                var reachablePickups = CountReachableInboundPickups(topology, infeed);
                if (reachablePickups < 1)
                {
                    var id = SimEntityNaming.FormatLogicalId(map, infeed);
                    error = $"入库口节点 {id} 沿输送网无法到达任何可入库堆垛机交互点。";
                    return false;
                }
            }

            foreach (var pickup in pickups)
            {
                ref var pickupNode = ref map.Nodes[pickup];
                if (!StackerInteractionModeUtility.AllowsInbound(in pickupNode))
                {
                    continue;
                }

                var reachableFromInfeed = false;
                foreach (var infeed in infeeds)
                {
                    if (topology.TryFindShortestPathByTransit(infeed, pickup, out var path)
                        && path != null
                        && path.Count >= 2)
                    {
                        reachableFromInfeed = true;
                        break;
                    }
                }

                if (!reachableFromInfeed)
                {
                    error =
                        $"堆垛机交互点 {SimEntityNaming.FormatLogicalId(in pickupNode, pickup)} 标记为可入库，" +
                        "但沿输送网无法从任一路入库口到达（请检查有向边是否指向该交互点）。";
                    return false;
                }
            }

            if (outfeeds.Count > 0)
            {
                foreach (var pickup in pickups)
                {
                    ref var pickupNode = ref map.Nodes[pickup];
                    if (!StackerInteractionModeUtility.AllowsOutbound(in pickupNode))
                    {
                        continue;
                    }

                    var canReachOutfeed = false;
                    foreach (var outfeed in outfeeds)
                    {
                        if (topology.TryFindShortestPathByTransit(pickup, outfeed, out var path)
                            && path != null
                            && path.Count >= 2)
                        {
                            canReachOutfeed = true;
                            break;
                        }
                    }

                    if (!canReachOutfeed)
                    {
                        error = $"堆垛机交互点节点 {SimEntityNaming.FormatLogicalId(in pickupNode, pickup)} 无法沿输送网到达任一出库口。";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidatePickupPointsPerStacker(
            WarehouseConveyorMap map,
            IStackerFleetDescriptor fleet,
            Dictionary<int, List<int>> pickupsPerStacker,
            out string error)
        {
            error = null;
            foreach (var kv in pickupsPerStacker)
            {
                var stackerId = kv.Key;
                var pickupIndices = kv.Value;
                var maxPickups = 2;
                SimStackerDefinition def = default;
                var hasDef = fleet != null
                              && StackerColumnReachUtility.TryResolveDefinitionForMapValidation(
                                  fleet, map, stackerId, out def);
                if (hasDef)
                {
                    maxPickups = StackerColumnReachUtility.GetMaxPickupPointCount(in def);
                }

                if (pickupIndices.Count > maxPickups)
                {
                    error = hasDef && def.ColumnReach == SimStackerColumnReach.OneColumn
                        ? $"堆垛机 {stackerId} 为单向伸叉，地图上最多 1 个交互点，当前 {pickupIndices.Count} 个。"
                        : $"堆垛机 {stackerId} 在地图上有超过 {maxPickups} 个堆垛机交互点节点。";
                    return false;
                }

                if (!hasDef)
                {
                    continue;
                }

                var seenColumns = new HashSet<int>();
                for (var i = 0; i < pickupIndices.Count; i++)
                {
                    ref var node = ref map.Nodes[pickupIndices[i]];
                    if (!StackerColumnReachUtility.CanReachColumn(in def, node.PickupColumn))
                    {
                        error =
                            $"堆垛机交互点 {SimEntityNaming.FormatLogicalId(in node, pickupIndices[i])} 的交互列 {node.PickupColumn} 不在堆垛机 {stackerId} 的伸叉列域内。";
                        return false;
                    }

                    if (!seenColumns.Add(node.PickupColumn))
                    {
                        error = $"堆垛机 {stackerId} 存在多个交互点使用相同交互列 {node.PickupColumn}。";
                        return false;
                    }
                }
            }

            return true;
        }

        public ref SimConveyorMapNode GetNode(int nodeIndex) => ref Map.Nodes[nodeIndex];

        public bool TryResolveNodeIndex(string nodeId, out int nodeIndex)
        {
            nodeIndex = -1;
            var id = nodeId?.Trim();
            return !string.IsNullOrEmpty(id) && _nodeIdToIndex.TryGetValue(id, out nodeIndex);
        }

        /// <summary>进入该节点的有向边（用于入库口排队路段）。</summary>
        public IReadOnlyList<(int from, float transit)> GetIncomingEdges(int nodeIndex) =>
            _inEdges[nodeIndex];

        public bool TryGetEdge(int fromNodeIndex, int toNodeIndex, out SimConveyorMapEdge edge) =>
            _edgeData.TryGetValue((fromNodeIndex, toNodeIndex), out edge);

        public float GetEdgeTransit(int fromNodeIndex, int toNodeIndex) =>
            _edgeTransit.TryGetValue((fromNodeIndex, toNodeIndex), out var t) ? t : 0f;

        public int GetEdgeCapacity(int fromNodeIndex, int toNodeIndex) =>
            TryGetEdge(fromNodeIndex, toNodeIndex, out var edge)
                ? Map.GetEdgeCapacity(edge)
                : 1;

        /// <summary>路口节点上的 ZPA zone 资源 ID（基于逻辑 ID，每节点独立）。</summary>
        public string GetJunctionZoneResourceId(int junctionNodeIndex)
        {
            if (junctionNodeIndex < 0 || junctionNodeIndex >= Map.Nodes.Length)
            {
                return SimEntityNaming.JunctionZoneResourceId(junctionNodeIndex);
            }

            ref var node = ref Map.Nodes[junctionNodeIndex];
            var logicalId = SimEntityNaming.FormatLogicalId(in node, junctionNodeIndex);
            if (logicalId != "—")
            {
                return SimEntityNaming.JunctionZoneResourceId(logicalId);
            }

            return SimEntityNaming.JunctionZoneResourceId(junctionNodeIndex);
        }

        /// <summary>无向图上两节点的最短输送时间（仅用于入库口距离比较）。</summary>
        public bool TryGetUndirectedDistance(int fromNodeIndex, int toNodeIndex, out float seconds)
        {
            seconds = 0f;
            if (fromNodeIndex == toNodeIndex)
            {
                return true;
            }

            var n = Map.Nodes.Length;
            if (fromNodeIndex < 0 || toNodeIndex < 0 || fromNodeIndex >= n || toNodeIndex >= n)
            {
                return false;
            }

            var dist = new float[n];
            var settled = new bool[n];
            Array.Fill(dist, float.MaxValue);
            dist[fromNodeIndex] = 0f;

            for (var iter = 0; iter < n; iter++)
            {
                var u = -1;
                var best = float.MaxValue;
                for (var i = 0; i < n; i++)
                {
                    if (settled[i] || dist[i] >= best)
                    {
                        continue;
                    }

                    best = dist[i];
                    u = i;
                }

                if (u < 0)
                {
                    break;
                }

                if (u == toNodeIndex)
                {
                    seconds = dist[u];
                    return true;
                }

                settled[u] = true;
                RelaxUndirectedNeighbors(u, dist, settled);
            }

            return false;
        }

        private void RelaxUndirectedNeighbors(int u, float[] dist, bool[] settled)
        {
            RelaxUndirectedEdgeList(u, _outEdges[u], dist, settled);
            RelaxUndirectedEdgeList(u, _inEdges[u], dist, settled);
        }

        private static void RelaxUndirectedEdgeList(
            int u,
            IReadOnlyList<(int neighbor, float transit)> edges,
            float[] dist,
            bool[] settled)
        {
            foreach (var (neighbor, transit) in edges)
            {
                if (settled[neighbor])
                {
                    continue;
                }

                var next = dist[u] + transit;
                if (next < dist[neighbor])
                {
                    dist[neighbor] = next;
                }
            }
        }

        /// <summary>从某节点沿有向边 BFS，收集所有可达且允许入库的堆垛机交互点下标。</summary>
        public IReadOnlyList<int> GetReachablePickupNodes(int fromNodeIndex) =>
            GetReachablePickupNodeIndices(fromNodeIndex);

        /// <summary>可达取货点下标（构建期缓存，避免放货热路径重复 BFS）。</summary>
        internal int[] GetReachablePickupNodeIndices(int fromNodeIndex)
        {
            if (_reachablePickupCache.TryGetValue(fromNodeIndex, out var cached))
            {
                return cached;
            }

            var result = new List<int>();
            var visited = new bool[Map.Nodes.Length];
            var queue = new Queue<int>();
            visited[fromNodeIndex] = true;
            queue.Enqueue(fromNodeIndex);

            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                ref var node = ref Map.Nodes[u];
                if (node.Kind == SimConveyorNodeKind.PickupPoint
                    && StackerInteractionModeUtility.AllowsInbound(in node))
                {
                    result.Add(u);
                }

                foreach (var (to, _) in _outEdges[u])
                {
                    if (visited[to])
                    {
                        continue;
                    }

                    visited[to] = true;
                    queue.Enqueue(to);
                }
            }

            cached = result.Count > 0 ? result.ToArray() : Array.Empty<int>();
            _reachablePickupCache[fromNodeIndex] = cached;
            return cached;
        }

        private static int CountReachableInboundPickups(ConveyorMapTopology topology, int fromNodeIndex) =>
            topology.GetReachablePickupNodes(fromNodeIndex).Count;

        /// <summary>有向图上按输送时间的最短路径（Dijkstra）；静态拓扑结果按起终点缓存。</summary>
        public bool TryFindShortestPathByTransit(int fromNodeIndex, int toNodeIndex, out List<int> path)
        {
            path = null;
            if (fromNodeIndex == toNodeIndex)
            {
                path = new List<int> { fromNodeIndex };
                return true;
            }

            var key = (fromNodeIndex, toNodeIndex);
            if (_transitShortestPathCache.TryGetValue(key, out var cached))
            {
                path = new List<int>(cached);
                return true;
            }

            if (_transitShortestPathMiss.Contains(key))
            {
                return false;
            }

            if (!TryFindShortestPathByTransitCore(fromNodeIndex, toNodeIndex, out path)
                || path == null
                || path.Count < 2)
            {
                _transitShortestPathMiss.Add(key);
                path = null;
                return false;
            }

            _transitShortestPathCache[key] = path.ToArray();
            return true;
        }

        /// <summary>预热入库口→取货点、取货点→出库口的静态最短路缓存。</summary>
        public void WarmTransitShortestPathCache()
        {
            foreach (var infeed in InfeedNodeIndices)
            {
                GetReachablePickupNodeIndices(infeed);
                foreach (var pickup in PickupNodeIndices)
                {
                    TryFindShortestPathByTransit(infeed, pickup, out _);
                }
            }

            if (OutfeedNodeIndices == null)
            {
                return;
            }

            foreach (var pickup in PickupNodeIndices)
            {
                foreach (var outfeed in OutfeedNodeIndices)
                {
                    TryFindShortestPathByTransit(pickup, outfeed, out _);
                }
            }
        }

        private bool TryFindShortestPathByTransitCore(int fromNodeIndex, int toNodeIndex, out List<int> path)
        {
            path = null;
            if (fromNodeIndex == toNodeIndex)
            {
                path = new List<int> { fromNodeIndex };
                return true;
            }

            var n = Map.Nodes.Length;
            if (fromNodeIndex < 0 || toNodeIndex < 0 || fromNodeIndex >= n || toNodeIndex >= n)
            {
                return false;
            }

            var dist = new float[n];
            var settled = new bool[n];
            var parent = new int[n];
            Array.Fill(dist, float.MaxValue);
            Array.Fill(parent, -1);
            dist[fromNodeIndex] = 0f;

            for (var iter = 0; iter < n; iter++)
            {
                var u = -1;
                var best = float.MaxValue;
                for (var i = 0; i < n; i++)
                {
                    if (settled[i] || dist[i] >= best)
                    {
                        continue;
                    }

                    best = dist[i];
                    u = i;
                }

                if (u < 0 || best >= float.MaxValue)
                {
                    break;
                }

                if (u == toNodeIndex)
                {
                    path = Reconstruct(parent, toNodeIndex);
                    return true;
                }

                settled[u] = true;
                foreach (var (to, transit) in _outEdges[u])
                {
                    if (settled[to])
                    {
                        continue;
                    }

                    var next = dist[u] + transit;
                    if (next < dist[to])
                    {
                        dist[to] = next;
                        parent[to] = u;
                    }
                }
            }

            return false;
        }

        /// <summary>有向图上按自定义边权的最短路径（Dijkstra）。</summary>
        public bool TryFindLowestWeightPath(
            int fromNodeIndex,
            int toNodeIndex,
            Func<int, int, float> edgeWeight,
            out List<int> path,
            out float totalWeight)
        {
            path = null;
            totalWeight = float.MaxValue;
            if (edgeWeight == null)
            {
                return false;
            }

            if (fromNodeIndex == toNodeIndex)
            {
                path = new List<int> { fromNodeIndex };
                totalWeight = 0f;
                return true;
            }

            var n = Map.Nodes.Length;
            if (fromNodeIndex < 0 || toNodeIndex < 0 || fromNodeIndex >= n || toNodeIndex >= n)
            {
                return false;
            }

            var dist = new float[n];
            var settled = new bool[n];
            var parent = new int[n];
            Array.Fill(dist, float.MaxValue);
            Array.Fill(parent, -1);
            dist[fromNodeIndex] = 0f;

            for (var iter = 0; iter < n; iter++)
            {
                var u = -1;
                var best = float.MaxValue;
                for (var i = 0; i < n; i++)
                {
                    if (settled[i] || dist[i] >= best)
                    {
                        continue;
                    }

                    best = dist[i];
                    u = i;
                }

                if (u < 0 || best >= float.MaxValue)
                {
                    break;
                }

                if (u == toNodeIndex)
                {
                    path = Reconstruct(parent, toNodeIndex);
                    totalWeight = dist[u];
                    return true;
                }

                settled[u] = true;
                foreach (var (to, _) in _outEdges[u])
                {
                    if (settled[to])
                    {
                        continue;
                    }

                    var w = edgeWeight(u, to);
                    if (w >= float.MaxValue)
                    {
                        continue;
                    }

                    var next = dist[u] + w;
                    if (next < dist[to])
                    {
                        dist[to] = next;
                        parent[to] = u;
                    }
                }
            }

            return false;
        }

        /// <summary>BFS 最短路径（按边数，非按时间）。</summary>
        public bool TryFindPath(int fromNodeIndex, int toNodeIndex, out List<int> path)
        {
            path = null;
            if (fromNodeIndex == toNodeIndex)
            {
                path = new List<int> { fromNodeIndex };
                return true;
            }

            var visited = new bool[Map.Nodes.Length];
            var parent = new int[Map.Nodes.Length];
            Array.Fill(parent, -1);

            var queue = new Queue<int>();
            visited[fromNodeIndex] = true;
            queue.Enqueue(fromNodeIndex);

            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                if (u == toNodeIndex)
                {
                    path = Reconstruct(parent, toNodeIndex);
                    return true;
                }

                foreach (var (to, _) in _outEdges[u])
                {
                    if (visited[to])
                    {
                        continue;
                    }

                    visited[to] = true;
                    parent[to] = u;
                    queue.Enqueue(to);
                }
            }

            return false;
        }

        /// <summary>估算路径总输送时间（边通过时间之和；积放等待由 ZPA 停留点调度决定）。</summary>
        public float EstimatePathSeconds(IReadOnlyList<int> pathNodeIndices)
        {
            if (pathNodeIndices == null || pathNodeIndices.Count < 2)
            {
                return 0f;
            }

            var total = 0f;
            for (var i = 1; i < pathNodeIndices.Count; i++)
            {
                total += GetEdgeTransit(pathNodeIndices[i - 1], pathNodeIndices[i]);
            }

            return total;
        }

        private static List<int> Reconstruct(int[] parent, int target)
        {
            var path = new List<int>();
            for (var v = target; v >= 0; v = parent[v])
            {
                path.Add(v);
            }

            path.Reverse();
            return path;
        }
    }
}
