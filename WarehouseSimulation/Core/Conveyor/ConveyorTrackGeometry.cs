using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送线上的一个点：在节点上，或在无向路段 (EdgeA&lt;EdgeB) 上距 EdgeA 的 Offset 米处。</summary>
    public struct ConveyorTrackPoint
    {
        public int NodeIndex;
        public int EdgeA;
        public int EdgeB;
        public double Offset;

        public bool IsNode => NodeIndex >= 0;
    }

    /// <summary>
    /// 输送线几何：zone 的物理位置与沿输送线的距离（不是直线距离，平行线不会互相影响）。
    /// 调度内核的「车身间距」判断、回放间距自检都用这一套，保证口径一致。
    /// </summary>
    public sealed class ConveyorTrackGeometry
    {
        private const double Eps = 1e-6;
        private readonly WarehouseConveyorMap _map;
        private readonly ConveyorMapTopology _topology;
        private readonly Dictionary<long, double> _edgeLength = new Dictionary<long, double>();
        private readonly List<(int To, double Len)>[] _adj;
        private readonly Dictionary<int, Dictionary<int, double>> _nodeDist = new Dictionary<int, Dictionary<int, double>>();
        private readonly double _searchRadius;

        public double CargoLength { get; }

        public ConveyorTrackGeometry(WarehouseConveyorMap map, ConveyorMapTopology topology)
        {
            _map = map;
            _topology = topology;
            CargoLength = ConveyorMapMath.GetCargoOccupancyLengthMeters(map);
            var n = map?.Nodes?.Length ?? 0;
            _adj = new List<(int, double)>[n];
            for (var i = 0; i < n; i++) _adj[i] = new List<(int, double)>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < n; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id) && !index.ContainsKey(id)) index[id] = i;
            }

            var maxEdge = 0d;
            if (map?.Edges != null)
            {
                foreach (var e in map.Edges)
                {
                    if (e.FromNodeId == null || e.ToNodeId == null
                        || !index.TryGetValue(e.FromNodeId.Trim(), out var a)
                        || !index.TryGetValue(e.ToNodeId.Trim(), out var b)
                        || a == b)
                    {
                        continue;
                    }

                    var len = Math.Max(0d, e.DistanceMeters);
                    var key = Key(a, b);
                    if (_edgeLength.TryGetValue(key, out var old))
                    {
                        if (len >= old) continue;
                        _edgeLength[key] = len;
                    }
                    else
                    {
                        _edgeLength[key] = len;
                    }

                    maxEdge = Math.Max(maxEdge, len);
                }
            }

            foreach (var pair in _edgeLength)
            {
                var a = (int)(pair.Key >> 32);
                var b = (int)(pair.Key & 0xffffffff);
                _adj[a].Add((b, pair.Value));
                _adj[b].Add((a, pair.Value));
            }

            _searchRadius = CargoLength * 2d + 1d;
        }

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        public bool TryGetEdgeLength(int a, int b, out double length) => _edgeLength.TryGetValue(Key(a, b), out length);

        public static ConveyorTrackPoint Node(int nodeIndex) =>
            new ConveyorTrackPoint { NodeIndex = nodeIndex, EdgeA = -1, EdgeB = -1, Offset = 0 };

        /// <summary>路段 from→to 上距 from 端 offset 米处。</summary>
        public ConveyorTrackPoint OnEdge(int from, int to, double offsetFromFrom)
        {
            if (!TryGetEdgeLength(from, to, out var len) || len <= Eps)
            {
                return Node(offsetFromFrom * 2 < len ? from : to);
            }

            var off = Math.Max(0d, Math.Min(len, offsetFromFrom));
            if (off <= Eps) return Node(from);
            if (off >= len - Eps) return Node(to);
            return from < to
                ? new ConveyorTrackPoint { NodeIndex = -1, EdgeA = from, EdgeB = to, Offset = off }
                : new ConveyorTrackPoint { NodeIndex = -1, EdgeA = to, EdgeB = from, Offset = len - off };
        }

        /// <summary>zone 中心位置：路段停留点按 <see cref="ConveyorMapMath.GetZoneNormalizedPosition(WarehouseConveyorMap,SimConveyorMapEdge,int,int)"/>；节点类 zone 在节点上。</summary>
        public bool TryGetZonePoint(in ConveyorPathZone zone, out ConveyorTrackPoint point)
        {
            point = default;
            if (zone.Kind == ConveyorPathZoneKind.EdgeSlot)
            {
                if (_topology == null || !_topology.TryGetEdge(zone.FromNodeIndex, zone.ToNodeIndex, out var edge))
                {
                    return false;
                }

                var cap = ConveyorMapMath.GetSegmentCapacity(_map, edge);
                var t = ConveyorMapMath.GetZoneNormalizedPosition(_map, edge, cap, zone.SlotIndex);
                point = OnEdge(zone.FromNodeIndex, zone.ToNodeIndex, t * Math.Max(0f, edge.DistanceMeters));
                return true;
            }

            if (zone.ToNodeIndex < 0) return false;
            point = Node(zone.ToNodeIndex);
            return true;
        }

        /// <summary>点在路段 a-b 上距 a 端的米数（点须在该路段或其端点上），否则 NaN。</summary>
        public double OffsetAlong(in ConveyorTrackPoint p, int a, int b)
        {
            if (!TryGetEdgeLength(a, b, out var len)) return double.NaN;
            if (p.IsNode)
            {
                if (p.NodeIndex == a) return 0;
                if (p.NodeIndex == b) return len;
                return double.NaN;
            }

            if (p.EdgeA == Math.Min(a, b) && p.EdgeB == Math.Max(a, b))
            {
                return a < b ? p.Offset : len - p.Offset;
            }

            return double.NaN;
        }

        /// <summary>沿输送线的最短距离；超过搜索半径（约两个箱长）返回 +∞。</summary>
        public double Distance(in ConveyorTrackPoint p, in ConveyorTrackPoint q)
        {
            if (!p.IsNode && !q.IsNode && p.EdgeA == q.EdgeA && p.EdgeB == q.EdgeB)
            {
                return Math.Abs(p.Offset - q.Offset);
            }

            Ends(p, out var pa, out var pac, out var pb, out var pbc);
            Ends(q, out var qa, out var qac, out var qb, out var qbc);
            var best = Pair(pa, pac, qa, qac);
            if (pb >= 0) best = Math.Min(best, Pair(pb, pbc, qa, qac));
            if (qb >= 0) best = Math.Min(best, Pair(pa, pac, qb, qbc));
            if (pb >= 0 && qb >= 0) best = Math.Min(best, Pair(pb, pbc, qb, qbc));
            return best;
        }

        private double Pair(int a, double ac, int b, double bc) => ac + bc + NodeDistance(a, b);

        private void Ends(in ConveyorTrackPoint p, out int a, out double ac, out int b, out double bc)
        {
            if (p.IsNode)
            {
                a = p.NodeIndex;
                ac = 0;
                b = -1;
                bc = 0;
                return;
            }

            TryGetEdgeLength(p.EdgeA, p.EdgeB, out var len);
            a = p.EdgeA;
            ac = p.Offset;
            b = p.EdgeB;
            bc = Math.Max(0, len - p.Offset);
        }

        private double NodeDistance(int a, int b)
        {
            if (a == b) return 0;
            if (a < 0 || b < 0 || a >= _adj.Length || b >= _adj.Length) return double.PositiveInfinity;
            if (!_nodeDist.TryGetValue(a, out var dist))
            {
                dist = BoundedDijkstra(a);
                _nodeDist[a] = dist;
            }

            return dist.TryGetValue(b, out var d) ? d : double.PositiveInfinity;
        }

        private Dictionary<int, double> BoundedDijkstra(int source)
        {
            var dist = new Dictionary<int, double> { [source] = 0 };
            var open = new List<int> { source };
            var done = new HashSet<int>();
            while (open.Count > 0)
            {
                var bi = 0;
                for (var i = 1; i < open.Count; i++)
                {
                    if (dist[open[i]] < dist[open[bi]]) bi = i;
                }

                var u = open[bi];
                open.RemoveAt(bi);
                if (!done.Add(u)) continue;
                var du = dist[u];
                foreach (var (v, len) in _adj[u])
                {
                    var nd = du + len;
                    if (nd > _searchRadius) continue;
                    if (!dist.TryGetValue(v, out var old) || nd < old - Eps)
                    {
                        dist[v] = nd;
                        open.Add(v);
                    }
                }
            }

            return dist;
        }

        /// <summary>两点之间按行驶路线插值：p 与 q 在同一路段（含端点）上，或经由节点 via 相连（p 在 via 一侧路段，q 在另一侧）。</summary>
        public ConveyorTrackPoint Interpolate(in ConveyorTrackPoint p, in ConveyorTrackPoint q, int edgeFrom, int edgeTo, double fraction)
        {
            fraction = Math.Max(0d, Math.Min(1d, fraction));
            var po = OffsetAlong(p, edgeFrom, edgeTo);
            var qo = OffsetAlong(q, edgeFrom, edgeTo);
            if (!double.IsNaN(po) && !double.IsNaN(qo))
            {
                return OnEdge(edgeFrom, edgeTo, po + (qo - po) * fraction);
            }

            if (double.IsNaN(po) && !double.IsNaN(qo) && !p.IsNode)
            {
                // p 在上一路段，经 edgeFrom 节点进入本段。
                var other = p.EdgeA == edgeFrom ? p.EdgeB : p.EdgeA;
                var d1 = OffsetAlong(p, other, edgeFrom);
                if (!double.IsNaN(d1) && TryGetEdgeLength(other, edgeFrom, out var len1))
                {
                    var first = len1 - d1;
                    var total = first + qo;
                    var s = total * fraction;
                    return s <= first ? OnEdge(other, edgeFrom, d1 + s) : OnEdge(edgeFrom, edgeTo, s - first);
                }
            }

            return fraction < 0.5 ? p : q;
        }

        public string Describe(in ConveyorTrackPoint p)
        {
            string Name(int i) => _map?.Nodes != null && i >= 0 && i < _map.Nodes.Length
                ? (string.IsNullOrWhiteSpace(_map.Nodes[i].LogicalId) ? $"n{i}" : _map.Nodes[i].LogicalId.Trim())
                : $"n{i}";
            return p.IsNode ? Name(p.NodeIndex) : $"{Name(p.EdgeA)}-{Name(p.EdgeB)}@{p.Offset:0.00}m";
        }
    }
}
