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
}
