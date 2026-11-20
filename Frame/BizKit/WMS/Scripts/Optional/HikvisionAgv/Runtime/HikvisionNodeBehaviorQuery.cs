using System.Collections.Generic;
using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace HikvisionAgv
{
    /// <summary>
    /// 在路网中查找挂了 <see cref="HikvisionNodeBehaviorFeature"/> 的最近节点。
    /// 使用按网络缓存的候选列表，避免每次 Status 扫全表。
    /// </summary>
    public static class HikvisionNodeBehaviorQuery
    {
        struct Entry
        {
            public PathNode Node;
            public HikvisionNodeBehaviorFeature Feature;
        }

        sealed class NetworkCache
        {
            public int NodeCount;
            public readonly List<Entry> Rotatable = new List<Entry>(64);
            public readonly List<Entry> ForkLift = new List<Entry>(64);
            public readonly List<Entry> AllBehavior = new List<Entry>(64);
        }

        static readonly Dictionary<int, NetworkCache> s_caches = new Dictionary<int, NetworkCache>();

        /// <summary>路网结构或点位行为变更后调用，强制重建候选列表。</summary>
        public static void Invalidate(PathNetwork network)
        {
            if (network == null)
                return;
            s_caches.Remove(network.GetInstanceID());
        }

        public static void InvalidateAll() => s_caches.Clear();

        public static bool TryFindNearest(
            PathNetwork network,
            Vector3 worldPosition,
            float maxDistance,
            out PathNode node,
            bool requireInPlaceRotate = false,
            bool requireForkLift = false)
        {
            return TryFindNearest(
                network, worldPosition, maxDistance, requireInPlaceRotate, requireForkLift,
                out node, out _, out _);
        }

        public static bool TryFindNearest(
            PathNetwork network,
            Vector3 worldPosition,
            float maxDistance,
            out PathNode node,
            out HikvisionNodeBehaviorFeature feature,
            out float distance,
            bool requireInPlaceRotate = false,
            bool requireForkLift = false)
        {
            return TryFindNearest(
                network, worldPosition, maxDistance, requireInPlaceRotate, requireForkLift,
                out node, out feature, out distance);
        }

        /// <summary>
        /// 在 XZ 平面上查找最近「点位行为」节点；仅当距离不超过 <paramref name="maxDistance"/> 时成功。
        /// <paramref name="requireInPlaceRotate"/> / <paramref name="requireForkLift"/> 为 true 时只匹配对应开关已勾选的点。
        /// <paramref name="maxDistance"/> ≤ 0 时直接失败。
        /// </summary>
        public static bool TryFindNearest(
            PathNetwork network,
            Vector3 worldPosition,
            float maxDistance,
            bool requireInPlaceRotate,
            bool requireForkLift,
            out PathNode node,
            out HikvisionNodeBehaviorFeature feature,
            out float distance)
        {
            node = null;
            feature = null;
            distance = 0f;

            if (network == null || maxDistance <= 0f)
                return false;

            var cache = GetOrBuildCache(network);
            List<Entry> list;
            if (requireInPlaceRotate)
                list = cache.Rotatable;
            else if (requireForkLift)
                list = cache.ForkLift;
            else
                list = cache.AllBehavior;

            if (list.Count == 0)
                return false;

            float maxDistSq = maxDistance * maxDistance;
            float bestDistSq = float.PositiveInfinity;
            PathNode best = null;
            HikvisionNodeBehaviorFeature bestFeature = null;

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                var candidate = entry.Node;
                if (candidate == null)
                    continue;

                // 双条件同时要求时再过滤（当前调用一般只开一个）
                if (requireInPlaceRotate && requireForkLift)
                {
                    var f = entry.Feature;
                    if (f == null || !f.AllowInPlaceRotate || !f.EnableForkLift)
                        continue;
                }

                Vector3 delta = candidate.Position - worldPosition;
                delta.y = 0f;
                float distSq = delta.sqrMagnitude;
                if (distSq > maxDistSq || distSq >= bestDistSq)
                    continue;

                bestDistSq = distSq;
                best = candidate;
                bestFeature = entry.Feature;
            }

            if (best == null)
                return false;

            node = best;
            feature = bestFeature;
            distance = Mathf.Sqrt(bestDistSq);
            return true;
        }

        /// <summary>供 Scene 可视化：遍历缓存中的全部点位行为条目。</summary>
        public static void ForEachBehavior(
            PathNetwork network,
            System.Action<PathNode, HikvisionNodeBehaviorFeature> visitor)
        {
            if (network == null || visitor == null)
                return;

            var cache = GetOrBuildCache(network);
            for (int i = 0; i < cache.AllBehavior.Count; i++)
            {
                var e = cache.AllBehavior[i];
                if (e.Node != null && e.Feature != null)
                    visitor(e.Node, e.Feature);
            }
        }

        static NetworkCache GetOrBuildCache(PathNetwork network)
        {
            int id = network.GetInstanceID();
            var nodes = network.Nodes;
            int count = nodes != null ? nodes.Count : 0;

            // 编辑器下每次重建，避免改 Feature 开关后缓存过期
            if (!Application.isPlaying)
            {
                var fresh = BuildCache(network, count);
                s_caches[id] = fresh;
                return fresh;
            }

            if (!s_caches.TryGetValue(id, out var cache) || cache.NodeCount != count)
            {
                cache = BuildCache(network, count);
                s_caches[id] = cache;
            }

            return cache;
        }

        static NetworkCache BuildCache(PathNetwork network, int nodeCount)
        {
            var cache = new NetworkCache { NodeCount = nodeCount };
            var nodes = network.Nodes;
            if (nodes == null || nodes.Count == 0)
                return cache;

            for (int i = 0; i < nodes.Count; i++)
            {
                var candidate = nodes[i];
                if (candidate == null)
                    continue;

                var f = candidate.GetFeature<HikvisionNodeBehaviorFeature>();
                if (f == null)
                    continue;

                var entry = new Entry { Node = candidate, Feature = f };
                cache.AllBehavior.Add(entry);
                if (f.AllowInPlaceRotate)
                    cache.Rotatable.Add(entry);
                if (f.EnableForkLift)
                    cache.ForkLift.Add(entry);
            }

            return cache;
        }
    }
}
