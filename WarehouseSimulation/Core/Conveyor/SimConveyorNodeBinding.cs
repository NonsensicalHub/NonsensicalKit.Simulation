using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送地图节点与堆垛机巷道列域的绑定解析。</summary>
    public static class SimConveyorNodeBinding
    {
        /// <summary>已单独配置相对巷道中心的伸叉距离。</summary>
        public static bool HasForkDepth(in SimConveyorMapNode node) =>
            Math.Abs(node.ForkDepthMeters) > 0.0001f;

        /// <summary>解析取货点货位列（节点显式配置优先，否则由堆垛机巷道推导左侧取货列）。</summary>
        public static int ResolvePickupColumn(
            in SimConveyorMapNode node,
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology)
        {
            // PickupColumn==0 在「已绑定堆垛机且配置了伸叉深度」时表示真实的第 0 列，
            // 不能再当成未配置（否则堆垛机 8 / 列 0 会走错推导）。
            if (node.PickupColumn > 0
                || (node.PickupColumn == 0 && node.StackerId >= 0 && HasForkDepth(in node)))
            {
                return node.PickupColumn;
            }

            if (node.StackerId >= 0
                && StackerColumnReachUtility.TryGetDefinition(fleet, topology, node.StackerId, out var def))
            {
                return StackerColumnReachUtility.GetEntrancePickupColumn(in def, 0);
            }

            return 0;
        }

        /// <summary>由取货列与堆垛机巷道左列推导取货点侧别（0=左，1=右），用于资源互斥 ID。</summary>
        public static int ResolvePickupIndexOnStacker(
            in SimConveyorMapNode node,
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology)
        {
            var pickupColumn = ResolvePickupColumn(node, fleet, topology);
            if (pickupColumn <= 0)
            {
                return 0;
            }

            if (node.StackerId >= 0
                && StackerColumnReachUtility.TryGetDefinition(fleet, topology, node.StackerId, out var def))
            {
                return StackerColumnReachUtility.DerivePickupIndexOnStacker(pickupColumn, in def);
            }

            var reach = fleet != null
                ? fleet.DefaultStackerColumnReach
                : SimStackerColumnReach.TwoColumns;
            var aisleLeft = StackerColumnReachUtility.InferAisleLeftColumnFromPickupColumn(
                pickupColumn, reach, 0);
            return StackerColumnReachUtility.DerivePickupIndexOnStacker(pickupColumn, aisleLeft, reach);
        }

        /// <summary>解析取货点所属堆垛机巷道左列（用于轨道定位与列域钳制）。</summary>
        public static int ResolvePickupAisleLeftColumn(
            in SimConveyorMapNode node,
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology)
        {
            if (node.StackerId >= 0
                && StackerColumnReachUtility.TryGetDefinition(fleet, topology, node.StackerId, out var def))
            {
                var pickupColumn = ResolvePickupColumn(node, fleet, topology);
                // 交互参考列可落在巷道左右浅位，含单向机货位对侧（如堆垛机 8：货位列 0、交互列 1）。
                // 只要参考列合法，轨道仍用定义巷道左列，不能按取货列反推（否则 OneColumn 会把 rail 推成 1）。
                if (StackerColumnReachUtility.CanUsePickupReferenceColumn(in def, pickupColumn))
                {
                    return def.AisleLeftColumn;
                }

                var pickupIndex = ResolvePickupIndexOnStacker(node, fleet, topology);
                return StackerColumnReachUtility.InferAisleLeftColumnFromPickupColumn(
                    pickupColumn, in def, pickupIndex);
            }

            if (node.PickupColumn > 0)
            {
                var reach = fleet != null
                    ? fleet.DefaultStackerColumnReach
                    : SimStackerColumnReach.TwoColumns;
                var pickupIndex = ResolvePickupIndexOnStacker(node, fleet, topology);
                return StackerColumnReachUtility.InferAisleLeftColumnFromPickupColumn(
                    node.PickupColumn, reach, pickupIndex);
            }

            return 0;
        }
    }
}
