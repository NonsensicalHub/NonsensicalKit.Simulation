using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>从回放事件读取仿真记录的输送路径。</summary>
    public static class SimPlaybackConveyorPathResolver
    {
        public static bool TryResolvePath(
            SimPlaybackEvent evt,
            out IReadOnlyList<int> pathNodeIndices,
            out int pickupPointIndex,
            out int stackerId,
            out string error)
        {
            pathNodeIndices = evt.PathNodeIndices;
            pickupPointIndex = evt.PickupPointIndex;
            stackerId = evt.StackerId;

            if (pathNodeIndices != null && pathNodeIndices.Count >= 2)
            {
                error = null;
                return true;
            }

            var nodeCount = pathNodeIndices?.Count ?? 0;
            error =
                $"回放事件缺少仿真路径 PathNodeIndices " +
                $"(job={evt.JobId} phase={evt.Phase} t={evt.SimTime:F2}s 节点数={nodeCount})，请重新运行仿真。";
            return false;
        }

        public static string FormatNodePath(WarehouseConveyorMap map, IReadOnlyList<int> pathNodeIndices)
        {
            if (map?.Nodes == null || pathNodeIndices == null || pathNodeIndices.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[pathNodeIndices.Count];
            for (var i = 0; i < pathNodeIndices.Count; i++)
            {
                var idx = pathNodeIndices[i];
                parts[i] = idx >= 0 && idx < map.Nodes.Length
                    ? SimEntityNaming.FormatLogicalId(map, idx)
                    : $"node-{idx}";
            }

            return string.Join(" →", parts);
        }

        public static string FormatRouteDetail(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            IReadOnlyList<int> pathNodeIndices,
            int pickupPointIndex,
            int infeedPortIndex,
            int stackerId,
            GridIndex targetSlot)
        {
            if (map?.Nodes == null || pathNodeIndices == null || pathNodeIndices.Count == 0)
            {
                return "路径为空。";
            }

            var sb = new StringBuilder(256);
            sb.AppendLine(
                $"job 路径 | infeed={SimEntityNaming.FormatInfeedPort(map, topology, infeedPortIndex)} " +
                $"pickup={SimEntityNaming.FormatPickupPoint(map, pickupPointIndex)} " +
                $"stacker={SimEntityNaming.FormatStacker(stackerId)} " +
                $"slot={targetSlot} 节点数{pathNodeIndices.Count}");
            sb.Append("  路线: ");
            sb.AppendLine(FormatNodePath(map, pathNodeIndices));

            for (var i = 1; i < pathNodeIndices.Count; i++)
            {
                var from = pathNodeIndices[i - 1];
                var to = pathNodeIndices[i];
                var transit = topology != null ? topology.GetEdgeTransit(from, to) : 0f;
                sb.AppendLine(
                    $"  [{i}] {DescribeNode(map, from)} --{transit:F2}s--> {DescribeNode(map, to)}");
            }

            if (topology != null)
            {
                sb.AppendLine($"  预估总耗时: {topology.EstimatePathSeconds(pathNodeIndices):F2}s");
            }

            return sb.ToString().TrimEnd();
        }

        private static string DescribeNode(WarehouseConveyorMap map, int nodeIndex)
        {
            if (map?.Nodes == null || nodeIndex < 0 || nodeIndex >= map.Nodes.Length)
            {
                return $"#{nodeIndex}";
            }

            var node = map.Nodes[nodeIndex];
            var id = SimEntityNaming.FormatLogicalId(in node, nodeIndex);
            return $"{id} ({node.Kind})";
        }
    }
}
