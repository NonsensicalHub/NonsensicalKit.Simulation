using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 为到货任务选择入库口与货位：入库口仅作物理入口，堆垛机按当前负载动态选取。
    /// </summary>
    public static class InfeedPortSelector
    {
        // 仿真在单线程执行，静态缓冲可安全复用，避免每次放货决策产生 GC 压力。
        private static int[] _stackerLoadsBuf = new int[4];
        private static int[] _stackerOrderBuf = new int[4];
        /// <summary>按策略在可用入库口中选取下一口。</summary>
        public static int SelectInfeedPort(
            ConveyorMapTopology topology,
            WarehouseSimStrategyProfile strategy,
            int[] infeedReservationCounts,
            bool[] infeedCargoOccupied,
            int maxReservationsPerPort,
            ref int roundRobinCursor)
        {
            if (topology == null
                || infeedReservationCounts == null
                || topology.InfeedNodeIndices.Count == 0)
            {
                return -1;
            }

            strategy ??= SimStrategyDefaults.Instance;
            var portCount = topology.InfeedNodeIndices.Count;
            var maxReservations = maxReservationsPerPort > 0 ? maxReservationsPerPort : 1;

            return PortSelectionUtility.SelectPort(
                strategy.InfeedPortSelectionStrategy,
                portCount,
                ref roundRobinCursor,
                port => IsPortAvailable(port, infeedReservationCounts, infeedCargoOccupied, maxReservations),
                port => PortSelectionUtility.GetQueueLoadWithOccupancyPenalty(
                    port, infeedReservationCounts, infeedCargoOccupied));
        }

        /// <summary>指定入库口放货时，按负载选取堆垛机与货位（须能规划输送路径）。</summary>
        public static bool TrySelectSlotForInfeedPort(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts,
            int infeedPortIndex,
            out GridIndex slot,
            out int stackerId,
            bool skipPickupCapacityPrecheck = false)
        {
            slot = default;
            stackerId = -1;
            var cursor = 0;
            if (!TrySelectBestPlacement(
                    topology,
                    bindings,
                    slotAllocator,
                    pickupReservationCounts,
                    stackerActiveJobCounts,
                    fixedInfeedPortIndex: infeedPortIndex,
                    ref cursor,
                    out _,
                    out slot,
                    out stackerId,
                    skipPickupCapacityPrecheck))
            {
                return false;
            }

            return true;
        }

        /// <summary>指定入库口是否仍能分配到可通过输送网到达的空货位。</summary>
        public static bool HasRoutableAllocatableSlotForInfeedPort(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts,
            int infeedPortIndex)
        {
            if (infeedPortIndex < 0)
            {
                return false;
            }

            var cursor = 0;
            return TrySelectBestPlacement(
                topology,
                bindings,
                slotAllocator,
                pickupReservationCounts,
                stackerActiveJobCounts,
                fixedInfeedPortIndex: infeedPortIndex,
                ref cursor,
                out _,
                out _,
                out _);
        }

        /// <summary>是否存在任一入库口仍可分配到可通过输送网到达的空货位。</summary>
        public static bool HasRoutableAllocatableSlotForAnyInfeedPort(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts)
        {
            if (topology?.InfeedNodeIndices == null
                || bindings == null
                || slotAllocator == null
                || slotAllocator.TotalFreeCount <= 0)
            {
                return false;
            }

            return ConveyorPathPlanner.HasOpenPickupRouteFromAnyInfeed(
                topology,
                bindings,
                pickupReservationCounts);
        }

        /// <summary>按策略顺序填充当前可尝试放货的入库口（不含物理占用口），写入 scratch 前部并返回数量。</summary>
        public static int FillCandidateInfeedPorts(
            ConveyorMapTopology topology,
            WarehouseSimStrategyProfile strategy,
            int[] infeedReservationCounts,
            bool[] infeedCargoOccupied,
            int maxReservationsPerPort,
            int roundRobinCursor,
            int[] portOrderScratch)
        {
            if (topology == null
                || infeedReservationCounts == null
                || portOrderScratch == null
                || topology.InfeedNodeIndices.Count == 0)
            {
                return 0;
            }

            strategy ??= SimStrategyDefaults.Instance;
            var portCount = topology.InfeedNodeIndices.Count;
            var maxReservations = maxReservationsPerPort > 0 ? maxReservationsPerPort : 1;

            return PortSelectionUtility.FillCandidatePorts(
                strategy.InfeedPortSelectionStrategy,
                portCount,
                roundRobinCursor,
                portOrderScratch,
                port => IsPortAvailable(port, infeedReservationCounts, infeedCargoOccupied, maxReservations),
                port => PortSelectionUtility.GetQueueLoadWithOccupancyPenalty(
                    port, infeedReservationCounts, infeedCargoOccupied));
        }

        private static bool TrySelectBestPlacement(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ISlotAllocator slotAllocator,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts,
            int fixedInfeedPortIndex,
            ref int roundRobinCursor,
            out int infeedPortIndex,
            out GridIndex slot,
            out int stackerId,
            bool skipPickupCapacityPrecheck = false)
        {
            infeedPortIndex = fixedInfeedPortIndex;
            slot = default;
            stackerId = -1;

            if (topology == null
                || bindings == null
                || slotAllocator == null
                || fixedInfeedPortIndex < 0)
            {
                return false;
            }

            if (slotAllocator.TotalFreeCount <= 0)
            {
                return false;
            }

            if (!skipPickupCapacityPrecheck
                && !ConveyorPathPlanner.HasOpenPickupRouteFromInfeed(
                    topology,
                    bindings,
                    fixedInfeedPortIndex,
                    pickupReservationCounts))
            {
                return false;
            }

            var stackerCount = Math.Max(1, bindings.StackerCount);
            if (_stackerLoadsBuf.Length < stackerCount)
            {
                _stackerLoadsBuf = new int[stackerCount];
                _stackerOrderBuf = new int[stackerCount];
            }

            for (var i = 0; i < stackerCount; i++)
            {
                _stackerOrderBuf[i] = i;
                _stackerLoadsBuf[i] = ComputeStackerLoad(
                    i, bindings, topology, pickupReservationCounts, stackerActiveJobCounts);
            }

            // 按负载从低到高插入排序（无闭包分配；堆垛机数量通常 ≤ 4，O(n²) 可接受）
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

            var bestStackerId = -1;
            var bestSlot = default(GridIndex);

            for (var orderIndex = 0; orderIndex < stackerCount; orderIndex++)
            {
                var candidateStackerId = _stackerOrderBuf[orderIndex];
                if (!StackerColumnReachUtility.TryGetDefinition(
                        bindings, topology, candidateStackerId, out var stackerDef))
                {
                    continue;
                }

                var anchorCol = StackerColumnReachUtility.GetAisleCenterColumn(in stackerDef);
                if (!slotAllocator.HasAllocatableFreeSlotForStacker(bindings, topology, candidateStackerId))
                {
                    continue;
                }

                if (!StackerPickupAnchorResolver.TryGetPrimaryPickupAnchor(
                        topology, bindings, candidateStackerId, out var pickupCol, out var pickupRow))
                {
                    pickupCol = anchorCol;
                    pickupRow = 0;
                }

                if (!slotAllocator.TryAllocateSlotForStacker(
                        bindings,
                        topology,
                        candidateStackerId,
                        pickupCol,
                        pickupRow,
                        out var candidateSlot))
                {
                    continue;
                }

                if (!ConveyorPathPlanner.CanRouteFromInfeed(
                        topology,
                        bindings,
                        fixedInfeedPortIndex,
                        candidateSlot,
                        pickupReservationCounts))
                {
                    continue;
                }

                bestStackerId = candidateStackerId;
                bestSlot = candidateSlot;
                break;
            }

            if (bestStackerId < 0)
            {
                return false;
            }

            slot = bestSlot;
            stackerId = bestStackerId;
            return true;
        }

        private static bool IsPortAvailable(
            int port,
            int[] infeedReservationCounts,
            bool[] infeedCargoOccupied,
            int maxReservations)
        {
            if (port >= infeedReservationCounts.Length || infeedReservationCounts[port] >= maxReservations)
            {
                return false;
            }

            return infeedCargoOccupied == null
                   || port >= infeedCargoOccupied.Length
                   || !infeedCargoOccupied[port];
        }

        /// <summary>堆垛机负载 = 取货点预定数 + 已分货位且在途任务数。</summary>
        internal static int ComputeStackerLoad(
            int stackerId,
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int[] pickupReservationCounts,
            int[] stackerActiveJobCounts)
        {
            var load = 0;

            if (pickupReservationCounts != null && topology?.PickupNodeIndices != null)
            {
                for (var i = 0; i < topology.PickupNodeIndices.Count; i++)
                {
                    var pickupIndex = topology.PickupNodeIndices[i];
                    ref var pickup = ref topology.GetNode(pickupIndex);
                    if (pickup.StackerId != stackerId)
                    {
                        continue;
                    }

                    if (pickupIndex >= 0 && pickupIndex < pickupReservationCounts.Length)
                    {
                        load += pickupReservationCounts[pickupIndex];
                    }
                }
            }

            if (stackerActiveJobCounts != null
                && stackerId >= 0
                && stackerId < stackerActiveJobCounts.Length)
            {
                load += stackerActiveJobCounts[stackerId];
            }

            return load;
        }
    }
}
