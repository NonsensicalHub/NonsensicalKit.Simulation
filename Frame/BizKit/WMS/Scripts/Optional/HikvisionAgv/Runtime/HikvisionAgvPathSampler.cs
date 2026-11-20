using System.Collections.Generic;
using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace HikvisionAgv
{
    /// <summary>
    /// 把本层海康路网收成一条可循环折线（沿边走，尽头折返）。
    /// 离线模拟用海康 MapX/MapY，不是 Unity 世界坐标。
    /// </summary>
    public sealed class HikvisionAgvPathSampler
    {
        readonly Vector2[] _points;
        readonly float[] _cum;
        readonly float _length;

        HikvisionAgvPathSampler(Vector2[] points, float[] cum, float length)
        {
            _points = points;
            _cum = cum;
            _length = length;
        }

        public float Length => _length;

        public static bool TryCreate(string mapShortName, out HikvisionAgvPathSampler sampler)
        {
            return TryCreate(HikvisionMapFrame.FindByMapShortName(mapShortName), out sampler);
        }

        public static bool TryCreate(HikvisionMapFrame frame, out HikvisionAgvPathSampler sampler)
        {
            sampler = null;
            if (frame == null)
                return false;

            var walk = BuildWalk(frame);
            if (walk == null || walk.Count == 0)
                return false;

            if (walk.Count == 1)
            {
                sampler = new HikvisionAgvPathSampler(
                    new[] { walk[0] },
                    new[] { 0f },
                    0f);
                return true;
            }

            var cum = new float[walk.Count];
            float total = 0f;
            for (int i = 1; i < walk.Count; i++)
            {
                total += Vector2.Distance(walk[i - 1], walk[i]);
                cum[i] = total;
            }

            if (total < 1e-3f)
                return false;

            sampler = new HikvisionAgvPathSampler(walk.ToArray(), cum, total);
            return true;
        }

        public void Sample(float distance, out float mapX, out float mapY, out float headingDeg)
        {
            if (_points.Length == 1 || _length < 1e-3f)
            {
                mapX = _points[0].x;
                mapY = _points[0].y;
                headingDeg = 0f;
                return;
            }

            float d = distance % _length;
            if (d < 0f)
                d += _length;

            int hi = 1;
            while (hi < _cum.Length && _cum[hi] < d)
                hi++;
            int lo = hi - 1;
            if (hi >= _points.Length)
                hi = _points.Length - 1;
            if (lo < 0)
                lo = 0;

            Vector2 a = _points[lo];
            Vector2 b = _points[hi];
            float seg = _cum[hi] - _cum[lo];
            float t = seg > 1e-6f ? (d - _cum[lo]) / seg : 0f;
            Vector2 p = Vector2.LerpUnclamped(a, b, t);
            mapX = p.x;
            mapY = p.y;

            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-4f && lo > 0)
                dir = a - _points[lo - 1];
            headingDeg = dir.sqrMagnitude < 1e-4f
                ? 0f
                : Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }

        static List<Vector2> BuildWalk(HikvisionMapFrame frame)
        {
            var network = frame.Network;
            if (network == null)
                return null;

            var nodes = network.Nodes;
            if (nodes == null || nodes.Count == 0)
                return null;

            var hik = new List<PathNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;
                var feature = node.GetFeature<HikvisionPathPointFeature>();
                if (feature == null)
                    continue;
                if (!string.IsNullOrEmpty(frame.MapId) &&
                    !string.IsNullOrEmpty(feature.MapId) &&
                    feature.MapId != frame.MapId)
                    continue;
                hik.Add(node);
            }

            if (hik.Count == 0)
                return null;

            var set = new HashSet<PathNode>(hik);
            List<Vector2> best = null;
            var visitedComponent = new HashSet<PathNode>();

            for (int i = 0; i < hik.Count; i++)
            {
                var seed = hik[i];
                if (visitedComponent.Contains(seed))
                    continue;

                var start = FindEndpoint(seed, set) ?? seed;
                var walk = WalkComponent(start, set, visitedComponent);
                if (walk == null || walk.Count == 0)
                    continue;
                if (best == null || walk.Count > best.Count)
                    best = walk;
            }

            return best;
        }

        static PathNode FindEndpoint(PathNode seed, HashSet<PathNode> set)
        {
            var q = new Queue<PathNode>();
            var seen = new HashSet<PathNode>();
            q.Enqueue(seed);
            seen.Add(seed);
            PathNode endpoint = seed;
            int bestDeg = CountHikNeighbors(seed, set);

            while (q.Count > 0)
            {
                var n = q.Dequeue();
                int deg = CountHikNeighbors(n, set);
                if (deg < bestDeg)
                {
                    bestDeg = deg;
                    endpoint = n;
                }

                var neighbors = n.Neighbors;
                if (neighbors == null)
                    continue;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    var other = neighbors[i];
                    if (other == null || !set.Contains(other) || !seen.Add(other))
                        continue;
                    q.Enqueue(other);
                }
            }

            return endpoint;
        }

        static List<Vector2> WalkComponent(
            PathNode start, HashSet<PathNode> set, HashSet<PathNode> visitedComponent)
        {
            var polyline = new List<Vector2>(64);
            AppendMap(start, polyline);
            visitedComponent.Add(start);

            var stack = new List<PathNode> { start };
            var used = new HashSet<(int, int)>();

            while (stack.Count > 0)
            {
                var u = stack[stack.Count - 1];
                PathNode next = null;
                var neighbors = u.Neighbors;
                if (neighbors != null)
                {
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        var v = neighbors[i];
                        if (v == null || !set.Contains(v))
                            continue;
                        if (!used.Add(EdgeKey(u, v)))
                            continue;
                        next = v;
                        break;
                    }
                }

                if (next != null)
                {
                    visitedComponent.Add(next);
                    AppendMap(next, polyline);
                    stack.Add(next);
                }
                else
                {
                    stack.RemoveAt(stack.Count - 1);
                    if (stack.Count > 0)
                        AppendMap(stack[stack.Count - 1], polyline);
                }
            }

            return polyline;
        }

        static int CountHikNeighbors(PathNode node, HashSet<PathNode> set)
        {
            int n = 0;
            var neighbors = node.Neighbors;
            if (neighbors == null)
                return 0;
            for (int i = 0; i < neighbors.Count; i++)
            {
                var other = neighbors[i];
                if (other != null && set.Contains(other))
                    n++;
            }

            return n;
        }

        static void AppendMap(PathNode node, List<Vector2> polyline)
        {
            var feature = node.GetFeature<HikvisionPathPointFeature>();
            if (feature == null)
                return;
            var p = new Vector2(feature.MapX, feature.MapY);
            if (polyline.Count > 0 && (polyline[polyline.Count - 1] - p).sqrMagnitude < 1e-6f)
                return;
            polyline.Add(p);
        }

        static (int, int) EdgeKey(PathNode a, PathNode b)
        {
            int ia = a.GetInstanceID();
            int ib = b.GetInstanceID();
            return ia <= ib ? (ia, ib) : (ib, ia);
        }
    }
}
