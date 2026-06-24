using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 输送路径择优（静态工具，无仿真状态）。
    /// <para>对每个可达取货点尝试寻路，按 <see cref="ConveyorRoutingStrategy"/> 评分选最优；</para>
    /// <para>堆垛机移动时间估算见 <see cref="ComputeMoveSecondsFrom"/>。</para>
    /// </summary>
    public static class ConveyorPathPlanner
    {
        #region 公开 API

        /// <summary>从指定入库口出发：在可取货点中按策略选最优路径。</summary>
        public static bool TrySelectBestPath(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            int[] pickupReservationCounts,
            int infeedPortIndex,
            GridIndex targetSlot,
            int preferredStackerId,
            double routePlanTime,
            out int pickupPointIndex,
            out int stackerId,
            out List<int> pathNodeIndices)
        {
            pickupPointIndex = -1;
            stackerId = -1;
            pathNodeIndices = null;

            if (topology == null || bindings == null || infeedPortIndex < 0
                || infeedPortIndex >= topology.InfeedNodeIndices.Count)
            {
                return false;
            }

            strategy ??= SimStrategyDefaults.Instance;

            var infeedNode = topology.InfeedNodeIndices[infeedPortIndex];
            var bestPickup = -1;
            var bestScore = float.MaxValue;
            var bestReservations = int.MaxValue;
            var bestSameStacker = false;
            List<int> bestPath = null;

            foreach (var pickupNode in topology.PickupNodeIndices)
            {
                ref var pickupNodeData = ref topology.GetNode(pickupNode);
                if (!StackerInteractionModeUtility.AllowsInbound(in pickupNodeData))
                {
                    continue;
                }

                if (!IsPickupAvailable(topology, bindings, pickupReservationCounts, pickupNode))
                {
                    continue;
                }

                var pt = topology.GetNode(pickupNode);
                if (!StackerColumnReachUtility.TryGetDefinition(bindings, topology, pt.StackerId, out var stackerDef)
                    || !StackerColumnReachUtility.CanReachSlot(stackerDef, targetSlot.Column))
                {
                    continue;
                }

                var sameStacker = preferredStackerId >= 0 && pt.StackerId == preferredStackerId;
                if (strategy.ConveyorRoutingStrategy == ConveyorRoutingStrategy.PreferAssignedStacker
                    && preferredStackerId >= 0
                    && !sameStacker)
                {
                    continue;
                }

                if (!TryFindPathForStrategy(
                        topology,
                        bindings,
                        strategy,
                        reservations,
                        infeedNode,
                        pickupNode,
                        routePlanTime,
                        out var candidatePath)
                    || candidatePath == null
                    || candidatePath.Count < 2)
                {
                    continue;
                }

                var score = ScorePath(topology, bindings, strategy, reservations, candidatePath, routePlanTime);
                var reservationsCount = GetPickupReservationCount(pickupReservationCounts, pickupNode);
                if (!IsBetterPathCandidate(
                        strategy,
                        score,
                        sameStacker,
                        pickupNode,
                        reservationsCount,
                        bestScore,
                        bestPickup,
                        bestReservations,
                        bestSameStacker))
                {
                    continue;
                }

                bestScore = score;
                bestPickup = pickupNode;
                bestReservations = reservationsCount;
                bestSameStacker = sameStacker;
                bestPath = candidatePath;
            }

            if (strategy.ConveyorRoutingStrategy == ConveyorRoutingStrategy.PreferAssignedStacker
                && preferredStackerId >= 0
                && bestPickup < 0)
            {
                return TrySelectBestPath(
                    topology,
                    bindings,
                    SimStrategyDefaults.Instance,
                    reservations,
                    pickupReservationCounts,
                    infeedPortIndex,
                    targetSlot,
                    preferredStackerId: -1,
                    routePlanTime,
                    out pickupPointIndex,
                    out stackerId,
                    out pathNodeIndices);
            }

            if (bestPickup < 0 || bestPath == null)
            {
                return false;
            }

            pickupPointIndex = bestPickup;
            stackerId = topology.GetNode(bestPickup).StackerId;
            pathNodeIndices = bestPath;
            return true;
        }

        /// <summary>入库口是否存在仍有预定余量的可达取货点（不扫描货位网格）。</summary>
        public static bool HasOpenPickupRouteFromInfeed(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int infeedPortIndex,
            int[] pickupReservationCounts)
        {
            if (topology == null
                || bindings == null
                || infeedPortIndex < 0
                || infeedPortIndex >= topology.InfeedNodeIndices.Count)
            {
                return false;
            }

            var infeedNode = topology.InfeedNodeIndices[infeedPortIndex];
            var reachablePickups = topology.GetReachablePickupNodes(infeedNode);
            for (var i = 0; i < reachablePickups.Count; i++)
            {
                if (IsPickupAvailable(topology, bindings, pickupReservationCounts, reachablePickups[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否存在任一入库口仍有预定余量的可达取货点。</summary>
        public static bool HasOpenPickupRouteFromAnyInfeed(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] pickupReservationCounts)
        {
            if (topology?.InfeedNodeIndices == null)
            {
                return false;
            }

            for (var port = 0; port < topology.InfeedNodeIndices.Count; port++)
            {
                if (HasOpenPickupRouteFromInfeed(topology, bindings, port, pickupReservationCounts))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否存在可达取货点与路径；传入 <paramref name="pickupReservationCounts"/> 时同时要求取货点预定未满。</summary>
        public static bool CanRouteFromInfeed(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int infeedPortIndex,
            GridIndex targetSlot,
            int[] pickupReservationCounts = null)
        {
            if (topology == null || bindings == null || infeedPortIndex < 0
                || infeedPortIndex >= topology.InfeedNodeIndices.Count)
            {
                return false;
            }

            var infeedNode = topology.InfeedNodeIndices[infeedPortIndex];
            foreach (var pickupNode in topology.PickupNodeIndices)
            {
                ref var pickupNodeData = ref topology.GetNode(pickupNode);
                if (!StackerInteractionModeUtility.AllowsInbound(in pickupNodeData))
                {
                    continue;
                }

                if (pickupReservationCounts != null
                    && !IsPickupAvailable(topology, bindings, pickupReservationCounts, pickupNode))
                {
                    continue;
                }

                var pt = topology.GetNode(pickupNode);
                if (!StackerColumnReachUtility.TryGetDefinition(bindings, topology, pt.StackerId, out var stackerDef)
                    || !StackerColumnReachUtility.CanReachSlot(stackerDef, targetSlot.Column))
                {
                    continue;
                }

                if (topology.TryFindShortestPathByTransit(infeedNode, pickupNode, out var path)
                    && path != null
                    && path.Count >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>是否存在拓扑可达的取货点与路径（忽略取货点预定是否已满）。</summary>
        public static bool CanRouteFromInfeedIgnoringPickupCapacity(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int infeedPortIndex,
            GridIndex targetSlot) =>
            CanRouteFromInfeed(topology, bindings, infeedPortIndex, targetSlot);

        /// <summary>从指定取货点出发：在可达出库口中按策略选最优路径。</summary>
        public static bool TrySelectBestOutboundPath(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            int[] outfeedReservationCounts,
            int pickupPointIndex,
            int preferredOutfeedPortIndex,
            double routePlanTime,
            out int outfeedPortIndex,
            out List<int> pathNodeIndices)
        {
            outfeedPortIndex = -1;
            pathNodeIndices = null;

            if (topology == null
                || bindings == null
                || pickupPointIndex < 0
                || topology.OutfeedNodeIndices == null
                || topology.OutfeedNodeIndices.Count == 0)
            {
                return false;
            }

            ref var pickupNodeData = ref topology.GetNode(pickupPointIndex);
            if (!StackerInteractionModeUtility.AllowsOutbound(in pickupNodeData))
            {
                return false;
            }

            strategy ??= SimStrategyDefaults.Instance;

            var bestOutfeed = -1;
            var bestScore = float.MaxValue;
            var bestReservations = int.MaxValue;
            List<int> bestPath = null;

            if (preferredOutfeedPortIndex >= 0
                && preferredOutfeedPortIndex < topology.OutfeedNodeIndices.Count)
            {
                var outfeedNode = topology.OutfeedNodeIndices[preferredOutfeedPortIndex];
                // 任务建单时已计入本任务预定，此处用 <= 而非 <，避免 count==max 时无法提交输送路径。
                if (IsOutfeedAvailableForAssignedJob(topology, bindings, outfeedReservationCounts, preferredOutfeedPortIndex)
                    && TryFindPathForStrategy(
                        topology, bindings, strategy, reservations,
                        pickupPointIndex, outfeedNode, routePlanTime, out var path)
                    && path != null && path.Count >= 2)
                {
                    outfeedPortIndex = preferredOutfeedPortIndex;
                    pathNodeIndices = path;
                    return true;
                }
            }

            for (var port = 0; port < topology.OutfeedNodeIndices.Count; port++)
            {
                if (!IsOutfeedAvailable(topology, bindings, outfeedReservationCounts, port))
                {
                    continue;
                }

                var outfeedNode = topology.OutfeedNodeIndices[port];
                if (!TryFindPathForStrategy(
                        topology, bindings, strategy, reservations,
                        pickupPointIndex, outfeedNode, routePlanTime, out var candidatePath)
                    || candidatePath == null
                    || candidatePath.Count < 2)
                {
                    continue;
                }

                var score = ScorePath(topology, bindings, strategy, reservations, candidatePath, routePlanTime);
                var reservationsCount = GetOutfeedReservationCount(outfeedReservationCounts, port);
                if (score > bestScore + 1e-6f
                    || (Math.Abs(score - bestScore) <= 1e-6f && reservationsCount >= bestReservations))
                {
                    continue;
                }

                bestScore = score;
                bestOutfeed = port;
                bestReservations = reservationsCount;
                bestPath = candidatePath;
            }

            if (bestOutfeed < 0 || bestPath == null)
            {
                return false;
            }

            outfeedPortIndex = bestOutfeed;
            pathNodeIndices = bestPath;
            return true;
        }

        /// <summary>是否存在从取货点到任一出库口的路径。</summary>
        public static bool CanRouteToOutfeed(
            ConveyorMapTopology topology,
            int pickupPointIndex) =>
            topology != null
            && topology.OutfeedNodeIndices != null
            && topology.OutfeedNodeIndices.Count > 0
            && TryFindFirstRoutableOutfeedPort(topology, pickupPointIndex, out _);

        /// <summary>是否存在从取货点到指定出库口的路径。</summary>
        public static bool CanRouteFromPickupToOutfeed(
            ConveyorMapTopology topology,
            int pickupPointIndex,
            int outfeedPortIndex)
        {
            if (topology == null
                || pickupPointIndex < 0
                || topology.OutfeedNodeIndices == null
                || outfeedPortIndex < 0
                || outfeedPortIndex >= topology.OutfeedNodeIndices.Count)
            {
                return false;
            }

            ref var pickupNodeData = ref topology.GetNode(pickupPointIndex);
            if (!StackerInteractionModeUtility.AllowsOutbound(in pickupNodeData))
            {
                return false;
            }

            var outfeedNode = topology.OutfeedNodeIndices[outfeedPortIndex];
            return topology.TryFindShortestPathByTransit(pickupPointIndex, outfeedNode, out var path)
                   && path != null
                   && path.Count >= 2;
        }

        private static bool TryFindFirstRoutableOutfeedPort(
            ConveyorMapTopology topology,
            int pickupPointIndex,
            out int outfeedPortIndex)
        {
            outfeedPortIndex = -1;
            if (topology == null
                || pickupPointIndex < 0
                || topology.OutfeedNodeIndices == null
                || topology.OutfeedNodeIndices.Count == 0)
            {
                return false;
            }

            ref var pickupNodeData = ref topology.GetNode(pickupPointIndex);
            if (!StackerInteractionModeUtility.AllowsOutbound(in pickupNodeData))
            {
                return false;
            }

            for (var port = 0; port < topology.OutfeedNodeIndices.Count; port++)
            {
                if (!CanRouteFromPickupToOutfeed(topology, pickupPointIndex, port))
                {
                    continue;
                }

                outfeedPortIndex = port;
                return true;
            }

            return false;
        }

        /// <summary>对固定起终点做加权最短路（距离 + 拥堵 + 在途箱数）。</summary>
        public static bool TryFindLowestWeightPath(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            int fromNodeIndex,
            int toNodeIndex,
            double atTime,
            out List<int> pathNodeIndices)
        {
            pathNodeIndices = null;
            if (topology == null || bindings == null || reservations == null)
            {
                return false;
            }

            return topology.TryFindLowestWeightPath(
                fromNodeIndex,
                toNodeIndex,
                (from, to) => ComputeEdgeWeight(topology, bindings, strategy, reservations, from, to, atTime),
                out pathNodeIndices,
                out _);
        }

        /// <summary>按堆垛机运动学估算移动时间（仅层/排；列向伸叉不计入）。</summary>
        public static float ComputeMoveSecondsFrom(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId,
            int railColumn,
            int fromRow,
            int fromLevel,
            GridIndex slot,
            bool isLoaded = false)
        {
            var kinematics = StackerKinematicsResolver.Resolve(bindings, topology, stackerId);
            return StackerKinematicsUtility.ComputeMoveSeconds(
                kinematics,
                bindings.SlotPositions,
                railColumn,
                fromRow,
                fromLevel,
                slot,
                isLoaded);
        }

        #endregion

        #region 寻路与评分（内部）

        private static bool TryFindPathForStrategy(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            int fromNodeIndex,
            int toNodeIndex,
            double atTime,
            out List<int> pathNodeIndices)
        {
            if (strategy.ConveyorRoutingStrategy == ConveyorRoutingStrategy.LeastCongestion
                && reservations != null)
            {
                return TryFindLowestWeightPath(
                    topology,
                    bindings,
                    strategy,
                    reservations,
                    fromNodeIndex,
                    toNodeIndex,
                    atTime,
                    out pathNodeIndices);
            }

            pathNodeIndices = null;
            if (!topology.TryFindShortestPathByTransit(fromNodeIndex, toNodeIndex, out pathNodeIndices)
                || pathNodeIndices == null
                || pathNodeIndices.Count < 2)
            {
                return false;
            }

            return true;
        }

        private static float ScorePath(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            List<int> path,
            double atTime)
        {
            if (strategy.ConveyorRoutingStrategy == ConveyorRoutingStrategy.LeastCongestion
                && reservations != null
                && path != null
                && path.Count >= 2)
            {
                var total = 0f;
                for (var i = 1; i < path.Count; i++)
                {
                    total += ComputeEdgeWeight(topology, bindings, strategy, reservations, path[i - 1], path[i], atTime);
                    if (total >= float.MaxValue * 0.5f)
                    {
                        return float.MaxValue;
                    }
                }

                return total;
            }

            return topology.EstimatePathSeconds(path);
        }

        private static bool IsBetterPathCandidate(
            WarehouseSimStrategyProfile strategy,
            float score,
            bool sameStacker,
            int pickupNode,
            int reservations,
            float bestScore,
            int bestPickup,
            int bestReservations,
            bool bestSameStacker)
        {
            if (strategy.ConveyorRoutingStrategy == ConveyorRoutingStrategy.PreferAssignedStacker)
            {
                if (sameStacker != bestSameStacker)
                {
                    return sameStacker;
                }
            }

            if (score > bestScore + 1e-6f)
            {
                return false;
            }

            if (score < bestScore - 1e-6f)
            {
                return true;
            }

            if (reservations < bestReservations)
            {
                return true;
            }

            if (reservations > bestReservations)
            {
                return false;
            }

            return bestPickup < 0 || pickupNode < bestPickup;
        }

        /// <summary>
        /// 当前取货点是否仍可接受新任务。
        /// 默认行为：限制同一取货点在“输送中 + 取货点等待（尚未进入堆垛机阶段）”的预约数量不超过
        /// <paramref name="maxReservations"/>。
        /// 若后续需要改为“按堆垛机维度统计”，可在此处遍历同一 StackerId 的所有取货点求和再对比。
        /// </summary>
        private static bool IsPickupAvailable(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] pickupReservationCounts,
            int pickupNode)
        {
            if (pickupReservationCounts == null
                || pickupNode < 0
                || pickupNode >= pickupReservationCounts.Length)
            {
                return true;
            }

            var globalDefault = bindings.MaxPickupReservationsPerPoint > 0
                ? bindings.MaxPickupReservationsPerPoint
                : 2;
            ref var node = ref topology.GetNode(pickupNode);
            var maxReservations = node.MaxReservations > 0 ? node.MaxReservations : globalDefault;
            return pickupReservationCounts[pickupNode] < maxReservations;
        }

        private static int GetPickupReservationCount(int[] pickupReservationCounts, int pickupNode)
        {
            if (pickupReservationCounts == null
                || pickupNode < 0
                || pickupNode >= pickupReservationCounts.Length)
            {
                return 0;
            }

            return pickupReservationCounts[pickupNode];
        }

        private static float ComputeEdgeWeight(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            WarehouseSimStrategyProfile strategy,
            ReservationTable reservations,
            int fromNode,
            int toNode,
            double atTime)
        {
            if (!topology.TryGetEdge(fromNode, toNode, out var edge))
            {
                return float.MaxValue;
            }

            strategy ??= SimStrategyDefaults.Instance;
            var distance = Math.Max(0f, edge.DistanceMeters);
            var capacity = topology.Map.GetEdgeCapacity(edge);
            var slotIds = ConveyorMapMath.BuildSegmentSlotIds(topology.Map, fromNode, toNode, capacity);
            var active = reservations.CountActiveAmong(slotIds, atTime);
            var congestedCargo = active >= capacity ? (float)active : 0f;
            var totalCargo = (float)active;

            return strategy.RouteDistanceWeight * distance
                   + strategy.RouteCongestedCargoWeight * congestedCargo
                   + strategy.RouteTotalCargoWeight * totalCargo;
        }

        private static bool IsOutfeedAvailable(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] outfeedReservationCounts,
            int port)
        {
            if (outfeedReservationCounts == null || port < 0 || port >= outfeedReservationCounts.Length)
            {
                return true;
            }

            var maxReservations = ResolveOutfeedMaxReservations(topology, bindings, port);
            return outfeedReservationCounts[port] < maxReservations;
        }

        /// <summary>任务已预定该出库口时：计数含本任务，允许 count == max。</summary>
        private static bool IsOutfeedAvailableForAssignedJob(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] outfeedReservationCounts,
            int port)
        {
            if (outfeedReservationCounts == null || port < 0 || port >= outfeedReservationCounts.Length)
            {
                return true;
            }

            var maxReservations = ResolveOutfeedMaxReservations(topology, bindings, port);
            return outfeedReservationCounts[port] <= maxReservations;
        }

        /// <summary>解析出库口前排队上限：节点自身配置（&gt;0）优先，否则回退 Profile 全局值。</summary>
        private static int ResolveOutfeedMaxReservations(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int port)
        {
            var globalDefault = bindings.MaxOutfeedQueuePerPort > 0
                ? bindings.MaxOutfeedQueuePerPort
                : 3;
            if (topology?.OutfeedNodeIndices == null || port >= topology.OutfeedNodeIndices.Count)
            {
                return globalDefault;
            }

            var nodeIndex = topology.OutfeedNodeIndices[port];
            ref var node = ref topology.GetNode(nodeIndex);
            return node.MaxReservations > 0 ? node.MaxReservations : globalDefault;
        }

        private static int GetOutfeedReservationCount(int[] outfeedReservationCounts, int port)
        {
            if (outfeedReservationCounts == null || port < 0 || port >= outfeedReservationCounts.Length)
            {
                return 0;
            }

            return outfeedReservationCounts[port];
        }

        #endregion
    }
}

