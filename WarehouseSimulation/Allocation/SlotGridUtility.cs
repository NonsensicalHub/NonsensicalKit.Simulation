using System.Collections.Generic;
using NonsensicalKit.DigitalTwin.Warehouse;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>规则网格坐标与货位可用性判断。</summary>
    public static class SlotGridUtility
    {
        public static int CountPhysicalSlots(WarehouseGridConfig grid) =>
            grid == null ? 0 : grid.LevelCount * grid.ColumnCount * grid.RowCount;

        public static int CountStorageSlots(WarehouseGridConfig grid, IWarehouseSimulationBindings bindings = null)
        {
            if (grid == null)
            {
                return 0;
            }

            return CountStorageSlots(grid, BuildEffectiveExclusionZones(grid, bindings));
        }

        public static int CountStorageSlots(WarehouseGridConfig grid, SimSlotExclusionZone[] zones)
        {
            if (grid == null)
            {
                return 0;
            }

            var count = 0;
            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var col = 0; col < grid.ColumnCount; col++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        if (IsStorageSlot(grid, level, col, row, zones))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        public static int CountOccupiedStorageSlots(
            WarehouseGridConfig grid,
            bool[] occupied,
            IWarehouseSimulationBindings bindings = null) =>
            CountOccupiedStorageSlots(grid, occupied, BuildEffectiveExclusionZones(grid, bindings));

        public static int CountOccupiedStorageSlots(
            WarehouseGridConfig grid,
            bool[] occupied,
            SimSlotExclusionZone[] zones)
        {
            if (grid == null || occupied == null)
            {
                return 0;
            }

            var count = 0;
            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var col = 0; col < grid.ColumnCount; col++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(grid, level, col, row);
                        if (idx < occupied.Length
                            && occupied[idx]
                            && IsStorageSlot(grid, level, col, row, zones))
                        {
                            count++;
                        }
                    }
                }
            }

            return count;
        }

        public static SimSlotExclusionZone[] BuildEffectiveExclusionZones(
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings bindings = null,
            int warehouseId = 0,
            bool restrictToWarehouse = false)
        {
            if (grid == null)
            {
                return System.Array.Empty<SimSlotExclusionZone>();
            }

            var list = new List<SimSlotExclusionZone>();
            if (grid.SlotExclusionZones != null && grid.SlotExclusionZones.Length > 0)
            {
                list.AddRange(grid.SlotExclusionZones);
            }

            if (!grid.AutoExcludePickupPointZones)
            {
                AppendDatSlotDisableRuleZones(list, grid, bindings, warehouseId, restrictToWarehouse);
                AppendMissingBinExclusionZones(list, grid, bindings, warehouseId);
                return list.Count > 0 ? list.ToArray() : System.Array.Empty<SimSlotExclusionZone>();
            }

            var map = bindings?.ConveyorMap;
            if (map?.Nodes == null)
            {
                AppendDatSlotDisableRuleZones(list, grid, bindings, warehouseId, restrictToWarehouse);
                AppendMissingBinExclusionZones(list, grid, bindings, warehouseId);
                return list.Count > 0 ? list.ToArray() : System.Array.Empty<SimSlotExclusionZone>();
            }

            // 层号 0 = 底层；排除交互点浅位列/排及向深处延伸的深位列，各从底层向上的若干层。
            var bottomLevelEnd = System.Math.Min(
                grid.LevelCount - 1,
                System.Math.Max(0, grid.PickupPointExcludedLevelCount - 1));
            var rearColumnCount = System.Math.Max(0, grid.PickupPointExcludedRearColumnCount);
            ConveyorMapTopology.TryBuild(map, bindings, out var topology, out _);
            var seen = new HashSet<(int col, int row, int excludedLevelEnd)>();
            var rearColumns = rearColumnCount > 0 ? new List<int>(rearColumnCount) : null;
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                if (node.Kind != SimConveyorNodeKind.PickupPoint)
                {
                    continue;
                }

                if (restrictToWarehouse
                    && StackerColumnReachUtility.GetWarehouseId(bindings, topology, node.StackerId) != warehouseId)
                {
                    continue;
                }

                var pickupColumn = SimConveyorNodeBinding.ResolvePickupColumn(node, bindings, topology);
                if (pickupColumn < 0 || pickupColumn >= grid.ColumnCount)
                {
                    continue;
                }

                var pickupRow = node.PickupRow;
                if (pickupRow < 0 || pickupRow >= grid.RowCount)
                {
                    continue;
                }

                TryAddPickupExclusionZone(list, seen, pickupColumn, pickupRow, bottomLevelEnd);

                if (StackerColumnReachUtility.TryGetDefinition(bindings, topology, node.StackerId, out var stackerDef))
                {
                    // 交互列在货位列域外（对侧取放货，如单向机货位列0、交互列1）时，
                    // CollectRear 会因列域校验直接跳过；仍须排除该机可达货位列在交互排上的底层。
                    if (!StackerColumnReachUtility.CanReachColumn(in stackerDef, pickupColumn))
                    {
                        StackerColumnReachUtility.GetColumnRange(in stackerDef, out var reachMin, out var reachMax);
                        for (var reachCol = reachMin; reachCol <= reachMax; reachCol++)
                        {
                            TryAddPickupExclusionZone(list, seen, reachCol, pickupRow, bottomLevelEnd);
                        }
                    }
                    else if (rearColumnCount > 0 && rearColumns != null)
                    {
                        StackerColumnReachUtility.CollectRearStorageColumns(
                            pickupColumn, in stackerDef, rearColumnCount, rearColumns);
                        for (var rearIndex = 0; rearIndex < rearColumns.Count; rearIndex++)
                        {
                            TryAddPickupExclusionZone(
                                list, seen, rearColumns[rearIndex], pickupRow, bottomLevelEnd);
                        }
                    }
                }
            }

            AppendDatSlotDisableRuleZones(list, grid, bindings, warehouseId, restrictToWarehouse);
            AppendMissingBinExclusionZones(list, grid, bindings, warehouseId);

            return list.Count > 0 ? list.ToArray() : System.Array.Empty<SimSlotExclusionZone>();
        }

        public static bool IsStorageSlot(WarehouseGridConfig grid, int level, int column, int row) =>
            IsStorageSlot(grid, level, column, row, BuildEffectiveExclusionZones(grid));

        public static bool IsStorageSlot(
            WarehouseGridConfig grid,
            int level,
            int column,
            int row,
            SimSlotExclusionZone[] zones)
        {
            if (grid == null)
            {
                return true;
            }

            if (level < 0 || level >= grid.LevelCount
                || column < 0 || column >= grid.ColumnCount
                || row < 0 || row >= grid.RowCount)
            {
                return false;
            }

            if (zones == null || zones.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < zones.Length; i++)
            {
                if (Contains(zones[i], level, column, row))
                {
                    return false;
                }
            }

            return true;
        }


        /// <summary>
        /// .dat 中缺失的货位（无精确 bin）并入排除区。
        /// SimulationTest 等库第一排底层往往在布局里删掉 bin，却仍留在网格尺寸里；
        /// 若不排除，分配器会选中它们，运动再轴向合成坐标，看起来就像放到「排除货位」或叠到邻位货物上。
        /// </summary>
        private static void AppendMissingBinExclusionZones(
            List<SimSlotExclusionZone> list,
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings bindings,
            int warehouseId)
        {
            if (list == null || grid == null || bindings == null)
            {
                return;
            }

            bindings.EnsureSlotPositionsLoaded();
            var positions = bindings.SlotPositions;
            if (positions == null || !positions.IsReady)
            {
                return;
            }

            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var col = 0; col < grid.ColumnCount; col++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        var slot = new GridIndex(level, col, row, 0, warehouseId);
                        if (positions.HasExactLocalPosition(slot))
                        {
                            continue;
                        }

                        list.Add(new SimSlotExclusionZone
                        {
                            ColumnStart = col,
                            ColumnEnd = col,
                            LevelStart = level,
                            LevelEnd = level,
                            RowStart = row,
                            RowEnd = row,
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 合并 .dat DSBL 结构禁用规则到排除区，使入库分配与 WarehouseManager 视觉禁用一致。
        /// 例：第一排下面几层（Row[0,0]+Level[0,2]）在 .dat 已禁用时，仿真不得再分配放货。
        /// </summary>
        private static void AppendDatSlotDisableRuleZones(
            List<SimSlotExclusionZone> list,
            WarehouseGridConfig grid,
            IWarehouseSimulationBindings bindings,
            int warehouseId,
            bool restrictToWarehouse)
        {
            if (list == null || grid == null || bindings == null)
            {
                return;
            }

            bindings.EnsureSlotPositionsLoaded();
            if (bindings.SlotPositions == null)
            {
                return;
            }

            if (!TryResolveSlotDisableRules(
                    bindings.SlotPositions,
                    warehouseId,
                    restrictToWarehouse,
                    out var rules)
                || rules == null
                || rules.Length == 0)
            {
                return;
            }

            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (rule == null || !rule.Enabled || !rule.HasAnyConstraint)
                {
                    continue;
                }

                // 无深度维的网格排除：Depth 约束忽略（禁用任意 Depth 的同层列排）。
                var columnStart = rule.ConstrainColumn ? System.Math.Min(rule.ColumnMin, rule.ColumnMax) : 0;
                var columnEnd = rule.ConstrainColumn
                    ? System.Math.Max(rule.ColumnMin, rule.ColumnMax)
                    : grid.ColumnCount - 1;
                var levelStart = rule.ConstrainLevel ? System.Math.Min(rule.LevelMin, rule.LevelMax) : 0;
                var levelEnd = rule.ConstrainLevel
                    ? System.Math.Max(rule.LevelMin, rule.LevelMax)
                    : grid.LevelCount - 1;
                var rowStart = 0;
                var rowEnd = -1;
                if (rule.ConstrainRow)
                {
                    rowStart = System.Math.Min(rule.RowMin, rule.RowMax);
                    rowEnd = System.Math.Max(rule.RowMin, rule.RowMax);
                }

                if (columnEnd < 0 || levelEnd < 0 || columnStart > columnEnd || levelStart > levelEnd)
                {
                    continue;
                }

                columnStart = System.Math.Clamp(columnStart, 0, grid.ColumnCount - 1);
                columnEnd = System.Math.Clamp(columnEnd, 0, grid.ColumnCount - 1);
                levelStart = System.Math.Clamp(levelStart, 0, grid.LevelCount - 1);
                levelEnd = System.Math.Clamp(levelEnd, 0, grid.LevelCount - 1);
                if (rowEnd >= 0)
                {
                    rowStart = System.Math.Clamp(rowStart, 0, grid.RowCount - 1);
                    rowEnd = System.Math.Clamp(rowEnd, 0, grid.RowCount - 1);
                }

                list.Add(new SimSlotExclusionZone
                {
                    ColumnStart = columnStart,
                    ColumnEnd = columnEnd,
                    LevelStart = levelStart,
                    LevelEnd = levelEnd,
                    RowStart = rowStart,
                    RowEnd = rowEnd,
                });
            }
        }

        private static bool TryResolveSlotDisableRules(
            ISlotPositionIndex positions,
            int warehouseId,
            bool restrictToWarehouse,
            out WarehouseSlotDisableRule[] rules)
        {
            rules = null;
            if (positions is WarehouseSlotPositionCatalog catalog)
            {
                if (restrictToWarehouse)
                {
                    return catalog.TryGetSlotDisableRules(warehouseId, out rules);
                }

                if (catalog.TryGetSlotDisableRules(warehouseId, out rules))
                {
                    return true;
                }

                return catalog.TryGetSlotDisableRules(0, out rules);
            }

            if (positions is SlotPositionIndex single)
            {
                rules = single.SlotDisableRules;
                return rules != null && rules.Length > 0;
            }

            return false;
        }

        private static void TryAddPickupExclusionZone(
            List<SimSlotExclusionZone> list,
            HashSet<(int col, int row, int excludedLevelEnd)> seen,
            int column,
            int row,
            int bottomLevelEnd)
        {
            if (!seen.Add((column, row, bottomLevelEnd)))
            {
                return;
            }

            list.Add(new SimSlotExclusionZone
            {
                ColumnStart = column,
                ColumnEnd = column,
                LevelStart = 0,
                LevelEnd = bottomLevelEnd,
                RowStart = row,
                RowEnd = row,
            });
        }

        private static bool Contains(in SimSlotExclusionZone zone, int level, int column, int row)
        {
            if (column < zone.ColumnStart || column > zone.ColumnEnd)
            {
                return false;
            }

            if (level < zone.LevelStart || level > zone.LevelEnd)
            {
                return false;
            }

            if (zone.RowEnd < 0)
            {
                return true;
            }

            return row >= zone.RowStart && row <= zone.RowEnd;
        }

        public static int ToFlatIndex(WarehouseGridConfig grid, int level, int column, int row) =>
            (row * grid.ColumnCount + column) * grid.LevelCount + level;

        /// <summary>将占用位图转为货位索引列表（仅可存储货位，供回放播种）。</summary>
        public static List<GridIndex> EnumerateOccupiedStorageSlots(
            WarehouseGridConfig grid,
            bool[] occupied,
            IWarehouseSimulationBindings bindings = null,
            int warehouseId = 0)
        {
            var result = new List<GridIndex>();
            if (grid == null || occupied == null)
            {
                return result;
            }

            var zones = BuildEffectiveExclusionZones(grid, bindings, warehouseId, restrictToWarehouse: true);
            for (var row = 0; row < grid.RowCount; row++)
            {
                for (var col = 0; col < grid.ColumnCount; col++)
                {
                    for (var level = 0; level < grid.LevelCount; level++)
                    {
                        var idx = ToFlatIndex(grid, level, col, row);
                        if (idx < occupied.Length
                            && occupied[idx]
                            && IsStorageSlot(grid, level, col, row, zones))
                        {
                            result.Add(new GridIndex(level, col, row, 0, warehouseId));
                        }
                    }
                }
            }

            return result;
        }
    }
}
