using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>解析堆垛机主取货点位置，供货位放置策略使用。</summary>
    public static class StackerPickupAnchorResolver
    {
        /// <summary>取该堆垛机在输送地图上第一个取货点的列/排；若无则退回巷道中心列。</summary>
        public static bool TryGetPrimaryPickupAnchor(
            ConveyorMapTopology topology,
            IStackerFleetDescriptor fleet,
            int stackerId,
            out int pickupColumn,
            out int pickupRow)
        {
            pickupColumn = 0;
            pickupRow = 0;

            if (topology?.PickupNodeIndices != null)
            {
                for (var i = 0; i < topology.PickupNodeIndices.Count; i++)
                {
                    var nodeIndex = topology.PickupNodeIndices[i];
                    ref var node = ref topology.GetNode(nodeIndex);
                    if (node.StackerId != stackerId
                        || !StackerInteractionModeUtility.AllowsInbound(in node))
                    {
                        continue;
                    }

                    pickupColumn = SimConveyorNodeBinding.ResolvePickupColumn(node, fleet, topology);
                    pickupRow = node.PickupRow;
                    return true;
                }
            }

            if (StackerColumnReachUtility.TryGetDefinition(fleet, topology, stackerId, out var def))
            {
                pickupColumn = StackerColumnReachUtility.GetAisleCenterColumn(in def);
                return true;
            }

            return false;
        }
    }
}
