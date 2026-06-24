using System.Collections.Generic;
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

            var zones = BuildEffectiveExclusionZones(grid, bindings);
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
            IWarehouseSimulationBindings bindings = null)
        {
            if (grid == null || occupied == null)
            {
                return 0;
            }

            var zones = BuildEffectiveExclusionZones(grid, bindings);
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
            IWarehouseSimulationBindings bindings = null)
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
                return list.Count > 0 ? list.ToArray() : System.Array.Empty<SimSlotExclusionZone>();
            }

            var map = bindings?.ConveyorMap;
            if (map?.Nodes == null)
            {
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

                if (rearColumnCount <= 0 || rearColumns == null)
                {
                    continue;
                }

                if (StackerColumnReachUtility.TryGetDefinition(bindings, topology, node.StackerId, out var stackerDef))
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
            IWarehouseSimulationBindings bindings = null)
        {
            var result = new List<GridIndex>();
            if (grid == null || occupied == null)
            {
                return result;
            }

            var zones = BuildEffectiveExclusionZones(grid, bindings);
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
                            result.Add(new GridIndex(level, col, row));
                        }
                    }
                }
            }

            return result;
        }
    }
}
