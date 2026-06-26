using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>垂直提升机节点参数解析。</summary>
    public static class ConveyorVerticalTransferUtility
    {
        public static bool NodeIsVerticalTransfer(in SimConveyorMapNode node) =>
            node.Kind == SimConveyorNodeKind.VerticalTransfer;

        public static string ResolveTransferGroupId(in SimConveyorMapNode node, int nodeIndex)
        {
            var group = node.TransferGroupId?.Trim();
            if (!string.IsNullOrEmpty(group))
            {
                return group;
            }

            return SimEntityNaming.FormatLogicalId(in node, nodeIndex);
        }

        public static float ResolveTransferSeconds(in SimConveyorMapNode node, ISimResourcePolicy policy)
        {
            if (node.TransferSeconds > 0f)
            {
                return node.TransferSeconds;
            }

            return policy?.VerticalTransferSeconds ?? 15f;
        }

        /// <summary>路径规划用：进入该节点时除边输送外额外计入的停留/服务时间。</summary>
        public static float GetNodeArrivalExtraSeconds(
            ConveyorMapTopology topology,
            int nodeIndex,
            ISimResourcePolicy policy,
            WarehouseJob job = null)
        {
            if (topology?.Map?.Nodes == null
                || nodeIndex < 0
                || nodeIndex >= topology.Map.Nodes.Length
                || policy == null)
            {
                return 0f;
            }

            ref var node = ref topology.GetNode(nodeIndex);
            if (node.Kind == SimConveyorNodeKind.VerticalTransfer)
            {
                return ResolveTransferSeconds(in node, policy);
            }

            if (node.Kind == SimConveyorNodeKind.ProcessStation
                && job != null
                && ConveyorProcessStationUtility.JobRequiresStationTag(job, in node))
            {
                return node.ProcessServiceSeconds > 0f
                    ? node.ProcessServiceSeconds
                    : policy.ProcessStationServiceSeconds;
            }

            return 0f;
        }

        public static bool TryResolveTargetNodeIndex(
            ConveyorMapTopology topology,
            in SimConveyorMapNode node,
            int nodeIndex,
            out int targetNodeIndex)
        {
            targetNodeIndex = -1;
            if (topology?.Map?.Nodes == null)
            {
                return false;
            }

            var targetLogicalId = node.TransferTargetLogicalId?.Trim();
            if (string.IsNullOrEmpty(targetLogicalId))
            {
                return false;
            }

            for (var i = 0; i < topology.Map.Nodes.Length; i++)
            {
                if (topology.Map.Nodes[i].LogicalId?.Trim() == targetLogicalId)
                {
                    targetNodeIndex = i;
                    return true;
                }
            }

            return false;
        }
    }
}
