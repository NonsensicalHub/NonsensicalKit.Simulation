using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送加工站点标签匹配与路径必经点查询。</summary>
    public static class ConveyorProcessStationUtility
    {
        public static bool HasRequiredProcessTags(WarehouseJob job) =>
            job?.RequiredProcessTags != null && job.RequiredProcessTags.Length > 0
            && !IsAllEmpty(job.RequiredProcessTags);

        public static bool NodeIsDwellProcessStation(in SimConveyorMapNode node) =>
            node.Kind == SimConveyorNodeKind.ProcessStation
            && node.ProcessMode == SimConveyorProcessMode.Dwell;

        public static bool TagsMatch(string requiredTag, string stationTag)
        {
            var a = requiredTag?.Trim();
            var b = stationTag?.Trim();
            return !string.IsNullOrEmpty(a)
                   && !string.IsNullOrEmpty(b)
                   && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static bool JobRequiresStationTag(WarehouseJob job, in SimConveyorMapNode stationNode)
        {
            if (!NodeIsDwellProcessStation(in stationNode)
                || job?.RequiredProcessTags == null
                || job.RequiredProcessTags.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < job.RequiredProcessTags.Length; i++)
            {
                if (TagsMatch(job.RequiredProcessTags[i], stationNode.ProcessTag))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool PathContainsAllRequiredStations(
            ConveyorMapTopology topology,
            IReadOnlyList<int> path,
            WarehouseJob job)
        {
            if (!HasRequiredProcessTags(job))
            {
                return true;
            }

            if (path == null || path.Count == 0)
            {
                return false;
            }

            for (var t = 0; t < job.RequiredProcessTags.Length; t++)
            {
                var tag = job.RequiredProcessTags[t]?.Trim();
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                var found = false;
                for (var i = 0; i < path.Count; i++)
                {
                    ref var node = ref topology.GetNode(path[i]);
                    if (NodeIsDwellProcessStation(in node) && TagsMatch(tag, node.ProcessTag))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        public static IReadOnlyList<int> GetDwellStationsWithTag(ConveyorMapTopology topology, string tag)
        {
            if (topology?.ProcessStationNodeIndices == null || string.IsNullOrWhiteSpace(tag))
            {
                return Array.Empty<int>();
            }

            var trimmed = tag.Trim();
            var matches = new List<int>();
            for (var i = 0; i < topology.ProcessStationNodeIndices.Count; i++)
            {
                var nodeIndex = topology.ProcessStationNodeIndices[i];
                ref var node = ref topology.GetNode(nodeIndex);
                if (NodeIsDwellProcessStation(in node) && TagsMatch(trimmed, node.ProcessTag))
                {
                    matches.Add(nodeIndex);
                }
            }

            return matches;
        }

        /// <summary>
        /// 点到点寻路或可达性是否允许走这条边。
        /// <paramref name="searchTarget"/> 为本次搜索终点；小于 0 表示可达性扫描，匹配标签的加工站可以穿过。
        /// </summary>
        public static bool CanTraverse(
            ConveyorMapTopology topology,
            int fromNodeIndex,
            int toNodeIndex,
            in ConveyorRouteAccess access,
            int searchTarget)
        {
            if (topology == null || !topology.TryGetEdge(fromNodeIndex, toNodeIndex, out var edge))
            {
                return false;
            }

            var edgeTag = edge.ProcessTag?.Trim();
            if (!string.IsNullOrEmpty(edgeTag) && !access.AllowsTag(edgeTag))
            {
                return false;
            }

            ref var node = ref topology.GetNode(toNodeIndex);
            if (!NodeIsDwellProcessStation(in node))
            {
                return true;
            }

            if (!access.AllowsTag(node.ProcessTag))
            {
                return false;
            }

            return searchTarget < 0 || toNodeIndex == searchTarget;
        }

        /// <summary>
        /// 根据所需加工标签，收集匹配停留加工站上「支持的入库口」逻辑 ID 的并集，并解析为入库口序号。
        /// </summary>
        /// <remarks>
        /// 约定：某加工站 <c>SupportedInfeedLogicalIds</c> 为空或未配置 → 该站不限制入库口（不向并集贡献约束）。
        /// 若所有匹配站均为空列表 → <paramref name="hasRestriction"/> 为 false，行为与旧版一致（可选任意入库口）。
        /// 若至少有一个匹配站配置了非空列表 → 只允许并集内的入库口；解析不到任何有效入库口时并集为空，表示无可选口。
        /// 直入任务（无加工标签）请勿调用，或调用后 <paramref name="hasRestriction"/> 为 false。
        /// </remarks>
        /// <returns>true 表示参数可用并已写入结果；false 表示 topology 无效。</returns>
        public static bool TryResolveSupportedInfeedPortUnion(
            ConveyorMapTopology topology,
            string[] requiredProcessTags,
            HashSet<int> allowedPortIndices,
            out bool hasRestriction)
        {
            hasRestriction = false;
            allowedPortIndices?.Clear();
            if (topology?.InfeedNodeIndices == null || allowedPortIndices == null)
            {
                return false;
            }

            if (requiredProcessTags == null
                || requiredProcessTags.Length == 0
                || IsAllEmpty(requiredProcessTags)
                || topology.ProcessStationNodeIndices == null)
            {
                return true;
            }

            var contributed = false;
            for (var t = 0; t < requiredProcessTags.Length; t++)
            {
                var tag = requiredProcessTags[t]?.Trim();
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                for (var i = 0; i < topology.ProcessStationNodeIndices.Count; i++)
                {
                    var nodeIndex = topology.ProcessStationNodeIndices[i];
                    ref var node = ref topology.GetNode(nodeIndex);
                    if (!NodeIsDwellProcessStation(in node) || !TagsMatch(tag, node.ProcessTag))
                    {
                        continue;
                    }

                    if (!TryCollectConfiguredInfeedLogicalIds(in node, out var ids) || ids.Count == 0)
                    {
                        continue;
                    }

                    contributed = true;
                    AddMatchingInfeedPorts(topology, ids, allowedPortIndices);
                }
            }

            hasRestriction = contributed;
            return true;
        }

        /// <summary>入库口序号是否允许用于带加工标签的任务（无限制时恒为 true）。</summary>
        public static bool IsInfeedPortAllowedForProcessTags(
            ConveyorMapTopology topology,
            string[] requiredProcessTags,
            int infeedPortIndex,
            HashSet<int> scratch)
        {
            if (scratch == null)
            {
                return true;
            }

            if (!TryResolveSupportedInfeedPortUnion(topology, requiredProcessTags, scratch, out var hasRestriction)
                || !hasRestriction)
            {
                return true;
            }

            return scratch.Contains(infeedPortIndex);
        }

        private static bool TryCollectConfiguredInfeedLogicalIds(in SimConveyorMapNode node, out List<string> ids)
        {
            ids = null;
            var raw = node.SupportedInfeedLogicalIds;
            if (raw == null || raw.Length == 0)
            {
                return false;
            }

            List<string> list = null;
            for (var i = 0; i < raw.Length; i++)
            {
                var id = raw[i]?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                list ??= new List<string>();
                var dup = false;
                for (var j = 0; j < list.Count; j++)
                {
                    if (string.Equals(list[j], id, StringComparison.OrdinalIgnoreCase))
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                {
                    list.Add(id);
                }
            }

            if (list == null || list.Count == 0)
            {
                return false;
            }

            ids = list;
            return true;
        }

        private static void AddMatchingInfeedPorts(
            ConveyorMapTopology topology,
            List<string> logicalIds,
            HashSet<int> allowedPortIndices)
        {
            var ports = topology.InfeedNodeIndices;
            for (var p = 0; p < ports.Count; p++)
            {
                ref var infeed = ref topology.GetNode(ports[p]);
                var infeedId = infeed.LogicalId?.Trim();
                if (string.IsNullOrEmpty(infeedId))
                {
                    continue;
                }

                for (var i = 0; i < logicalIds.Count; i++)
                {
                    if (string.Equals(infeedId, logicalIds[i], StringComparison.OrdinalIgnoreCase))
                    {
                        allowedPortIndices.Add(p);
                        break;
                    }
                }
            }
        }

        private static bool IsAllEmpty(string[] tags)
        {
            for (var i = 0; i < tags.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(tags[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>一次寻路允许使用的加工标签。无标签时不能走加工专用路段，也不能进入加工站。</summary>
    public readonly struct ConveyorRouteAccess
    {
        public static ConveyorRouteAccess Plain => default;

        private readonly string[] _tags;
        private readonly string _signature;

        private ConveyorRouteAccess(string[] tags, string signature)
        {
            _tags = tags;
            _signature = signature;
        }

        public string Signature => _signature ?? string.Empty;

        public bool HasTags => _tags != null && _tags.Length > 0;

        public static ConveyorRouteAccess FromJob(WarehouseJob job) =>
            FromTags(job?.RequiredProcessTags);

        public static ConveyorRouteAccess FromTags(string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return Plain;
            }

            var buffer = new string[tags.Length];
            var count = 0;
            for (var i = 0; i < tags.Length; i++)
            {
                var trimmed = tags[i]?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                var normalized = trimmed.ToLowerInvariant();
                var duplicate = false;
                for (var j = 0; j < count; j++)
                {
                    if (string.Equals(buffer[j], normalized, StringComparison.Ordinal))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    continue;
                }

                buffer[count++] = normalized;
            }

            if (count == 0)
            {
                return Plain;
            }

            if (count != buffer.Length)
            {
                Array.Resize(ref buffer, count);
            }

            Array.Sort(buffer, StringComparer.Ordinal);
            return new ConveyorRouteAccess(buffer, string.Join("|", buffer));
        }

        public bool AllowsTag(string tag)
        {
            var wanted = tag?.Trim();
            if (string.IsNullOrEmpty(wanted) || _tags == null)
            {
                return false;
            }

            for (var i = 0; i < _tags.Length; i++)
            {
                if (string.Equals(_tags[i], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
