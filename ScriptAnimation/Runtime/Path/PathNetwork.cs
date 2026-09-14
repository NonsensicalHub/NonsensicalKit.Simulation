    using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>路网：节点图 + A* 寻路。</summary>
    [AddComponentMenu("ScriptAnimation/路网 (PathNetwork)")]
    public class PathNetwork : MonoBehaviour
    {
        [InspectorLabel("节点列表")]
        [SerializeField] private List<PathNode> m_nodes = new List<PathNode>();
        [InspectorLabel("自动收集子节点")]
        [SerializeField] private bool m_autoCollectFromChildren = true;

        [Header("自动连边")]
        [InspectorLabel("Awake 时自动连边")]
        [SerializeField] private bool m_autoLinkOnAwake = true;

        [Tooltip("连边前清除已有双向边；已有单向边会保留，且不会被补成双向。彻底重建请先点「清除所有邻接」。")]
        [InspectorLabel("连边前清除双向边")]
        [SerializeField] private bool m_clearBeforeAutoLink = true;
        [InspectorLabel("自动双向连边")]
        [SerializeField] private bool m_autoLinkBidirectional = true;

        [Tooltip("正交连边的最大长度。≤0 = 不限制（推荐，长直道也能连）。")]
        [InspectorLabel("最大走廊长度")]
        [SerializeField] private float m_maxCorridorLength = 0f;

        [Tooltip("判定为同一条正交走廊时，另外两轴允许的偏移（米）。横/纵连边会检查高度 Y；竖向连边会检查 X/Z。")]
        [InspectorLabel("正交容差")]
        [SerializeField] private float m_orthogonalTolerance = 0.5f;

        [Tooltip("线段中间若有其他节点落在此宽度内，则不跨过去直连（应经过中间点）。")]
        [InspectorLabel("走廊净空宽度")]
        [SerializeField] private float m_corridorClearance = 0.45f;

        [HideInInspector]
        [SerializeField] private PathNode m_pathEditStart;

        [HideInInspector]
        [SerializeField] private PathNode m_pathEditGoal;

        [HideInInspector]
        [SerializeField] private string m_renamePrefix;

        [Header("场景 Gizmo")]
        [InspectorLabel("显示节点 Gizmo")]
        [SerializeField] private bool m_showNodeGizmos = true;
        [InspectorLabel("显示节点标签")]
        [SerializeField] private bool m_showNodeLabels = true;
        [InspectorLabel("Gizmo 颜色")]
        [SerializeField] private Color m_gizmoColor = new Color(0.25f, 0.9f, 1f, 0.8f);

        [Tooltip("场景中节点名字标签的颜色。白地面建议用深色。")]
        [InspectorLabel("标签颜色")]
        [SerializeField] private Color m_labelColor = new Color(0.12f, 0.12f, 0.12f, 1f);

        [Tooltip("名字标签相对节点的高度（米）。加大可避免和 Gizmo 球重叠。")]
        [InspectorLabel("标签高度")]
        [SerializeField] private float m_labelHeight = 0.55f;

        public bool ShowNodeGizmos => m_showNodeGizmos;
        public bool ShowNodeLabels => m_showNodeLabels;
        public Color GizmoColor => m_gizmoColor;
        public Color LabelColor => m_labelColor;
        public float LabelHeight => m_labelHeight;

        /// <summary>路径编辑共用的起点（单向道 / 断开连线 / 插入节点等）。</summary>
        public PathNode PathEditStart => m_pathEditStart;

        /// <summary>路径编辑共用的终点（单向道 / 断开连线 / 插入节点等）。</summary>
        public PathNode PathEditGoal => m_pathEditGoal;

        /// <summary>自动命名前缀；为空则不加。</summary>
        public string RenamePrefix => m_renamePrefix;

        /// <summary>Awake 时是否自动正交连边。外部地图导入后应关掉，以免冲掉导入邻接。</summary>
        public bool AutoLinkOnAwake
        {
            get => m_autoLinkOnAwake;
            set => m_autoLinkOnAwake = value;
        }

        /// <summary>节点扩展缓存。Bind 后只读；热路径不要扫 PathNode.GetFeature。</summary>
        public PathFeatureStore FeatureStore => _featureStore;

        private readonly List<PathNode> _pathOpen = new List<PathNode>(64);
        private readonly HashSet<PathNode> _pathOpenSet = new HashSet<PathNode>();
        private readonly Dictionary<PathNode, PathNode> _pathCameFrom = new Dictionary<PathNode, PathNode>(64);
        private readonly Dictionary<PathNode, float> _pathGScore = new Dictionary<PathNode, float>(64);
        private readonly Dictionary<PathNode, float> _pathFScore = new Dictionary<PathNode, float>(64);
        private readonly PathFeatureStore _featureStore = new PathFeatureStore();

        public IReadOnlyList<PathNode> Nodes
        {
            get
            {
                EnsureNodes();
                return m_nodes;
            }
        }

        private void Awake()
        {
            EnsureNodes();
            BindNodesNetwork();
            if (m_autoLinkOnAwake && !HasAnyEdge())
                AutoLinkNeighbors();
        }

        private void OnValidate()
        {
            // 显示配置等变更时通知托管节点刷新所属路网引用（复制/改层级后避免节点侧 GetComponentInParent 错乱）
            BindNodesNetwork();
        }

        [ContextMenu("收集子节点")]
        public void CollectNodesFromChildren()
        {
            m_nodes.Clear();
            GetComponentsInChildren(true, m_nodes);
            BindNodesNetwork();
        }

        /// <summary>将本路网写入当前节点列表，并重建 FeatureStore。</summary>
        public void BindNodesNetwork()
        {
            if (m_nodes == null)
            {
                _featureStore.Rebuild(null);
                return;
            }

            for (int i = 0; i < m_nodes.Count; i++)
            {
                if (m_nodes[i] != null)
                    m_nodes[i].SetNetwork(this);
            }

            _featureStore.Rebuild(m_nodes);
        }

        [ContextMenu("自动链接邻近节点")]
        public void AutoLinkNeighbors()
        {
            EnsureNodes(forceCollect: true);
            if (m_nodes.Count == 0)
            {
                Debug.LogWarning($"[{name}] 没有可连接的路径节点。", this);
                return;
            }

            if (m_clearBeforeAutoLink)
                ClearBidirectionalNeighborsOnly();

            int linkCount = LinkByOrthogonalNearest();
            Debug.Log($"[{name}] 自动连边完成：约 {linkCount} 条边，节点 {m_nodes.Count}。", this);
        }

        [ContextMenu("将配置路径设为单向")]
        public void MakeConfiguredPathOneWay()
        {
            var path = new List<PathNode>();
            int changed = MakeShortestPathOneWay(m_pathEditStart, m_pathEditGoal, path);
            if (changed < 0)
            {
                Debug.LogWarning($"[{name}] 找不到单向道：请配置起点和终点，并确保两点之间有路。", this);
                return;
            }

            string startName = m_pathEditStart != null ? m_pathEditStart.name : "?";
            string goalName = m_pathEditGoal != null ? m_pathEditGoal.name : "?";
            Debug.Log($"[{name}] 单向道：{startName} → {goalName}，路径节点 {path.Count}，边改写 {changed}。", this);
        }

        [ContextMenu("断开配置的最短路径")]
        public void DisconnectConfiguredShortestPath()
        {
            var path = new List<PathNode>();
            int removed = DisconnectShortestPath(m_pathEditStart, m_pathEditGoal, path);
            if (removed < 0)
            {
                Debug.LogWarning($"[{name}] 找不到可断开路径：请配置起点和终点，并确保两点之间有路。", this);
                return;
            }

            string startName = m_pathEditStart != null ? m_pathEditStart.name : "?";
            string goalName = m_pathEditGoal != null ? m_pathEditGoal.name : "?";
            Debug.Log($"[{name}] 断开最短连线：{startName} ↔ {goalName}，路径节点 {path.Count}，边清除 {removed}。", this);
        }

        [ContextMenu("在配置的两点间插入节点")]
        public void InsertConfiguredPathNode()
        {
            var inserted = InsertNodeBetween(m_pathEditStart, m_pathEditGoal);
            if (inserted == null)
            {
                Debug.LogWarning($"[{name}] 无法插入节点：请配置有直接连线的起点和终点。", this);
                return;
            }

            string startName = m_pathEditStart != null ? m_pathEditStart.name : "?";
            string goalName = m_pathEditGoal != null ? m_pathEditGoal.name : "?";
            Debug.Log($"[{name}] 插入节点：{startName} — {inserted.name} — {goalName}。", this);
        }

        [ContextMenu("清除所有邻接")]
        public void ClearAllNeighbors()
        {
            EnsureNodes(forceCollect: true);
            for (int i = 0; i < m_nodes.Count; i++)
            {
                if (m_nodes[i] != null)
                    m_nodes[i].ClearNeighbors();
            }
        }

        /// <summary>
        /// 清除错误邻居：空引用、自环、以及连接到其他路网的边。返回清理条数。
    /// </summary>
        [ContextMenu("清除错误邻居")]
        public int ClearInvalidNeighbors()
        {
            EnsureNodes(forceCollect: true);
            int removed = 0;
            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node != null)
                    removed += node.RemoveInvalidNeighbors(this);
            }

            if (removed > 0)
                Debug.Log($"[{name}] 已清除错误邻居 {removed} 条。", this);
            else
                Debug.Log($"[{name}] 没有需要清除的错误邻居。", this);

            return removed;
        }

        /// <summary>
        /// 手动健康检测：按有向邻接检查是否每个节点都能到达其他所有节点，并输出报告。
    /// 含单向边时要求强连通；返回 true 表示健康。
    /// </summary>
        [ContextMenu("健康性检测（全连通）")]
        public bool CheckReachabilityHealth()
        {
            EnsureNodes(forceCollect: true);

            var valid = new List<PathNode>(m_nodes.Count);
            int nullCount = 0;
            for (int i = 0; i < m_nodes.Count; i++)
            {
                if (m_nodes[i] == null)
                    nullCount++;
                else
                    valid.Add(m_nodes[i]);
            }

            int nodeCount = valid.Count;
            int directedEdgeCount = CountDirectedEdges(valid);
            int isolatedOut = 0;
            int isolatedIn = 0;
            var inDegree = BuildInDegree(valid);

            for (int i = 0; i < nodeCount; i++)
            {
                var node = valid[i];
                if (CountValidOutgoing(node) == 0)
                    isolatedOut++;
                if (!inDegree.TryGetValue(node, out int deg) || deg == 0)
                    isolatedIn++;
            }

            var unreachableBySource = new List<(PathNode from, List<PathNode> unreachable)>();
            int unreachablePairCount = 0;
            var reachableBuffer = new HashSet<PathNode>();
            var queue = new Queue<PathNode>();

            for (int i = 0; i < nodeCount; i++)
            {
                var from = valid[i];
                CollectDirectedReachable(from, reachableBuffer, queue);
                List<PathNode> missing = null;
                for (int j = 0; j < nodeCount; j++)
                {
                    var to = valid[j];
                    if (to == from || reachableBuffer.Contains(to))
                        continue;

                    unreachablePairCount++;
                    if (missing == null)
                        missing = new List<PathNode>();
                    missing.Add(to);
                }

                if (missing != null)
                    unreachableBySource.Add((from, missing));
            }

            bool healthy = nodeCount > 0 && nullCount == 0 && unreachablePairCount == 0;
            LogReachabilityHealthReport(
                healthy,
                nodeCount,
                nullCount,
                directedEdgeCount,
                isolatedOut,
                isolatedIn,
                unreachablePairCount,
                unreachableBySource);
            return healthy;
        }

        private static int CountDirectedEdges(List<PathNode> nodes)
        {
            int count = 0;
            for (int i = 0; i < nodes.Count; i++)
                count += CountValidOutgoing(nodes[i]);
            return count;
        }

        private static int CountValidOutgoing(PathNode node)
        {
            if (node == null)
                return 0;

            var neighbors = node.Neighbors;
            int count = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] != null && neighbors[i] != node)
                    count++;
            }

            return count;
        }

        private static Dictionary<PathNode, int> BuildInDegree(List<PathNode> nodes)
        {
            var inDegree = new Dictionary<PathNode, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                inDegree[nodes[i]] = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var neighbors = node.Neighbors;
                for (int n = 0; n < neighbors.Count; n++)
                {
                    var other = neighbors[n];
                    if (other == null || other == node || !inDegree.ContainsKey(other))
                        continue;
                    inDegree[other]++;
                }
            }

            return inDegree;
        }

        /// <summary>从 start 出发按有向邻居 BFS，收集全部可达节点（含自身）。</summary>
        private static void CollectDirectedReachable(
            PathNode start,
            HashSet<PathNode> reachable,
            Queue<PathNode> queue)
        {
            reachable.Clear();
            queue.Clear();
            if (start == null)
                return;

            reachable.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var neighbors = current.Neighbors;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    var next = neighbors[i];
                    if (next == null || !reachable.Add(next))
                        continue;
                    queue.Enqueue(next);
                }
            }
        }

        private const int HealthReportMaxSources = 40;
        private const int HealthReportMaxTargetsPerSource = 12;

        private void LogReachabilityHealthReport(
            bool healthy,
            int nodeCount,
            int nullCount,
            int directedEdgeCount,
            int isolatedOut,
            int isolatedIn,
            int unreachablePairCount,
            List<(PathNode from, List<PathNode> unreachable)> unreachableBySource)
        {
            var sb = new System.Text.StringBuilder(512);
            sb.AppendLine($"[{name}] 路网健康检测报告");
            sb.AppendLine($"结果：{(healthy ? "通过（强连通）" : "未通过")}");
            sb.AppendLine($"有效节点：{nodeCount}，空引用槽位：{nullCount}，有向边：{directedEdgeCount}");
            sb.AppendLine($"无出边节点：{isolatedOut}，无入边节点：{isolatedIn}");
            sb.AppendLine($"不可达有序对：{unreachablePairCount}（期望 0）");

            if (nodeCount == 0)
                sb.AppendLine("警告：没有任何有效节点。");

            if (unreachableBySource.Count > 0)
            {
                sb.AppendLine("不可达明细（按起点分组）：");
                int sourceLimit = Mathf.Min(HealthReportMaxSources, unreachableBySource.Count);
                for (int i = 0; i < sourceLimit; i++)
                {
                    var (from, missing) = unreachableBySource[i];
                    sb.Append("  ");
                    sb.Append(from != null ? from.name : "?");
                    sb.Append(" → 无法到达 ");
                    int targetLimit = Mathf.Min(HealthReportMaxTargetsPerSource, missing.Count);
                    for (int t = 0; t < targetLimit; t++)
                    {
                        if (t > 0)
                            sb.Append(", ");
                        sb.Append(missing[t] != null ? missing[t].name : "?");
                    }

                    if (missing.Count > targetLimit)
                        sb.Append($", …共 {missing.Count} 个");
                    sb.AppendLine();
                }

                if (unreachableBySource.Count > sourceLimit)
                    sb.AppendLine($"  …另有 {unreachableBySource.Count - sourceLimit} 个起点存在不可达目标（已截断）");
            }

            if (healthy)
                Debug.Log(sb.ToString(), this);
            else
                Debug.LogWarning(sb.ToString(), this);
        }

        /// <summary>
        /// 仅清除双向边，保留单向边（供自动连边增量更新使用）。
    /// </summary>
        private void ClearBidirectionalNeighborsOnly()
        {
            EnsureNodes(forceCollect: true);
            var seen = new HashSet<long>();
            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null)
                    continue;

                var neighbors = node.Neighbors;
                for (int n = neighbors.Count - 1; n >= 0; n--)
                {
                    var other = neighbors[n];
                    if (other == null)
                        continue;

                    long key = EdgeKey(node, other);
                    if (!seen.Add(key))
                        continue;

                    if (node.IsConnectedTo(other) && other.IsConnectedTo(node))
                    {
                        node.RemoveNeighbor(other);
                        other.RemoveNeighbor(node);
                    }
                }
            }
        }

        /// <summary>
        /// 把 start→goal 的无向最短路径改成有向单向：路径上每条边只保留前进方向，去掉回边。
    /// 成功返回改写的边数；找不到路径返回 -1。
    /// </summary>
        public int MakeShortestPathOneWay(PathNode start, PathNode goal, List<PathNode> pathOut = null)
        {
            EnsureNodes(forceCollect: true);
            var path = pathOut ?? new List<PathNode>();
            if (!TryFindUndirectedPath(start, goal, path))
                return -1;

            if (path.Count < 2)
                return 0;

            int changed = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var from = path[i];
                var to = path[i + 1];
                if (from == null || to == null)
                    continue;

                bool hadForward = from.IsConnectedTo(to);
                bool hadBackward = to.IsConnectedTo(from);
                if (!hadForward)
                {
                    from.Connect(to, bidirectional: false);
                    changed++;
                }

                if (hadBackward)
                {
                    to.RemoveNeighbor(from);
                    changed++;
                }
            }

            return changed;
        }

        /// <summary>
        /// 在 a 与 b 的世界坐标中点插入新节点：断开两点原连线，并按原方向接到新点。
    /// 原为 a→b 则变成 a→新点→b；原为双向则两端都双向连接。失败返回 null。
    /// </summary>
        public PathNode InsertNodeBetween(PathNode a, PathNode b)
        {
            if (a == null || b == null || a == b)
                return null;

            EnsureNodes(forceCollect: true);
            if (!m_nodes.Contains(a) || !m_nodes.Contains(b))
                return null;

            bool aToB = a.IsConnectedTo(b);
            bool bToA = b.IsConnectedTo(a);
            if (!aToB && !bToA)
                return null;

            var go = new GameObject("插入点");
            go.transform.SetParent(transform, false);
            go.transform.position = (a.Position + b.Position) * 0.5f;

            if (a.transform.parent == transform && b.transform.parent == transform)
            {
                int sibling = Mathf.Min(a.transform.GetSiblingIndex(), b.transform.GetSiblingIndex()) + 1;
                go.transform.SetSiblingIndex(sibling);
            }

            var mid = go.AddComponent<PathNode>();

            a.RemoveNeighbor(b);
            b.RemoveNeighbor(a);

            if (aToB)
            {
                a.Connect(mid, bidirectional: false);
                mid.Connect(b, bidirectional: false);
            }

            if (bToA)
            {
                b.Connect(mid, bidirectional: false);
                mid.Connect(a, bidirectional: false);
            }

            if (!m_nodes.Contains(mid))
                m_nodes.Add(mid);
            mid.SetNetwork(this);

            return mid;
        }

        /// <summary>
        /// 完全断开 start↔goal 无向最短路径上的所有边（双向都清掉）。
    /// 成功返回清除的邻接引用数；找不到路径返回 -1。
    /// </summary>
        public int DisconnectShortestPath(PathNode start, PathNode goal, List<PathNode> pathOut = null)
        {
            EnsureNodes(forceCollect: true);
            var path = pathOut ?? new List<PathNode>();
            if (!TryFindUndirectedPath(start, goal, path))
                return -1;

            if (path.Count < 2)
                return 0;

            int removed = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                if (a == null || b == null)
                    continue;

                if (a.IsConnectedTo(b))
                {
                    a.RemoveNeighbor(b);
                    removed++;
                }

                if (b.IsConnectedTo(a))
                {
                    b.RemoveNeighbor(a);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// 按无向邻接（任一方向有边即通）做 A*，用于路径编辑（单向改写 / 断开连线等）。
    /// </summary>
        public bool TryFindUndirectedPath(PathNode start, PathNode goal, List<PathNode> result)
        {
            if (result == null)
                return false;

            result.Clear();
            if (start == null || goal == null)
                return false;

            if (start == goal)
            {
                result.Add(start);
                return true;
            }

            EnsureNodes();
            var adjacency = BuildUndirectedAdjacency();
            if (!adjacency.ContainsKey(start) || !adjacency.ContainsKey(goal))
                return false;

            var open = new List<PathNode> { start };
            var cameFrom = new Dictionary<PathNode, PathNode>();
            var gScore = new Dictionary<PathNode, float> { [start] = 0f };
            var fScore = new Dictionary<PathNode, float> { [start] = Heuristic(start, goal) };

            while (open.Count > 0)
            {
                var current = PopLowestF(open, fScore);
                if (current == goal)
                {
                    Reconstruct(cameFrom, current, result);
                    return true;
                }

                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    var neighbor = neighbors[i];
                    if (neighbor == null)
                        continue;

                    float tentative = gScore[current] + Distance(current, neighbor);
                    if (gScore.TryGetValue(neighbor, out float existing) && tentative >= existing)
                        continue;

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentative;
                    fScore[neighbor] = tentative + Heuristic(neighbor, goal);
                    if (!open.Contains(neighbor))
                        open.Add(neighbor);
                }
            }

            return false;
        }

        private Dictionary<PathNode, List<PathNode>> BuildUndirectedAdjacency()
        {
            EnsureNodes();
            var map = new Dictionary<PathNode, List<PathNode>>();
            var queue = new Queue<PathNode>();
            var enqueued = new HashSet<PathNode>();

            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null || !enqueued.Add(node))
                    continue;
                queue.Enqueue(node);
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!map.TryGetValue(node, out var listA))
                {
                    listA = new List<PathNode>();
                    map[node] = listA;
                }

                var neighbors = node.Neighbors;
                for (int n = 0; n < neighbors.Count; n++)
                {
                    var other = neighbors[n];
                    if (other == null || other == node)
                        continue;

                    if (!listA.Contains(other))
                        listA.Add(other);

                    if (!map.TryGetValue(other, out var listB))
                    {
                        listB = new List<PathNode>();
                        map[other] = listB;
                    }

                    if (!listB.Contains(node))
                        listB.Add(node);

                    if (enqueued.Add(other))
                        queue.Enqueue(other);
                }
            }

            return map;
        }

        /// <summary>
        /// 按世界位置排序子节点（先 X 后 Z，忽略 Y），并同步 m_nodes 顺序。
    /// 名称含「#」的特殊节点固定放在最前，不参与位置排序。
    /// </summary>
        [ContextMenu("按位置排序节点")]
        public void SortNodesByPosition()
        {
            EnsureNodes(forceCollect: true);
            if (m_nodes.Count == 0)
            {
                Debug.LogWarning($"[{name}] 没有可排序的路径节点。", this);
                return;
            }

            var sorted = BuildSortedNodes(CompareByPosition);
            ApplySortedNodes(sorted);
            Debug.Log($"[{name}] 按位置排序完成：节点 {sorted.Count}。", this);
        }

        /// <summary>
        /// 按节点名字自然序排序子节点（数字按数值比较，如 xx-2 在 xx-10 前），并同步 m_nodes 顺序。
    /// 名称含「#」的特殊节点固定放在最前，不参与名字排序。
    /// </summary>
        [ContextMenu("按名称排序节点")]
        public void SortNodesByName()
        {
            EnsureNodes(forceCollect: true);
            if (m_nodes.Count == 0)
            {
                Debug.LogWarning($"[{name}] 没有可排序的路径节点。", this);
                return;
            }

            var sorted = BuildSortedNodes(CompareByName);
            ApplySortedNodes(sorted);
            Debug.Log($"[{name}] 按名字排序完成：节点 {sorted.Count}。", this);
        }

        /// <summary>
        /// 特殊名（含 #）保持原相对顺序置顶，其余节点按 comparison 排序。
    /// </summary>
        private List<PathNode> BuildSortedNodes(Comparison<PathNode> comparison)
        {
            var special = new List<PathNode>();
            var normal = new List<PathNode>();
            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null)
                    continue;
                if (HasSpecialName(node))
                    special.Add(node);
                else
                    normal.Add(node);
            }

            normal.Sort(comparison);

            var sorted = new List<PathNode>(special.Count + normal.Count);
            sorted.AddRange(special);
            sorted.AddRange(normal);
            return sorted;
        }

        private void ApplySortedNodes(List<PathNode> sorted)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != null)
                    sorted[i].transform.SetSiblingIndex(i);
            }

            m_nodes.Clear();
            m_nodes.AddRange(sorted);
        }

        /// <summary>
        /// 按邻居连接自动命名节点，格式「类型名-序号」。
    /// 邻居按无向统计（出边或入边任一存在即算邻接），避免单向道中间点被误判为端点。
    /// 同类序号按位置（先X 后Z）递增。0=孤立，1=端点，2=通道或拐角，3=三岔，4+=路口。
    /// 两个邻接点时：两方向接近平行（夹角小于 10°）为通道，否则为拐角。
    /// 名称含「#」的节点视为特殊命名，一开始就排除，不参与命名也不占用序号。
    /// </summary>
        [ContextMenu("自动重命名节点")]
        public void AutoRenameNodes()
        {
            EnsureNodes(forceCollect: true);
            if (m_nodes.Count == 0)
            {
                Debug.LogWarning($"[{name}] 没有可命名的路径节点。", this);
                return;
            }

            var toRename = new List<PathNode>(m_nodes.Count);
            int skipped = 0;
            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null)
                    continue;
                if (HasSpecialName(node))
                {
                    skipped++;
                    continue;
                }

                toRename.Add(node);
            }

            toRename.Sort(CompareByPosition);

            var adjacency = BuildUndirectedAdjacency();
            var typeCounters = new Dictionary<string, int>(8);
            int renamed = 0;
            for (int i = 0; i < toRename.Count; i++)
            {
                var node = toRename[i];
                string typeName = GetNodeTypeName(node, adjacency);
                if (!typeCounters.TryGetValue(typeName, out int counter))
                    counter = 0;
                counter++;
                typeCounters[typeName] = counter;

                string newName = BuildRenamedName(typeName, counter, m_renamePrefix);
                if (node.gameObject.name == newName)
                    continue;

                node.gameObject.name = newName;
                renamed++;
            }

            Debug.Log(
                $"[{name}] 自动命名完成：参与 {toRename.Count}，重命名 {renamed}，排除特殊命名 {skipped}。",
                this);
        }

        private static string BuildRenamedName(string typeName, int counter, string prefix)
        {
            string name = $"{typeName}-{counter}";
            if (string.IsNullOrEmpty(prefix))
                return name;
            return prefix + name;
        }

        private static bool HasSpecialName(PathNode node)
        {
            return node != null && node.gameObject.name.IndexOf('#') >= 0;
        }

        private static int CompareByPosition(PathNode a, PathNode b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            Vector3 pa = a.Position;
            Vector3 pb = b.Position;
            int cmpX = pa.x.CompareTo(pb.x);
            if (cmpX != 0)
                return cmpX;
            int cmpZ = pa.z.CompareTo(pb.z);
            if (cmpZ != 0)
                return cmpZ;
            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        private static int CompareByName(PathNode a, PathNode b)
        {
            if (a == null && b == null)
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int cmp = CompareNatural(a.name, b.name);
            if (cmp != 0)
                return cmp;
            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        /// <summary>自然序比较：连续数字按数值比，使 xx-2 排在 xx-10 前。</summary>
        private static int CompareNatural(string a, string b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;

            int i = 0;
            int j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i];
                char cb = b[j];
                bool digA = char.IsDigit(ca);
                bool digB = char.IsDigit(cb);

                if (digA && digB)
                {
                    long numA = 0;
                    long numB = 0;
                    int startI = i;
                    int startJ = j;
                    while (i < a.Length && char.IsDigit(a[i]))
                    {
                        numA = numA * 10 + (a[i] - '0');
                        i++;
                    }

                    while (j < b.Length && char.IsDigit(b[j]))
                    {
                        numB = numB * 10 + (b[j] - '0');
                        j++;
                    }

                    if (numA != numB)
                        return numA.CompareTo(numB);

                    int lenA = i - startI;
                    int lenB = j - startJ;
                    if (lenA != lenB)
                        return lenA.CompareTo(lenB);
                    continue;
                }

                if (ca != cb)
                    return ca.CompareTo(cb);

                i++;
                j++;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        private const float TwoNeighborParallelAngleDeg = 10f;

        /// <summary>
        /// 按无向邻居连接得到类型名。两个邻接点时再按方向夹角区分通道/拐角。
    /// 无向：当前节点指向对方，或对方指向当前节点，都算邻接。
    /// </summary>
        public static string GetNodeTypeName(PathNode node)
        {
            if (node == null)
                return GetNodeTypeName(0);

            return GetNodeTypeName(node, CollectUndirectedNeighbors(node));
        }

        private static string GetNodeTypeName(PathNode node, Dictionary<PathNode, List<PathNode>> undirectedAdjacency)
        {
            if (node == null)
                return GetNodeTypeName(0);

            if (undirectedAdjacency == null ||
                !undirectedAdjacency.TryGetValue(node, out var neighbors) ||
                neighbors == null)
                return GetNodeTypeName(0);

            return GetNodeTypeName(node, neighbors);
        }

        private static string GetNodeTypeName(PathNode node, IReadOnlyList<PathNode> undirectedNeighbors)
        {
            int neighborCount = CountValidNeighbors(undirectedNeighbors);
            if (neighborCount == 2 && !AreTwoNeighborDirectionsNearlyParallel(node, undirectedNeighbors))
                return "拐角";

            return GetNodeTypeName(neighborCount);
        }

        /// <summary>按邻居数得到类型名。</summary>
        public static string GetNodeTypeName(int neighborCount)
        {
            switch (neighborCount)
            {
                case 0: return "孤立";
                case 1: return "端点";
                case 2: return "通道";
                case 3: return "三岔";
                default: return "路口";
            }
        }

        /// <summary>收集无向邻接：出边 + 指向自己的入边，去重。</summary>
        private static List<PathNode> CollectUndirectedNeighbors(PathNode node)
        {
            var result = new List<PathNode>();
            if (node == null)
                return result;

            var seen = new HashSet<PathNode>();
            var outgoing = node.Neighbors;
            for (int i = 0; i < outgoing.Count; i++)
            {
                var n = outgoing[i];
                if (n == null || n == node)
                    continue;
                if (seen.Add(n))
                    result.Add(n);
            }

            var network = node.Network;
            if (network == null)
                return result;

            var nodes = network.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var other = nodes[i];
                if (other == null || other == node || seen.Contains(other))
                    continue;
                if (!other.IsConnectedTo(node))
                    continue;
                if (seen.Add(other))
                    result.Add(other);
            }

            return result;
        }

        private static int CountValidNeighbors(IReadOnlyList<PathNode> neighbors)
        {
            if (neighbors == null)
                return 0;

            int count = 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] != null)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 两个有效邻接点的方向是否接近平行（三维空间中两线夹角小于 10°）。
    /// 直线通道两端方向相反，作为无向线夹角接近 0°。
    /// </summary>
        private static bool AreTwoNeighborDirectionsNearlyParallel(
            PathNode node,
            IReadOnlyList<PathNode> undirectedNeighbors)
        {
            if (node == null || undirectedNeighbors == null)
                return true;

            PathNode a = null;
            PathNode b = null;
            for (int i = 0; i < undirectedNeighbors.Count; i++)
            {
                var n = undirectedNeighbors[i];
                if (n == null)
                    continue;
                if (a == null)
                {
                    a = n;
                    continue;
                }

                b = n;
                break;
            }

            if (a == null || b == null)
                return true;

            Vector3 origin = node.Position;
            Vector3 d1 = a.Position - origin;
            Vector3 d2 = b.Position - origin;
            if (d1.sqrMagnitude < 0.0001f || d2.sqrMagnitude < 0.0001f)
                return true;

            float angle = Vector3.Angle(d1, d2);
            float lineAngle = Mathf.Min(angle, 180f - angle);
            return lineAngle < TwoNeighborParallelAngleDeg;
        }

        public PathNode FindNearest(Vector3 worldPosition)
        {
            EnsureNodes();
            PathNode best = null;
            float bestSq = float.MaxValue;
            Vector3 flat = worldPosition;
            flat.y = 0f;

            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null)
                    continue;

                Vector3 p = node.Position;
                p.y = 0f;
                float sq = (p - flat).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = node;
                }
            }

            return best;
        }

        public bool TryFindPath(PathNode start, PathNode goal, List<PathNode> result)
        {
            if (result == null)
                return false;

            result.Clear();
            if (start == null || goal == null)
                return false;

            if (start == goal)
            {
                result.Add(start);
                return true;
            }

            EnsureNodes();
            if (!HasAnyEdge() && m_autoLinkOnAwake)
                AutoLinkNeighbors();

            var open = _pathOpen;
            var openSet = _pathOpenSet;
            var cameFrom = _pathCameFrom;
            var gScore = _pathGScore;
            var fScore = _pathFScore;
            open.Clear();
            openSet.Clear();
            cameFrom.Clear();
            gScore.Clear();
            fScore.Clear();

            open.Add(start);
            openSet.Add(start);
            gScore[start] = 0f;
            fScore[start] = Heuristic(start, goal);

            while (open.Count > 0)
            {
                var current = PopLowestF(open, openSet, fScore);
                if (current == goal)
                {
                    Reconstruct(cameFrom, current, result);
                    return true;
                }

                var neighbors = current.Neighbors;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    var neighbor = neighbors[i];
                    if (neighbor == null)
                        continue;

                    float tentative = gScore[current] + Distance(current, neighbor);
                    if (gScore.TryGetValue(neighbor, out float existing) && tentative >= existing)
                        continue;

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentative;
                    fScore[neighbor] = tentative + Heuristic(neighbor, goal);
                    if (openSet.Add(neighbor))
                        open.Add(neighbor);
                }
            }

            return false;
        }

        public bool TryBuildWorldPoints(
            PathNode start, PathNode goal, List<Vector3> points, List<float> pauseDurations = null)
        {
            if (points == null)
                return false;

            points.Clear();
            pauseDurations?.Clear();
            var nodes = new List<PathNode>();
            if (!TryFindPath(start, goal, nodes))
                return false;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null)
                    continue;

                points.Add(nodes[i].Position);
                pauseDurations?.Add(nodes[i].PauseDuration);
            }

            return points.Count > 0;
        }

        /// <summary>
        /// 对每个节点，在 +X/-X/+Y/-Y/+Z/-Z 六向连接最近且中间无其它节点的邻居。
    /// 长直道只需两端点即可连上（默认不限制走廊长度）。
    /// </summary>
        private int LinkByOrthogonalNearest()
        {
            var dirs = new[]
            {
                Vector3.right,
                Vector3.left,
                Vector3.up,
                Vector3.down,
                Vector3.forward,
                Vector3.back,
            };

            var linked = new HashSet<long>();
            int linkCount = 0;
            float maxLen = m_maxCorridorLength > 0f ? m_maxCorridorLength : float.MaxValue;
            float tol = Mathf.Max(0.01f, m_orthogonalTolerance);

            for (int i = 0; i < m_nodes.Count; i++)
            {
                var node = m_nodes[i];
                if (node == null)
                    continue;

                for (int d = 0; d < dirs.Length; d++)
                {
                    var best = FindNearestInDirection(node, dirs[d], tol, maxLen);
                    if (best == null)
                        continue;

                    long key = EdgeKey(node, best);
                    if (linked.Contains(key) || node.IsConnectedTo(best) || best.IsConnectedTo(node))
                        continue;

                    node.Connect(best, m_autoLinkBidirectional);
                    linked.Add(key);
                    linkCount++;
                }
            }

            return linkCount;
        }

        private PathNode FindNearestInDirection(PathNode from, Vector3 dir, float orthogonalTolerance, float maxLength)
        {
            PathNode best = null;
            float bestDist = float.MaxValue;
            Vector3 origin = from.Position;

            for (int i = 0; i < m_nodes.Count; i++)
            {
                var other = m_nodes[i];
                if (other == null || other == from)
                    continue;

                Vector3 delta = other.Position - origin;
                float along = Vector3.Dot(delta, dir);
                if (along <= 0.01f)
                    continue;

                float dist = delta.magnitude;
                if (dist < 0.0001f || dist > maxLength)
                    continue;

                // 搜索轴上的分量不计入次轴偏移：X 向查 Y/Z，Y 向查 X/Z，Z 向查 X/Y。
                float ox = Mathf.Abs(delta.x) * (1f - Mathf.Abs(dir.x));
                float oy = Mathf.Abs(delta.y) * (1f - Mathf.Abs(dir.y));
                float oz = Mathf.Abs(delta.z) * (1f - Mathf.Abs(dir.z));
                if (ox > orthogonalTolerance || oy > orthogonalTolerance || oz > orthogonalTolerance)
                    continue;

                if (dist >= bestDist)
                    continue;

                if (!IsSegmentClear(from, other))
                    continue;

                best = other;
                bestDist = dist;
            }

            return best;
        }

        /// <summary>
        /// 若 a-b 三维线段中间有节点挡路，则不可直连（应经过中间点）。
    /// </summary>
        private bool IsSegmentClear(PathNode a, PathNode b)
        {
            Vector3 aPos = a.Position;
            Vector3 bPos = b.Position;
            Vector3 ab = bPos - aPos;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 0.0001f)
                return false;

            float clearance = Mathf.Max(0.01f, m_corridorClearance);
            float clearanceSq = clearance * clearance;

            for (int i = 0; i < m_nodes.Count; i++)
            {
                var c = m_nodes[i];
                if (c == null || c == a || c == b)
                    continue;

                Vector3 p = c.Position;
                Vector3 ap = p - aPos;
                float t = Vector3.Dot(ap, ab) / abLenSq;
                if (t <= 0.02f || t >= 0.98f)
                    continue;

                Vector3 closest = aPos + ab * t;
                if ((p - closest).sqrMagnitude <= clearanceSq)
                    return false;
            }

            return true;
        }

        private bool HasAnyEdge()
        {
            EnsureNodes();
            for (int i = 0; i < m_nodes.Count; i++)
            {
                if (m_nodes[i] != null && m_nodes[i].Neighbors.Count > 0)
                    return true;
            }

            return false;
        }

        private void EnsureNodes(bool forceCollect = false)
        {
            if (forceCollect || (m_autoCollectFromChildren && (m_nodes == null || m_nodes.Count == 0 || m_nodes.Count != this.transform.childCount)))
                CollectNodesFromChildren();
        }

        private static float Distance(PathNode a, PathNode b) => Vector3.Distance(a.Position, b.Position);
        private static float Heuristic(PathNode a, PathNode b) => Distance(a, b);

        private static long EdgeKey(PathNode a, PathNode b)
        {
            int idA = a.GetInstanceID();
            int idB = b.GetInstanceID();
            if (idA > idB)
                (idA, idB) = (idB, idA);
            return ((long)idA << 32) ^ (uint)idB;
        }

        private static PathNode PopLowestF(
            List<PathNode> open,
            HashSet<PathNode> openSet,
            Dictionary<PathNode, float> fScore)
        {
            int bestIndex = 0;
            float bestF = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                float f = fScore.TryGetValue(open[i], out float value) ? value : float.MaxValue;
                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            var best = open[bestIndex];
            int last = open.Count - 1;
            open[bestIndex] = open[last];
            open.RemoveAt(last);
            openSet.Remove(best);
            return best;
        }

        private static PathNode PopLowestF(List<PathNode> open, Dictionary<PathNode, float> fScore)
        {
            int bestIndex = 0;
            float bestF = float.MaxValue;
            for (int i = 0; i < open.Count; i++)
            {
                float f = fScore.TryGetValue(open[i], out float value) ? value : float.MaxValue;
                if (f < bestF)
                {
                    bestF = f;
                    bestIndex = i;
                }
            }

            var best = open[bestIndex];
            int last = open.Count - 1;
            open[bestIndex] = open[last];
            open.RemoveAt(last);
            return best;
        }

        private static void Reconstruct(
            Dictionary<PathNode, PathNode> cameFrom,
            PathNode current,
            List<PathNode> result)
        {
            result.Clear();
            result.Add(current);
            while (cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                result.Add(current);
            }

            result.Reverse();
        }

    }
}
