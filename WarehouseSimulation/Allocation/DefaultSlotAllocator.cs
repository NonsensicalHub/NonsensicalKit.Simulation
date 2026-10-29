using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>
    /// 默认货位分配器：基于规则网格的 <c>bool[]</c> 占用位图。
    /// 四列双深同一层、同一排先放远离巷道的深位，深位落稳后才放浅位，避免货叉伸到深位时撞上浅位货物。
    /// </summary>
    public sealed class DefaultSlotAllocator : ISlotAllocator, IWarehouseGridLookup, IWarehouseStorageProbe
    {
        private WarehouseGridConfig _grid;
        private int _warehouseId;
        private IWarehouseSimulationBindings _bindings;
        private SimSlotExclusionZone[] _exclusionZones = Array.Empty<SimSlotExclusionZone>();
        private bool[] _occupied;
        /// <summary>已预占、入库货叉尚未放完。深位在此期间不能打开同格浅位。</summary>
        private bool[] _inboundPlacementPending;
        private int[] _freeCountByColumn;
        private int[] _columnOrderScratch;
        private bool[] _isStorage;
        private bool[] _slotHasPos;
        private float[] _slotX;
        private float[] _slotY;
        private float[] _slotZ;
        private bool _slotPosReady;
        private bool[] _colBounded;
        private float[] _colMinX;
        private float[] _colMaxX;
        private float[] _colMinY;
        private float[] _colMaxY;
        private float[] _colMinZ;
        private float[] _colMaxZ;
        private bool[][] _reachableByStacker;

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

        public void Configure(WarehouseGridConfig grid, int warehouseId = 0)
        {
            _grid = grid;
            _warehouseId = warehouseId;
        }

        public bool HasFreeStorage(int warehouseId) =>
            warehouseId == _warehouseId && TotalFreeCount > 0;

        public bool HasStoredCargo(int warehouseId) =>
            warehouseId == _warehouseId && TotalFreeCount < StorageSlotCount;

        public bool TryGetGrid(int warehouseId, out WarehouseGridConfig grid)
        {
            if (warehouseId == _warehouseId && _grid != null)
            {
                grid = _grid;
                return true;
            }

            grid = null;
            return false;
        }

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
            _exclusionZones = SlotGridUtility.BuildEffectiveExclusionZones(
                _grid, bindings, _warehouseId, restrictToWarehouse: true);
            PhysicalSlotCount = SlotGridUtility.CountPhysicalSlots(_grid);
            StorageSlotCount = SlotGridUtility.CountStorageSlots(_grid, _exclusionZones);
            _occupied = initialOccupied != null && initialOccupied.Length == PhysicalSlotCount
                ? (bool[])initialOccupied.Clone()
                : new bool[PhysicalSlotCount];
            _inboundPlacementPending = new bool[PhysicalSlotCount];
            BuildStorageMask();
            MarkExcludedSlotsOccupied();
            EnsureColumnOrderScratch(_grid.ColumnCount);
            RebuildFreeCounts();
            _slotPosReady = false;
            _reachableByStacker = null;
        }

        public int CountOccupiedStorageSlots(bool[] occupied) =>
            SlotGridUtility.CountOccupiedStorageSlots(_grid, occupied, _exclusionZones);

        /// <summary>按占用率生成初始占用位图；占用率为 0 时返回 null。</summary>
        public static bool[] BuildInitialOccupancy(
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings hardware,
            float ratio,
            bool random,
            int randomSeed = 42,
            int warehouseId = 0)
        {
            if (ratio <= 0f)
            {
                return null;
            }

            ratio = Math.Min(1f, ratio);
            var occupied = new bool[SlotGridUtility.CountPhysicalSlots(grid)];
            var zones = SlotGridUtility.BuildEffectiveExclusionZones(
                grid, hardware, warehouseId, restrictToWarehouse: true);
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

            FillRespectingDoubleDepth(
                occupied,
                grid,
                storageIndices,
                zones,
                fillCount,
                random,
                MixOccupancySeed(randomSeed, warehouseId),
                hardware,
                warehouseId);
            return occupied;
        }

        /// <summary>
        /// 先占深位和没有深位配对的货位，再占浅位。
        /// 浅位的深位还空着时不预占，避免开局库存把货叉通道堵住。
        /// </summary>
        private static void FillRespectingDoubleDepth(
            bool[] occupied,
            WarehouseGridConfig grid,
            System.Collections.Generic.List<int> storageIndices,
            SimSlotExclusionZone[] zones,
            int fillCount,
            bool random,
            int randomSeed,
            IWarehouseSimulationBindings bindings,
            int warehouseId)
        {
            var primary = new System.Collections.Generic.List<int>(storageIndices.Count);
            var nearSlots = new System.Collections.Generic.List<int>();
            for (var i = 0; i < storageIndices.Count; i++)
            {
                var index = storageIndices[i];
                if (IsNearSlotWaitingForFar(grid, zones, bindings, warehouseId, index))
                {
                    nearSlots.Add(index);
                }
                else
                {
                    primary.Add(index);
                }
            }

            if (random)
            {
                ShuffleIndices(primary, randomSeed);
                ShuffleIndices(nearSlots, randomSeed ^ 0x5bd1e995);
            }

            var filled = 0;
            for (var i = 0; i < primary.Count && filled < fillCount; i++)
            {
                occupied[primary[i]] = true;
                filled++;
            }

            for (var i = 0; i < nearSlots.Count && filled < fillCount; i++)
            {
                occupied[nearSlots[i]] = true;
                filled++;
            }
        }

        private static bool IsNearSlotWaitingForFar(
            WarehouseGridConfig grid,
            SimSlotExclusionZone[] zones,
            IWarehouseSimulationBindings bindings,
            int warehouseId,
            int flatIndex)
        {
            var level = flatIndex % grid.LevelCount;
            var columnAndRow = flatIndex / grid.LevelCount;
            var column = columnAndRow % grid.ColumnCount;
            var row = columnAndRow / grid.ColumnCount;
            if (!TryFindDoubleDepthPartner(bindings, warehouseId, column, out var partnerColumn, out var columnIsFar)
                || columnIsFar
                || partnerColumn < 0
                || partnerColumn >= grid.ColumnCount)
            {
                return false;
            }

            return SlotGridUtility.IsStorageSlot(grid, level, partnerColumn, row, zones);
        }

        private static bool TryFindDoubleDepthPartner(
            IWarehouseSimulationBindings bindings,
            int warehouseId,
            int column,
            out int partnerColumn,
            out bool columnIsFar)
        {
            partnerColumn = -1;
            columnIsFar = false;
            var definitions = bindings?.StackerDefinitions;
            if (definitions == null)
            {
                return false;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition.WarehouseId != warehouseId
                    || !StackerColumnReachUtility.CanReachColumn(in definition, column))
                {
                    continue;
                }

                if (StackerColumnReachUtility.TryGetDoubleDepthPartner(
                        in definition, column, out partnerColumn, out columnIsFar))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ShuffleIndices(System.Collections.Generic.List<int> indices, int randomSeed)
        {
            var rng = new Random(randomSeed);
            for (var i = indices.Count - 1; i > 0; i--)
            {
                var swapIndex = rng.Next(i + 1);
                (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
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

        public bool HasAllocatableFreeSlot(IWarehouseSimulationBindings bindings, ConveyorMapTopology topology)
        {
            if (_grid == null || TotalFreeCount <= 0 || _occupied == null)
            {
                return false;
            }

            var stackerCount = Math.Max(1, bindings?.StackerCount ?? 1);
            for (var stackerId = 0; stackerId < stackerCount; stackerId++)
            {
                if (!ServesThisWarehouse(bindings, topology, stackerId))
                {
                    continue;
                }

                if (HasAllocatableFreeSlotForStacker(bindings, topology, stackerId))
                {
                    return true;
                }
            }

            return false;
        }

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

            var hasDefinition = StackerColumnReachUtility.TryGetDefinition(
                bindings, topology, servingStackerId, out var definition);
            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (_freeCountByColumn[col] <= 0)
                {
                    continue;
                }

                if (!ColumnReachable(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (_occupied[idx]
                            || !_isStorage[idx]
                            || !IsInboundDepthClear(in definition, hasDefinition, level, col, row))
                        {
                            continue;
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryOccupy(GridIndex slot)
        {
            if (_grid == null || _occupied == null)
            {
                return false;
            }

            var idx = ToFlatIndex(_grid, slot.Level, slot.Column, slot.Row);
            if (idx < 0 || idx >= _occupied.Length
                || _occupied[idx]
                || !SlotGridUtility.IsStorageSlot(_grid, slot.Level, slot.Column, slot.Row, _exclusionZones))
            {
                return false;
            }

            _occupied[idx] = true;
            if (_inboundPlacementPending != null && idx < _inboundPlacementPending.Length)
            {
                _inboundPlacementPending[idx] = true;
            }

            _freeCountByColumn[slot.Column]--;
            TotalFreeCount--;
            return true;
        }

        public void Occupy(GridIndex slot) => TryOccupy(slot);

        public bool IsOccupied(GridIndex slot)
        {
            if (_grid == null || _occupied == null)
            {
                return false;
            }

            var idx = ToFlatIndex(_grid, slot.Level, slot.Column, slot.Row);
            return idx >= 0 && idx < _occupied.Length && _occupied[idx];
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
            if (_inboundPlacementPending != null && idx < _inboundPlacementPending.Length)
            {
                _inboundPlacementPending[idx] = false;
            }

            _freeCountByColumn[slot.Column]++;
            TotalFreeCount++;
        }

        public void NotifyInboundPlaced(GridIndex slot)
        {
            if (_grid == null || _inboundPlacementPending == null)
            {
                return;
            }

            var idx = ToFlatIndex(_grid, slot.Level, slot.Column, slot.Row);
            if (idx >= 0 && idx < _inboundPlacementPending.Length)
            {
                _inboundPlacementPending[idx] = false;
            }
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
                if (!ServesThisWarehouse(bindings, topology, stackerId))
                {
                    continue;
                }

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
            var hasDefinition = StackerColumnReachUtility.TryGetDefinition(
                bindings, topology, servingStackerId, out var definition);
            var fromReady = TryReadOrigin(bindings, pickupColumn, pickupRow, out var fromX, out var fromY, out var fromZ);

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (!ColumnReachable(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                if (found && fromReady && ColumnLowerBound(col, fromX, fromY, fromZ) > bestDistance + 1e-4)
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var candidate = MakeSlot(level, col, row);
                        if (reservedSlots != null && reservedSlots.Contains(candidate))
                        {
                            continue;
                        }

                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_occupied[idx]
                            || IsInboundPlacementPending(idx)
                            || !_isStorage[idx]
                            || !IsOutboundDepthClear(in definition, hasDefinition, level, col, row))
                        {
                            continue;
                        }

                        var distance = PickupDistance(
                            bindings, pickupColumn, pickupRow, col, row, level, idx, fromReady, fromX, fromY, fromZ);
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

            var hasDefinition = StackerColumnReachUtility.TryGetDefinition(
                bindings, topology, servingStackerId, out var definition);
            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (!ColumnReachable(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var candidate = MakeSlot(level, col, row);
                        if (reservedSlots != null && reservedSlots.Contains(candidate))
                        {
                            continue;
                        }

                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (_occupied[idx]
                            && !IsInboundPlacementPending(idx)
                            && _isStorage[idx]
                            && IsOutboundDepthClear(in definition, hasDefinition, level, col, row))
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
            var hasDefinition = StackerColumnReachUtility.TryGetDefinition(
                bindings, topology, servingStackerId, out var definition);
            var fromReady = TryReadOrigin(bindings, pickupColumn, pickupRow, out var fromX, out var fromY, out var fromZ);

            for (var col = 0; col < _grid.ColumnCount; col++)
            {
                if (_freeCountByColumn[col] <= 0)
                {
                    continue;
                }

                if (!ColumnReachable(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                if (found && fromReady && ColumnLowerBound(col, fromX, fromY, fromZ) > bestDistance + 1e-4)
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (_occupied[idx]
                            || !_isStorage[idx]
                            || !IsInboundDepthClear(in definition, hasDefinition, level, col, row))
                        {
                            continue;
                        }

                        var distance = PickupDistance(
                            bindings, pickupColumn, pickupRow, col, row, level, idx, fromReady, fromX, fromY, fromZ);
                        if (distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        bestSlot = MakeSlot(level, col, row);
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
            var hasDefinition = StackerColumnReachUtility.TryGetDefinition(
                bindings, topology, servingStackerId, out var definition);
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

                if (!ColumnReachable(bindings, topology, servingStackerId, col))
                {
                    continue;
                }

                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_occupied[idx]
                            && _isStorage[idx]
                            && IsInboundDepthClear(in definition, hasDefinition, level, col, row))
                        {
                            slot = MakeSlot(level, col, row);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 深位：同格浅位必须空着。浅位：同格深位必须已经放进货架，不能只是预占。
        /// </summary>
        private bool IsInboundDepthClear(
            in SimStackerDefinition def,
            bool hasDefinition,
            int level,
            int column,
            int row)
        {
            if (!hasDefinition
                || !StackerColumnReachUtility.TryGetDoubleDepthPartner(
                    in def, column, out var partnerColumn, out var columnIsFar)
                || partnerColumn < 0
                || partnerColumn >= _grid.ColumnCount
                || !SlotGridUtility.IsStorageSlot(_grid, level, partnerColumn, row, _exclusionZones))
            {
                return true;
            }

            var partnerIndex = ToFlatIndex(_grid, level, partnerColumn, row);
            if (columnIsFar)
            {
                return !_occupied[partnerIndex];
            }

            return _occupied[partnerIndex] && !IsInboundPlacementPending(partnerIndex);
        }

        /// <summary>出库取深位时，同格浅位上还有货就会挡住货叉。</summary>
        private bool IsOutboundDepthClear(
            in SimStackerDefinition def,
            bool hasDefinition,
            int level,
            int column,
            int row)
        {
            if (!hasDefinition
                || !StackerColumnReachUtility.TryGetDoubleDepthPartner(
                    in def, column, out var partnerColumn, out var columnIsFar)
                || !columnIsFar
                || partnerColumn < 0
                || partnerColumn >= _grid.ColumnCount
                || !SlotGridUtility.IsStorageSlot(_grid, level, partnerColumn, row, _exclusionZones))
            {
                return true;
            }

            return !_occupied[ToFlatIndex(_grid, level, partnerColumn, row)];
        }

        private bool IsInboundPlacementPending(int flatIndex) =>
            _inboundPlacementPending != null
            && (uint)flatIndex < (uint)_inboundPlacementPending.Length
            && _inboundPlacementPending[flatIndex];

        private double ComputePickupDistance(
            IWarehouseSimulationBindings bindings,
            int pickupColumn,
            int pickupRow,
            int column,
            int row,
            int level)
        {
            var from = new GridIndex(0, pickupColumn, pickupRow, 0, _warehouseId);
            var to = new GridIndex(level, column, row, 0, _warehouseId);
            return SlotPositionMath.ComputeManhattanDistance(bindings.SlotPositions, from, to);
        }

        private void BuildStorageMask()
        {
            var count = _occupied != null ? _occupied.Length : 0;
            _isStorage = new bool[count];
            if (_grid == null)
            {
                return;
            }

            for (var row = 0; row < _grid.RowCount; row++)
            {
                for (var col = 0; col < _grid.ColumnCount; col++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        _isStorage[idx] = SlotGridUtility.IsStorageSlot(
                            _grid, level, col, row, _exclusionZones);
                    }
                }
            }
        }

        private bool ColumnReachable(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId,
            int column)
        {
            if (topology == null)
            {
                return true;
            }

            if (stackerId < 0 || _grid == null || (uint)column >= (uint)_grid.ColumnCount)
            {
                return false;
            }

            if (_reachableByStacker == null || stackerId >= _reachableByStacker.Length)
            {
                var grown = new bool[Math.Max(stackerId + 1, 4)][];
                if (_reachableByStacker != null)
                {
                    Array.Copy(_reachableByStacker, grown, _reachableByStacker.Length);
                }

                _reachableByStacker = grown;
            }

            var flags = _reachableByStacker[stackerId];
            if (flags == null || flags.Length != _grid.ColumnCount)
            {
                flags = new bool[_grid.ColumnCount];
                for (var col = 0; col < flags.Length; col++)
                {
                    flags[col] = IsAllocatableColumnForStacker(bindings, topology, stackerId, col);
                }

                _reachableByStacker[stackerId] = flags;
            }

            return flags[column];
        }

        private bool TryReadOrigin(
            IWarehouseSimulationBindings bindings,
            int pickupColumn,
            int pickupRow,
            out float x,
            out float y,
            out float z)
        {
            x = y = z = 0f;
            EnsureSlotPositions(bindings);
            var index = bindings?.SlotPositions;
            if (index == null || !index.IsReady)
            {
                return false;
            }

            if (!index.TryGetLocalPosition(new GridIndex(0, pickupColumn, pickupRow, 0, _warehouseId), out var local))
            {
                return false;
            }

            x = local.x;
            y = local.y;
            z = local.z;
            return true;
        }

        private void EnsureSlotPositions(IWarehouseSimulationBindings bindings)
        {
            if (_slotPosReady)
            {
                return;
            }

            _slotPosReady = true;
            var count = _occupied != null ? _occupied.Length : 0;
            _slotHasPos = new bool[count];
            _slotX = new float[count];
            _slotY = new float[count];
            _slotZ = new float[count];
            var index = bindings?.SlotPositions;
            if (index == null || !index.IsReady || _grid == null)
            {
                return;
            }

            for (var row = 0; row < _grid.RowCount; row++)
            {
                for (var col = 0; col < _grid.ColumnCount; col++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!index.TryGetLocalPosition(
                                new GridIndex(level, col, row, 0, _warehouseId),
                                out var local))
                        {
                            continue;
                        }

                        _slotHasPos[idx] = true;
                        _slotX[idx] = local.x;
                        _slotY[idx] = local.y;
                        _slotZ[idx] = local.z;
                    }
                }
            }

            BuildColumnBounds();
        }

        private void BuildColumnBounds()
        {
            var columns = _grid.ColumnCount;
            _colBounded = new bool[columns];
            _colMinX = new float[columns];
            _colMaxX = new float[columns];
            _colMinY = new float[columns];
            _colMaxY = new float[columns];
            _colMinZ = new float[columns];
            _colMaxZ = new float[columns];
            for (var col = 0; col < columns; col++)
            {
                var minX = float.PositiveInfinity;
                var maxX = float.NegativeInfinity;
                var minY = float.PositiveInfinity;
                var maxY = float.NegativeInfinity;
                var minZ = float.PositiveInfinity;
                var maxZ = float.NegativeInfinity;
                var any = false;
                for (var row = 0; row < _grid.RowCount; row++)
                {
                    for (var level = 0; level < _grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(_grid, level, col, row);
                        if (!_isStorage[idx] || !_slotHasPos[idx])
                        {
                            continue;
                        }

                        any = true;
                        var x = _slotX[idx];
                        var y = _slotY[idx];
                        var z = _slotZ[idx];
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        if (z < minZ) minZ = z;
                        if (z > maxZ) maxZ = z;
                    }
                }

                if (!any)
                {
                    continue;
                }

                _colBounded[col] = true;
                _colMinX[col] = minX;
                _colMaxX[col] = maxX;
                _colMinY[col] = minY;
                _colMaxY[col] = maxY;
                _colMinZ[col] = minZ;
                _colMaxZ[col] = maxZ;
            }
        }

        private double ColumnLowerBound(int column, float x, float y, float z)
        {
            if (_colBounded == null || (uint)column >= (uint)_colBounded.Length || !_colBounded[column])
            {
                return 0d;
            }

            return AxisGap(x, _colMinX[column], _colMaxX[column])
                   + AxisGap(y, _colMinY[column], _colMaxY[column])
                   + AxisGap(z, _colMinZ[column], _colMaxZ[column]);
        }

        private static double AxisGap(float value, float min, float max)
        {
            var v = (double)value;
            var lo = (double)min;
            var hi = (double)max;
            if (v < lo)
            {
                return lo - v;
            }

            if (v > hi)
            {
                return v - hi;
            }

            return 0d;
        }

        private double PickupDistance(
            IWarehouseSimulationBindings bindings,
            int pickupColumn,
            int pickupRow,
            int column,
            int row,
            int level,
            int idx,
            bool fromReady,
            float fromX,
            float fromY,
            float fromZ)
        {
            if (fromReady && _slotHasPos != null && (uint)idx < (uint)_slotHasPos.Length && _slotHasPos[idx])
            {
                return Math.Abs(_slotX[idx] - fromX)
                       + Math.Abs(_slotY[idx] - fromY)
                       + Math.Abs(_slotZ[idx] - fromZ);
            }

            return ComputePickupDistance(bindings, pickupColumn, pickupRow, column, row, level);
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

        private bool IsColumnAllocatableByAnyStacker(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int column)
        {
            var stackerCount = Math.Max(1, bindings?.StackerCount ?? 1);
            for (var stackerId = 0; stackerId < stackerCount; stackerId++)
            {
                if (!ServesThisWarehouse(bindings, topology, stackerId))
                {
                    continue;
                }

                if (IsAllocatableColumnForStacker(bindings, topology, stackerId, column))
                {
                    return true;
                }
            }

            return false;
        }

        private GridIndex MakeSlot(int level, int column, int row) =>
            new(level, column, row, 0, _warehouseId);

        private bool ServesThisWarehouse(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId) =>
            StackerColumnReachUtility.GetWarehouseId(bindings, topology, stackerId) == _warehouseId;

        public static int ToFlatIndex(WarehouseGridConfig grid, int level, int column, int row) =>
            SlotGridUtility.ToFlatIndex(grid, level, column, row);

        /// <summary>0 号库沿用原种子。其他仓库混入仓库号，避免网格相同时初始占用图案相同。</summary>
        private static int MixOccupancySeed(int randomSeed, int warehouseId)
        {
            if (warehouseId == 0)
            {
                return randomSeed;
            }

            unchecked
            {
                return (randomSeed * 397) ^ warehouseId;
            }
        }
    }
}
