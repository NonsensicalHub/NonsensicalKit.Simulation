using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>
    /// 按堆垛机所属仓库把货位分配转到对应的 <see cref="DefaultSlotAllocator"/>。
    /// 每个子分配器只持有本库网格。
    /// </summary>
    public sealed class MultiWarehouseSlotAllocator : ISlotAllocator, IWarehouseGridLookup
    {
        private readonly SortedDictionary<int, DefaultSlotAllocator> _byWarehouse;
        private readonly Dictionary<int, string> _names;
        private bool[] _packedOccupied;
        private bool _packedDirty = true;

        private MultiWarehouseSlotAllocator(
            SortedDictionary<int, DefaultSlotAllocator> byWarehouse,
            Dictionary<int, string> names,
            StackerSlotPlacementStrategy placementStrategy)
        {
            _byWarehouse = byWarehouse;
            _names = names;
            PlacementStrategy = placementStrategy;
            var physical = 0;
            var storage = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                physical += allocator.PhysicalSlotCount;
                storage += allocator.StorageSlotCount;
            }

            PhysicalSlotCount = physical;
            StorageSlotCount = storage;
        }

        public static MultiWarehouseSlotAllocator Create(
            WarehouseSimScenario scenario,
            IReadOnlyList<SimWarehouseDefinition> warehouses)
        {
            var bindings = scenario.ResolvedHardwareBindings;
            var strategy = scenario.ResolvedStrategy.StackerPlacementStrategy;
            var byWarehouse = new SortedDictionary<int, DefaultSlotAllocator>();
            var names = new Dictionary<int, string>();
            for (var i = 0; i < warehouses.Count; i++)
            {
                var definition = warehouses[i];
                var name = string.IsNullOrWhiteSpace(definition.Name)
                    ? $"库{definition.WarehouseId}"
                    : definition.Name;
                AddWarehouse(
                    byWarehouse,
                    names,
                    scenario,
                    bindings,
                    definition.Grid,
                    definition.WarehouseId,
                    name,
                    strategy);
            }

            return new MultiWarehouseSlotAllocator(byWarehouse, names, strategy);
        }

        public bool[] Occupied
        {
            get
            {
                if (!_packedDirty && _packedOccupied != null)
                {
                    return _packedOccupied;
                }

                _packedOccupied = PackOccupied();
                _packedDirty = false;
                return _packedOccupied;
            }
        }

        public int TotalFreeCount
        {
            get
            {
                var total = 0;
                foreach (var allocator in _byWarehouse.Values)
                {
                    total += allocator.TotalFreeCount;
                }

                return total;
            }
        }

        public int PhysicalSlotCount { get; }

        public int StorageSlotCount { get; }

        public string SlotLayoutDescription
        {
            get
            {
                var sb = new StringBuilder();
                var first = true;
                foreach (var pair in _byWarehouse)
                {
                    if (!first)
                    {
                        sb.Append('；');
                    }

                    first = false;
                    var allocator = pair.Value;
                    var occupied = allocator.Occupied == null
                        ? 0
                        : allocator.CountOccupiedStorageSlots(allocator.Occupied);
                    sb.Append(_names.TryGetValue(pair.Key, out var name) ? name : $"库{pair.Key}");
                    sb.Append('：');
                    sb.Append(allocator.SlotLayoutDescription);
                    sb.Append("（占用 ");
                    sb.Append(occupied);
                    sb.Append('/');
                    sb.Append(allocator.StorageSlotCount);
                    sb.Append('）');
                }

                return sb.ToString();
            }
        }

        public StackerSlotPlacementStrategy PlacementStrategy { get; private set; }

        public bool TryGetGrid(int warehouseId, out WarehouseGridConfig grid)
        {
            if (_byWarehouse.TryGetValue(warehouseId, out var allocator))
            {
                return allocator.TryGetGrid(warehouseId, out grid);
            }

            grid = null;
            return false;
        }

        public void Reset(
            IWarehouseSimulationBindings bindings,
            bool[] initialOccupied = null,
            StackerSlotPlacementStrategy placementStrategy = StackerSlotPlacementStrategy.NearestToPickup)
        {
            PlacementStrategy = placementStrategy;
            _packedDirty = true;
            var offset = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                bool[] slice = null;
                var length = allocator.PhysicalSlotCount;
                if (initialOccupied != null && offset + length <= initialOccupied.Length)
                {
                    slice = new bool[length];
                    Array.Copy(initialOccupied, offset, slice, 0, length);
                }

                allocator.Reset(bindings, slice, placementStrategy);
                offset += length;
            }
        }

        public int CountOccupiedStorageSlots(bool[] occupied)
        {
            if (occupied == null
                || occupied.Length != PhysicalSlotCount
                || ReferenceEquals(occupied, _packedOccupied))
            {
                return CountLiveOccupiedStorageSlots();
            }

            var total = 0;
            var offset = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                var length = allocator.PhysicalSlotCount;
                var slice = new bool[length];
                Array.Copy(occupied, offset, slice, 0, length);
                total += allocator.CountOccupiedStorageSlots(slice);
                offset += length;
            }

            return total;
        }

        public bool TryAllocateSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            out GridIndex slot)
        {
            slot = default;
            return TryGetAllocator(bindings, topology, servingStackerId, out var allocator)
                   && allocator.TryAllocateSlotForStacker(
                       bindings, topology, servingStackerId, pickupColumn, pickupRow, out slot);
        }

        public bool HasAllocatableFreeSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology)
        {
            foreach (var allocator in _byWarehouse.Values)
            {
                if (allocator.HasAllocatableFreeSlot(bindings, topology))
                {
                    return true;
                }
            }

            return false;
        }

        public int CountAllocatableStorageSlots(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology) =>
            Sum(allocator => allocator.CountAllocatableStorageSlots(bindings, topology));

        public int CountFreeAllocatableStorageSlots(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology) =>
            Sum(allocator => allocator.CountFreeAllocatableStorageSlots(bindings, topology));

        public bool HasAllocatableFreeSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId) =>
            TryGetAllocator(bindings, topology, servingStackerId, out var allocator)
            && allocator.HasAllocatableFreeSlotForStacker(bindings, topology, servingStackerId);

        public bool TryOccupy(GridIndex slot)
        {
            if (!_byWarehouse.TryGetValue(slot.WarehouseId, out var allocator))
            {
                return false;
            }

            if (!allocator.TryOccupy(slot))
            {
                return false;
            }

            _packedDirty = true;
            return true;
        }

        public void Occupy(GridIndex slot) => TryOccupy(slot);

        public bool IsOccupied(GridIndex slot) =>
            _byWarehouse.TryGetValue(slot.WarehouseId, out var allocator) && allocator.IsOccupied(slot);

        public void Release(GridIndex slot)
        {
            if (_byWarehouse.TryGetValue(slot.WarehouseId, out var allocator))
            {
                allocator.Release(slot);
                _packedDirty = true;
            }
        }

        public bool HasRetrievableOccupiedSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology)
        {
            foreach (var allocator in _byWarehouse.Values)
            {
                if (allocator.HasRetrievableOccupiedSlot(bindings, topology))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TrySelectOccupiedSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            HashSet<GridIndex> reservedSlots,
            out GridIndex slot)
        {
            slot = default;
            return TryGetAllocator(bindings, topology, servingStackerId, out var allocator)
                   && allocator.TrySelectOccupiedSlotForStacker(
                       bindings,
                       topology,
                       servingStackerId,
                       pickupColumn,
                       pickupRow,
                       reservedSlots,
                       out slot);
        }

        private static void AddWarehouse(
            SortedDictionary<int, DefaultSlotAllocator> byWarehouse,
            Dictionary<int, string> names,
            WarehouseSimScenario scenario,
            IWarehouseSimulationBindings bindings,
            WarehouseGridConfig grid,
            int warehouseId,
            string name,
            StackerSlotPlacementStrategy strategy)
        {
            var allocator = new DefaultSlotAllocator();
            allocator.Configure(grid, warehouseId);
            var initialOccupied = DefaultSlotAllocator.BuildInitialOccupancy(
                grid,
                bindings,
                scenario.InitialOccupancyRatio,
                scenario.InitialOccupancyRandom,
                scenario.FlowRandomSeed,
                warehouseId);
            allocator.Reset(bindings, initialOccupied, strategy);
            byWarehouse[warehouseId] = allocator;
            names[warehouseId] = name;
        }

        private bool[] PackOccupied()
        {
            if (PhysicalSlotCount <= 0)
            {
                return Array.Empty<bool>();
            }

            var packed = new bool[PhysicalSlotCount];
            var offset = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                var length = allocator.PhysicalSlotCount;
                var child = allocator.Occupied;
                if (child != null && child.Length > 0 && offset < packed.Length)
                {
                    Array.Copy(child, 0, packed, offset, Math.Min(child.Length, length));
                }

                offset += length;
            }

            return packed;
        }

        private int CountLiveOccupiedStorageSlots()
        {
            var live = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                if (allocator.Occupied != null)
                {
                    live += allocator.CountOccupiedStorageSlots(allocator.Occupied);
                }
            }

            return live;
        }

        private bool TryGetAllocator(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId,
            out DefaultSlotAllocator allocator)
        {
            var warehouseId = StackerColumnReachUtility.GetWarehouseId(bindings, topology, stackerId);
            return _byWarehouse.TryGetValue(warehouseId, out allocator);
        }

        private int Sum(Func<DefaultSlotAllocator, int> select)
        {
            var total = 0;
            foreach (var allocator in _byWarehouse.Values)
            {
                total += select(allocator);
            }

            return total;
        }
    }
}
