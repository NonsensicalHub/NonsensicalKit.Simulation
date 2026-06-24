using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>为出库任务选择源货位、堆垛机、堆垛机交互点与出库口。</summary>
    public static class OutfeedPortSelector
    {
        // 仿真在单线程执行，静态缓冲可安全复用，避免每次出库决策产生 GC 压力。
        private static int[] _stackerLoadsBuf = new int[4];
        private static int[] _stackerOrderBuf = new int[4];
        public static int SelectOutfeedPort(
            ConveyorMapTopology topology,
            WarehouseSimStrategyProfile strategy,
            int[] outfeedReservationCounts,
            bool[] outfeedCargoOccupied,
            IWarehouseSimulationBindings bindings,
            ref int roundRobinCursor)
        {
            if (topology?.OutfeedNodeIndices == null || topology.OutfeedNodeIndices.Count == 0)
            {
                return -1;
            }

            strategy ??= SimStrategyDefaults.Instance;
            var portCount = topology.OutfeedNodeIndices.Count;

            return PortSelectionUtility.SelectPort(
                strategy.InfeedPortSelectionStrategy,
                portCount,
                ref roundRobinCursor,
                port => IsPortAvailable(port, topology, bindings, outfeedReservationCounts, outfeedCargoOccupied),
                port => PortSelectionUtility.GetQueueLoadWithOccupancyPenalty(
                    port, outfeedReservationCounts, outfeedCargoOccupied));
        }

        public static int GetMaxReservationsPerPort(
            int port,
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings)
        {
            var globalDefault = bindings.MaxOutfeedQueuePerPort > 0
                ? bindings.MaxOutfeedQueuePerPort
                : 3;
            if (topology?.OutfeedNodeIndices == null
                || port < 0
                || port >= topology.OutfeedNodeIndices.Count)
            {
                return globalDefault;
            }

            var nodeIndex = topology.OutfeedNodeIndices[port];
            ref var node = ref topology.GetNode(nodeIndex);
            return node.MaxReservations > 0 ? node.MaxReservations : globalDefault;
        }

        public static int FillCandidateOutfeedPorts(
            ConveyorMapTopology topology,
            WarehouseSimStrategyProfile strategy,
            int[] outfeedReservationCounts,
            bool[] outfeedCargoOccupied,
            IWarehouseSimulationBindings bindings,
            int roundRobinCursor,
            int[] portOrderScratch)
        {
            if (topology?.OutfeedNodeIndices == null
                || outfeedReservationCounts == null
                || portOrderScratch == null
                || bindings == null
                || topology.OutfeedNodeIndices.Count == 0)
            {
                return 0;
            }

            strategy ??= SimStrategyDefaults.Instance;
            var portCount = topology.OutfeedNodeIndices.Count;

            return PortSelectionUtility.FillCandidatePorts(
                strategy.InfeedPortSelectionStrategy,
                portCount,
                roundRobinCursor,
                portOrderScratch,
                port => IsPortAvailable(port, topology, bindings, outfeedReservationCounts, outfeedCargoOccupied),
                port => PortSelectionUtility.GetQueueLoadWithOccupancyPenalty(
                    port, outfeedReservationCounts, outfeedCargoOccupied));
        }

        public static bool TrySelectOutboundPlacementForOutfeedPort(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts,
            System.Collections.Generic.HashSet<GridIndex> reservedSlots,
            int outfeedPortIndex,
            out GridIndex slot,
            out int stackerId,
            out int pickupPointIndex)
        {
            slot = default;
            stackerId = -1;
            pickupPointIndex = -1;

            if (topology == null
                || bindings == null
                || slotAllocator == null
                || outfeedPortIndex < 0
                || topology.OutfeedNodeIndices == null
                || outfeedPortIndex >= topology.OutfeedNodeIndices.Count)
            {
                return false;
            }

            var stackerCount = Math.Max(1, bindings.StackerCount);
            EnsureStackerBufs(stackerCount);
            for (var i = 0; i < stackerCount; i++)
            {
                _stackerOrderBuf[i] = i;
                _stackerLoadsBuf[i] = InfeedPortSelector.ComputeStackerLoad(
                    i, bindings, topology, pickupReservationCounts, stackerActiveJobCounts);
            }

            SortStackerOrderByLoad(stackerCount);

            for (var orderIndex = 0; orderIndex < stackerCount; orderIndex++)
            {
                var candidateStackerId = _stackerOrderBuf[orderIndex];
                if (bindings.UseStackerReservation
                    && stackerActiveJobCounts != null
                    && candidateStackerId >= 0
                    && candidateStackerId < stackerActiveJobCounts.Length
                    && stackerActiveJobCounts[candidateStackerId] > 0)
                {
                    continue;
                }

                if (!StackerColumnReachUtility.TryGetDefinition(
                        bindings, topology, candidateStackerId, out _))
                {
                    continue;
                }

                if (!StackerPickupAnchorResolver.TryGetPrimaryPickupAnchor(
                        topology, bindings, candidateStackerId, out var pickupCol, out var pickupRow))
                {
                    pickupCol = 0;
                    pickupRow = 0;
                }

                if (!slotAllocator.TrySelectOccupiedSlotForStacker(
                        bindings,
                        topology,
                        candidateStackerId,
                        pickupCol,
                        pickupRow,
                        reservedSlots,
                        out var candidateSlot))
                {
                    continue;
                }

                var pickup = ResolvePickupForStacker(topology, candidateStackerId);
                if (pickup < 0
                    || !ConveyorPathPlanner.CanRouteFromPickupToOutfeed(topology, pickup, outfeedPortIndex))
                {
                    continue;
                }

                stackerId = candidateStackerId;
                slot = candidateSlot;
                pickupPointIndex = pickup;
                return true;
            }

            return false;
        }

        public static bool TrySelectOutboundPlacement(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts,
            System.Collections.Generic.HashSet<GridIndex> reservedSlots,
            out GridIndex slot,
            out int stackerId,
            out int pickupPointIndex,
            out int outfeedPortIndex)
        {
            slot = default;
            stackerId = -1;
            pickupPointIndex = -1;
            outfeedPortIndex = -1;

            if (topology == null || bindings == null || slotAllocator == null)
            {
                return false;
            }

            var stackerCount = Math.Max(1, bindings.StackerCount);
            EnsureStackerBufs(stackerCount);
            for (var i = 0; i < stackerCount; i++)
            {
                _stackerOrderBuf[i] = i;
                _stackerLoadsBuf[i] = InfeedPortSelector.ComputeStackerLoad(
                    i, bindings, topology, pickupReservationCounts, stackerActiveJobCounts);
            }

            SortStackerOrderByLoad(stackerCount);

            for (var orderIndex = 0; orderIndex < stackerCount; orderIndex++)
            {
                var candidateStackerId = _stackerOrderBuf[orderIndex];
                if (bindings.UseStackerReservation
                    && stackerActiveJobCounts != null
                    && candidateStackerId >= 0
                    && candidateStackerId < stackerActiveJobCounts.Length
                    && stackerActiveJobCounts[candidateStackerId] > 0)
                {
                    continue;
                }

                if (!StackerColumnReachUtility.TryGetDefinition(
                        bindings, topology, candidateStackerId, out _))
                {
                    continue;
                }

                if (!StackerPickupAnchorResolver.TryGetPrimaryPickupAnchor(
                        topology, bindings, candidateStackerId, out var pickupCol, out var pickupRow))
                {
                    pickupCol = 0;
                    pickupRow = 0;
                }

                if (!slotAllocator.TrySelectOccupiedSlotForStacker(
                        bindings,
                        topology,
                        candidateStackerId,
                        pickupCol,
                        pickupRow,
                        reservedSlots,
                        out var candidateSlot))
                {
                    continue;
                }

                var pickup = ResolvePickupForStacker(topology, candidateStackerId);
                if (pickup < 0
                    || !ConveyorPathPlanner.CanRouteToOutfeed(topology, pickup))
                {
                    continue;
                }

                stackerId = candidateStackerId;
                slot = candidateSlot;
                pickupPointIndex = pickup;
                outfeedPortIndex = -1;
                return true;
            }

            return false;
        }

        private static void EnsureStackerBufs(int stackerCount)
        {
            if (_stackerLoadsBuf.Length < stackerCount)
            {
                _stackerLoadsBuf = new int[stackerCount];
                _stackerOrderBuf = new int[stackerCount];
            }
        }

        private static void SortStackerOrderByLoad(int stackerCount)
        {
            for (var i = 1; i < stackerCount; i++)
            {
                var key = _stackerOrderBuf[i];
                var keyLoad = _stackerLoadsBuf[key];
                var j = i - 1;
                while (j >= 0)
                {
                    var jLoad = _stackerLoadsBuf[_stackerOrderBuf[j]];
                    if (jLoad < keyLoad || (jLoad == keyLoad && _stackerOrderBuf[j] <= key))
                    {
                        break;
                    }

                    _stackerOrderBuf[j + 1] = _stackerOrderBuf[j];
                    j--;
                }

                _stackerOrderBuf[j + 1] = key;
            }
        }

        private static int ResolvePickupForStacker(ConveyorMapTopology topology, int stackerId)
        {
            if (topology?.PickupNodeIndices == null)
            {
                return -1;
            }

            for (var i = 0; i < topology.PickupNodeIndices.Count; i++)
            {
                var pickupIndex = topology.PickupNodeIndices[i];
                ref var node = ref topology.GetNode(pickupIndex);
                if (node.StackerId != stackerId
                    || !StackerInteractionModeUtility.AllowsOutbound(in node))
                {
                    continue;
                }

                return pickupIndex;
            }

            return -1;
        }

        private static bool IsPortAvailable(
            int port,
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] outfeedReservationCounts,
            bool[] outfeedCargoOccupied)
        {
            if (outfeedReservationCounts == null || port >= outfeedReservationCounts.Length)
            {
                return false;
            }

            if (outfeedReservationCounts[port] >= GetMaxReservationsPerPort(port, topology, bindings))
            {
                return false;
            }

            return outfeedCargoOccupied == null
                   || port >= outfeedCargoOccupied.Length
                   || !outfeedCargoOccupied[port];
        }
    }
}
