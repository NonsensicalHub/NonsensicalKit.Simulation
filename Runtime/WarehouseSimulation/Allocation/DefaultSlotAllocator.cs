using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>
    /// 默认货位分配器：基于规则网格的 <c>bool[]</c> 占用位图。
    /// </summary>
    public sealed class DefaultSlotAllocator : ISlotAllocator
    {
        private WarehouseGridConfig _grid;
        private IWarehouseSimulationBindings _bindings;
        private SimSlotExclusionZone[] _exclusionZones = Array.Empty<SimSlotExclusionZone>();
        private bool[] _occupied;
        private int[] _freeCountByColumn;
        private int[] _columnOrderScratch;

        public bool[] Occupied => _occupied;

        public int TotalFreeCount { get; private set; }

        public int PhysicalSlotCount { get; private set; }

        public int StorageSlotCount { get; private set; }

        public string SlotLayoutDescription =>
            _grid != null
                ? $"{_grid.LevelCount} 层×{_grid.ColumnCount} 列×{_grid.RowCount} 排"
                : "—";

        public StackerSlotPlacementStrategy PlacementStrategy { get; private set; } =
            StackerSlotPlacementStrategy.NearestToPickup;

        public void Configure(WarehouseGridConfig grid) => _grid = grid;

        public void Reset(
            IWarehouseSimulationBindings bindings,
            bool[] initialOccupied = null,
            StackerSlotPlacementStrategy placementStrategy = StackerSlotPlacementStrategy.NearestToPickup)
        {
            if (_grid == null)
            {
                throw new InvalidOperationException("请先调用 Configure(WarehouseGridConfig) 配置网格。");
            }

            _bindings = bindings;
            PlacementStrategy = placementStrategy;
            _exclusionZones = SlotGridUtility.BuildEffectiveExclusionZones(_grid, bindings);
            PhysicalSlotCount = SlotGridUtility.CountPhysicalSlots(_grid);
            StorageSlotCount = SlotGridUtility.CountStorageSlots(_grid, bindings);
            _occupied = initialOccupied != null && initialOccupied.Length == PhysicalSlotCount
                ? (bool[])initialOccupied.Clone()
                : new bool[PhysicalSlotCount];
            MarkExcludedSlotsOccupied();
            EnsureColumnOrderScratch(_grid.ColumnCount);
            RebuildFreeCounts();
        }

        public int CountOccupiedStorageSlots(bool[] occupied) =>
            SlotGridUtility.CountOccupiedStorageSlots(_grid, occupied, _bindings);

        /// <summary>按占用率生成初始占用位图；占用率为 0 时返回 null。</summary>
        public static bool[] BuildInitialOccupancy(
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings hardware,
            float ratio,
            bool random,
            int randomSeed = 42)
        {
            if (ratio <= 0f)
            {
                return null;
            }

            ratio = Math.Min(1f, ratio);
            var occupied = new bool[SlotGridUtility.CountPhysicalSlots(grid)];
            var zones = SlotGridUtility.BuildEffectiveExclusionZones(grid, hardware);
            var storageIndices = CollectStorageSlotIndices(grid, zones);
            if (storageIndices.Count == 0)
            {
                return occupied;
            }

            var fillCount = Math.Clamp((int)Math.Round(storageIndices.Count * ratio), 0, storageIndices.Count);
            if (fillCount <= 0)
            {
                return occupied;
            }

            if (random)
            {
                FillRandomSlots(occupied, storageIndices, fillCount, randomSeed);
            }
            else
            {
                for (var i = 0; i < fillCount; i++)
                {
                    occupied[storageIndices[i]] = true;
                }
            }

            return occupied;
        }

        private static void FillRandomSlots(
            bool[] occupied,
            System.Collections.Generic.List<int> storageIndices,
            int fillCount,
            int randomSeed)
        {
            var order = new int[storageIndices.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            var rng = new Random(randomSeed);
            for (var i = order.Length - 1; i > 0; i--)
            {
                var swapIndex = rng.Next(i + 1);
                (order[i], order[swapIndex]) = (order[swapIndex], order[i]);
            }

            for (var i = 0; i < fillCount; i++)
            {
                occupied[storageIndices[order[i]]] = true;
            }
        }

        private static System.Collections.Generic.List<int> CollectStorageSlotIndices(
            WarehouseGridConfig grid,
            SimSlotExclusionZone[] zones)
        {
            var indices = new System.Collections.Generic.List<int>();
            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var col = 0; col < grid.ColumnCount; col++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        if (SlotGridUtility.IsStorageSlot(grid, level, col, row, zones))
                        {
                            indices.Add(ToFlatIndex(grid, level, col, row));
                        }
                    }
                }
            }

            return indices;
        }

        public bool TryAllocateSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            out GridIndex slot) =>
            PlacementStrategy switch
            {
                StackerSlotPlacementStrategy.FillColumnFirst => TryFillColumnFirst(
                    bindings, topology, servingStackerId, pickupColumn, out slot),
                _ => TryNearestToPickup(
                    bindings, topology, servingStackerId, pickupColumn, pickupRow, out slot),
            };

        public bool HasAllocatableFreeSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology) =>
            CountFreeAllocatableStorageSlots(bindings, topology) > 0;

        public int CountAllocatableStorageSlots(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology) =>
            CountAllocatableStorageSlotsInternal(bindings, topology, freeOnly: false);

        public int CountFreeAllocatableStorageSlots(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology) =>
            CountAllocatableStorageSlotsInternal(bindings, topology, freeOnly: true);

        public bool HasAllocatableFreeSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId)
        {
            if (_grid == null || TotalFreeCount <= 0 || _occupied == null)
            {
                return false;
            }

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (_freeCountByColumn[col] <= 0)
                {
                    continue;
                }

                if (!IsAllocatableColumnForStacker(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public void Occupy(GridIndex slot)
        {
            if (_grid == null || _occupied == null)
            {
                return;
            }

            var idx = ToFlatIndex(_grid, slot.Level, slot.Column, slot.Row);
            if (idx < 0 || idx >= _occupied.Length
                || _occupied[idx]
                || !SlotGridUtility.IsStorageSlot(_grid, slot.Level, slot.Column, slot.Row, _exclusionZones))
            {
                return;
            }

            _occupied[idx] = true;
            _freeCountByColumn[slot.Column]--;
            TotalFreeCount--;
        }

        public void Release(GridIndex slot)
        {
            if (_grid == null || _occupied == null)
            {
                return;
            }

            var idx = ToFlatIndex(_grid, slot.Level, slot.Column, slot.Row);
            if (idx < 0 || idx >= _occupied.Length
                || !_occupied[idx]
                || !SlotGridUtility.IsStorageSlot(_grid, slot.Level, slot.Column, slot.Row, _exclusionZones))
            {
                return;
            }

            _occupied[idx] = false;
            _freeCountByColumn[slot.Column]++;
            TotalFreeCount++;
        }

        public bool HasRetrievableOccupiedSlot(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology)
        {
            if (_grid == null || _occupied == null)
            {
                return false;
            }

            var stackerCount = Math.Max(1, bindings?.StackerCount ?? 1);
            for (var stackerId = 0; stackerId < stackerCount; stackerId++)
            {
                if (HasRetrievableOccupiedSlotForStacker(bindings, topology, stackerId, null))
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
            System.Collections.Generic.HashSet<GridIndex> reservedSlots,
            out GridIndex slot)
        {
            slot = default;
            if (_grid == null || _occupied == null)
            {
                return false;
            }

            var bestDistance = double.MaxValue;
            var found = false;
            var bestSlot = default(GridIndex);

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (!IsAllocatableColumnForStacker(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var candidate = new GridIndex(level, col, row, 0);
                        if (reservedSlots != null && reservedSlots.Contains(candidate))
                        {
                            continue;
                        }

                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_occupied[idx]
                            || !SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            continue;
                        }

                        var distance = ComputePickupDistance(
                            bindings, pickupColumn, pickupRow, col, row, level);
                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        bestSlot = candidate;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            slot = bestSlot;
            return true;
        }

        private bool HasRetrievableOccupiedSlotForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            System.Collections.Generic.HashSet<GridIndex> reservedSlots)
        {
            if (_grid == null || _occupied == null)
            {
                return false;
            }

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (!IsAllocatableColumnForStacker(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var candidate = new GridIndex(level, col, row, 0);
                        if (reservedSlots != null && reservedSlots.Contains(candidate))
                        {
                            continue;
                        }

                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (_occupied[idx]
                            && SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void EnsureColumnOrderScratch(int columnCount)
        {
            if (_columnOrderScratch == null || _columnOrderScratch.Length != columnCount)
            {
                _columnOrderScratch = new int[columnCount];
            }
        }

        private bool TryNearestToPickup(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            int pickupRow,
            out GridIndex slot)
        {
            slot = default;
            if (_grid == null || _occupied == null || TotalFreeCount <= 0)
            {
                return false;
            }

            var bestDistance = double.MaxValue;
            var found = false;
            var bestSlot = default(GridIndex);

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (_freeCountByColumn[col] <= 0)
                {
                    continue;
                }

                if (!IsAllocatableColumnForStacker(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (_occupied[idx]
                            || !SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            continue;
                        }

                        var distance = ComputePickupDistance(
                            bindings, pickupColumn, pickupRow, col, row, level);
                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        bestSlot = new GridIndex(level, col, row, 0);
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            slot = bestSlot;
            return true;
        }

        private bool TryFillColumnFirst(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int pickupColumn,
            out GridIndex slot)
        {
            slot = default;
            if (_grid == null || _occupied == null || TotalFreeCount <= 0)
            {
                return false;
            }

            EnsureColumnOrderScratch(_grid.ColumnCount);
            var columnOrder = _columnOrderScratch;
            for (var i = 0; i < _grid.ColumnCount; i++)
            {
                columnOrder[i] = i;
            }

            // 按与取货列的距离升序排列（无闭包分配的插入排序，列数通常 ≤ 50，O(n²) 可接受）
            for (var i = 1; i < _grid.ColumnCount; i++)
            {
                var key = columnOrder[i];
                var keyDist = Math.Abs(key - pickupColumn);
                var j = i - 1;
                while (j >= 0)
                {
                    var jDist = Math.Abs(columnOrder[j] - pickupColumn);
                    if (jDist < keyDist || (jDist == keyDist && columnOrder[j] <= key))
                    {
                        break;
                    }

                    columnOrder[j + 1] = columnOrder[j];
                    j--;
                }

                columnOrder[j + 1] = key;
            }

            for (var orderIndex = 0; orderIndex < columnOrder.Length; orderIndex++)
            {
                var col = columnOrder[orderIndex];
                if (_freeCountByColumn[col] <= 0)
                {
                    continue;
                }

                if (!IsAllocatableColumnForStacker(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_occupied[idx]
                            && SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            slot = new GridIndex(level, col, row, 0);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static double ComputePickupDistance(
            IWarehouseSimulationBindings bindings,
            int pickupColumn,
            int pickupRow,
            int column,
            int row,
            int level)
        {
            var from = new GridIndex(0, pickupColumn, pickupRow, 0);
            var to = new GridIndex(level, column, row, 0);
            return SlotPositionMath.ComputeManhattanDistance(bindings.SlotPositions, from, to);
        }

        private void RebuildFreeCounts()
        {
            if (_grid == null || _occupied == null)
            {
                TotalFreeCount = 0;
                _freeCountByColumn = Array.Empty<int>();
                return;
            }

            _freeCountByColumn = new int[_grid.ColumnCount];
            TotalFreeCount = 0;
            for (var row = 0; row < _grid.RowCount; row++)
            {
                for (var col = 0; col < _grid.ColumnCount; col++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_occupied[idx]
                            && SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            _freeCountByColumn[col]++;
                            TotalFreeCount++;
                        }
                    }
                }
            }
        }

        private void MarkExcludedSlotsOccupied()
        {
            if (_grid == null || _occupied == null)
            {
                return;
            }

            for (var row = 0; row < _grid.RowCount; row++)
            {
                for (var col = 0; col < _grid.ColumnCount; col++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        if (!SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            _occupied[ToFlatIndex(_grid, level, col, row)] = true;
                        }
                    }
                }
            }
        }

        private static bool IsAllocatableColumnForStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int servingStackerId,
            int column)
        {
            if (topology == null)
            {
                return true;
            }

            return StackerColumnReachUtility.TryGetDefinition(bindings, topology, servingStackerId, out var def)
                   && StackerColumnReachUtility.CanReachColumn(in def, column);
        }

        private int CountAllocatableStorageSlotsInternal(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            bool freeOnly)
        {
            if (_grid == null || _occupied == null)
            {
                return 0;
            }

            var count = 0;
            for (var row = 0; row < _grid.RowCount; row++)
            {
                for (var col = 0; col < _grid.ColumnCount; col++)
                {
                    if (!IsColumnAllocatableByAnyStacker(bindings, topology, col))
                    {
                        continue;
                    }

                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!SlotGridUtility.IsStorageSlot(_grid, level, col, row, _exclusionZones))
                        {
                            continue;
                        }

                        if (freeOnly && _occupied[idx])
                        {
                            continue;
                        }

                        count++;
                    }
                }
            }

            return count;
        }

        private static bool IsColumnAllocatableByAnyStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int column)
        {
            var stackerCount = Math.Max(1, bindings?.StackerCount ?? 1);
            for (var stackerId = 0; stackerId < stackerCount; stackerId++)
            {
                if (IsAllocatableColumnForStacker(bindings, topology, stackerId, column))
                {
                    return true;
                }
            }

            return false;
        }

        public static int ToFlatIndex(WarehouseGridConfig grid, int level, int column, int row) =>
            (row * grid.ColumnCount + column) * grid.LevelCount + level;
    }
}
