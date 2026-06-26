using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 堆垛机轨道位于 <see cref="SimStackerDefinition.AisleLeftColumn"/> 与下一列之间的空隙；
    /// 双向堆垛机（2 列伸叉）可取空隙两侧货位；单向堆垛机（1 列伸叉）仅服务一侧。
    /// </summary>
    public static class StackerColumnReachUtility
    {
        public static int GetSpan(SimStackerColumnReach reach) =>
            reach switch
            {
                SimStackerColumnReach.OneColumn => 1,
                SimStackerColumnReach.FourColumns => 4,
                _ => 2,
            };

        /// <summary>该堆垛机在输送地图上允许配置的交互点数量上限。</summary>
        public static int GetMaxPickupPointCount(in SimStackerDefinition def) =>
            def.ColumnReach == SimStackerColumnReach.OneColumn ? 1 : 2;

        /// <summary>轨道中心列坐标（空隙正中，非整数货位列）。</summary>
        public static float GetRailColumnCoordinate(int aisleLeftColumn) => aisleLeftColumn + 0.5f;

        public static void GetColumnRange(in SimStackerDefinition def, out int minColumn, out int maxColumn) =>
            GetColumnRange(def.AisleLeftColumn, def.ColumnReach, def.ServingSide, out minColumn, out maxColumn);

        public static void GetColumnRange(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            out int minColumn,
            out int maxColumn) =>
            GetColumnRange(aisleLeftColumn, reach, SimStackerServingSide.Left, out minColumn, out maxColumn);

        public static void GetColumnRange(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            out int minColumn,
            out int maxColumn)
        {
            if (reach == SimStackerColumnReach.OneColumn)
            {
                var column = servingSide == SimStackerServingSide.Right
                    ? aisleLeftColumn + 1
                    : aisleLeftColumn;
                minColumn = column;
                maxColumn = column;
                return;
            }

            if (reach == SimStackerColumnReach.FourColumns)
            {
                minColumn = aisleLeftColumn - 1;
                maxColumn = aisleLeftColumn + 2;
                return;
            }

            minColumn = aisleLeftColumn;
            maxColumn = aisleLeftColumn + 1;
        }

        public static int GetAisleCenterColumn(in SimStackerDefinition def) =>
            GetAisleCenterColumn(def.AisleLeftColumn, def.ColumnReach, def.ServingSide);

        public static int GetAisleCenterColumn(int aisleLeftColumn, SimStackerColumnReach reach) =>
            GetAisleCenterColumn(aisleLeftColumn, reach, SimStackerServingSide.Left);

        public static int GetAisleCenterColumn(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide)
        {
            GetColumnRange(aisleLeftColumn, reach, servingSide, out var min, out var max);
            return (min + max) / 2;
        }

        public static bool CanReachColumn(in SimStackerDefinition def, int column) =>
            CanReachColumn(def.AisleLeftColumn, def.ColumnReach, def.ServingSide, column);

        public static bool CanReachColumn(int aisleLeftColumn, SimStackerColumnReach reach, int column) =>
            CanReachColumn(aisleLeftColumn, reach, SimStackerServingSide.Left, column);

        public static bool CanReachColumn(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            int column)
        {
            GetColumnRange(aisleLeftColumn, reach, servingSide, out var min, out var max);
            return column >= min && column <= max;
        }

        public static int ClampColumn(in SimStackerDefinition def, int column) =>
            ClampColumn(def.AisleLeftColumn, def.ColumnReach, def.ServingSide, column);

        public static int ClampColumn(int aisleLeftColumn, SimStackerColumnReach reach, int column) =>
            ClampColumn(aisleLeftColumn, reach, SimStackerServingSide.Left, column);

        public static int ClampColumn(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            int column)
        {
            GetColumnRange(aisleLeftColumn, reach, servingSide, out var min, out var max);
            return Math.Clamp(column, min, max);
        }

        /// <summary>
        /// 由交互列（浅位）沿伸叉方向向货架深处枚举深位列号（远离巷道轨道一侧）。
        /// </summary>
        public static void CollectRearStorageColumns(
            int pickupColumn,
            in SimStackerDefinition def,
            int maxCount,
            List<int> rearColumns)
        {
            rearColumns.Clear();
            if (maxCount <= 0)
            {
                return;
            }

            CollectRearStorageColumns(
                pickupColumn,
                def.AisleLeftColumn,
                def.ColumnReach,
                def.ServingSide,
                maxCount,
                rearColumns);
        }

        public static void CollectRearStorageColumns(
            int pickupColumn,
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            int maxCount,
            List<int> rearColumns)
        {
            rearColumns.Clear();
            if (maxCount <= 0)
            {
                return;
            }

            GetColumnRange(aisleLeftColumn, reach, servingSide, out var min, out var max);
            if (pickupColumn < min || pickupColumn > max)
            {
                return;
            }

            var direction = pickupColumn > aisleLeftColumn ? 1 : -1;
            for (var depth = 1; depth <= maxCount; depth++)
            {
                var column = pickupColumn + direction * depth;
                if (column < min || column > max)
                {
                    break;
                }

                rearColumns.Add(column);
            }
        }

        /// <summary>堆垛机旁取货点：0=伸叉覆盖最左货位列，1=空隙右侧货位列（单向时恒为最左/唯一列）。</summary>
        public static int GetEntrancePickupColumn(in SimStackerDefinition def, int pickupIndexOnStacker) =>
            GetEntrancePickupColumn(def.AisleLeftColumn, def.ColumnReach, def.ServingSide, pickupIndexOnStacker);

        public static int GetEntrancePickupColumn(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            int pickupIndexOnStacker) =>
            GetEntrancePickupColumn(aisleLeftColumn, reach, SimStackerServingSide.Left, pickupIndexOnStacker);

        public static int GetEntrancePickupColumn(
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            int pickupIndexOnStacker)
        {
            GetColumnRange(aisleLeftColumn, reach, servingSide, out var min, out var max);
            if (reach == SimStackerColumnReach.OneColumn || pickupIndexOnStacker <= 0)
            {
                return min;
            }

            return Math.Min(aisleLeftColumn + 1, max);
        }

        /// <summary>由取货列与巷道左列推导侧别（0=左，1=右）。</summary>
        public static int DerivePickupIndexOnStacker(
            int pickupColumn,
            in SimStackerDefinition def) =>
            DerivePickupIndexOnStacker(pickupColumn, def.AisleLeftColumn, def.ColumnReach, def.ServingSide);

        public static int DerivePickupIndexOnStacker(
            int pickupColumn,
            int aisleLeftColumn,
            SimStackerColumnReach reach) =>
            DerivePickupIndexOnStacker(pickupColumn, aisleLeftColumn, reach, SimStackerServingSide.Left);

        public static int DerivePickupIndexOnStacker(
            int pickupColumn,
            int aisleLeftColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide)
        {
            if (reach == SimStackerColumnReach.OneColumn)
            {
                return 0;
            }

            var col0 = GetEntrancePickupColumn(aisleLeftColumn, reach, servingSide, 0);
            var col1 = GetEntrancePickupColumn(aisleLeftColumn, reach, servingSide, 1);
            if (pickupColumn == col0)
            {
                return 0;
            }

            if (pickupColumn == col1)
            {
                return 1;
            }

            return Math.Abs(pickupColumn - col0) <= Math.Abs(pickupColumn - col1) ? 0 : 1;
        }

        /// <summary>由货位列与侧别反推巷道左列（与 <see cref="GetEntrancePickupColumn"/> 互逆）。</summary>
        public static int InferAisleLeftColumnFromPickupColumn(
            int pickupColumn,
            in SimStackerDefinition def,
            int pickupIndexOnStacker) =>
            InferAisleLeftColumnFromPickupColumn(
                pickupColumn, def.ColumnReach, def.ServingSide, pickupIndexOnStacker);

        public static int InferAisleLeftColumnFromPickupColumn(
            int pickupColumn,
            SimStackerColumnReach reach,
            int pickupIndexOnStacker) =>
            InferAisleLeftColumnFromPickupColumn(
                pickupColumn, reach, SimStackerServingSide.Left, pickupIndexOnStacker);

        public static int InferAisleLeftColumnFromPickupColumn(
            int pickupColumn,
            SimStackerColumnReach reach,
            SimStackerServingSide servingSide,
            int pickupIndexOnStacker)
        {
            if (reach == SimStackerColumnReach.OneColumn)
            {
                return servingSide == SimStackerServingSide.Right
                    ? pickupColumn - 1
                    : pickupColumn;
            }

            if (reach == SimStackerColumnReach.FourColumns)
            {
                return pickupIndexOnStacker <= 0 ? pickupColumn + 1 : pickupColumn - 1;
            }

            return pickupIndexOnStacker <= 0 ? pickupColumn : pickupColumn - 1;
        }

        public static bool CanReachSlot(in SimStackerDefinition def, int slotColumn) =>
            CanReachColumn(in def, slotColumn);

        public static bool TryGetDefinition(
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology,
            int stackerId,
            out SimStackerDefinition definition)
        {
            if (fleet?.StackerDefinitions != null)
            {
                for (var i = 0; i < fleet.StackerDefinitions.Length; i++)
                {
                    var entry = fleet.StackerDefinitions[i];
                    if (entry.StackerId == stackerId)
                    {
                        definition = entry;
                        return true;
                    }
                }
            }

            if (topology?.Map != null
                && TryDeriveFromMap(fleet, topology.Map, stackerId, out definition))
            {
                return true;
            }

            definition = new SimStackerDefinition
            {
                StackerId = stackerId,
                AisleLeftColumn = 0,
                ColumnReach = fleet != null
                    ? fleet.DefaultStackerColumnReach
                    : SimStackerColumnReach.TwoColumns,
            };
            return true;
        }

        public static bool TryDeriveFromMap(
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology,
            int stackerId,
            out SimStackerDefinition definition) =>
            TryDeriveFromMap(fleet, topology?.Map, stackerId, out definition);

        public static bool TryDeriveFromMap(
            IStackerFleetDescriptor fleet,
            WarehouseConveyorMap map,
            int stackerId,
            out SimStackerDefinition definition)
        {
            definition = default;
            if (map?.Nodes == null)
            {
                return false;
            }

            var reach = fleet != null
                ? fleet.DefaultStackerColumnReach
                : SimStackerColumnReach.TwoColumns;
            var aisleLeft = int.MaxValue;
            var servingSide = SimStackerServingSide.Left;
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                if (node.Kind != SimConveyorNodeKind.PickupPoint || node.StackerId != stackerId)
                {
                    continue;
                }

                var left = -1;
                if (node.PickupColumn > 0)
                {
                    for (var tryIdx = 0; tryIdx <= 1; tryIdx++)
                    {
                        for (var trySide = 0; trySide <= 1; trySide++)
                        {
                            var side = trySide == 0
                                ? SimStackerServingSide.Left
                                : SimStackerServingSide.Right;
                            var candidate = InferAisleLeftColumnFromPickupColumn(
                                node.PickupColumn, reach, side, tryIdx);
                            if (DerivePickupIndexOnStacker(node.PickupColumn, candidate, reach, side) == tryIdx
                                && CanReachColumn(candidate, reach, side, node.PickupColumn))
                            {
                                left = candidate;
                                servingSide = side;
                                break;
                            }
                        }

                        if (left >= 0)
                        {
                            break;
                        }
                    }
                }

                if (left >= 0)
                {
                    aisleLeft = Math.Min(aisleLeft, left);
                }
            }

            if (aisleLeft == int.MaxValue)
            {
                return false;
            }

            definition = new SimStackerDefinition
            {
                StackerId = stackerId,
                AisleLeftColumn = aisleLeft,
                ColumnReach = reach,
                ServingSide = reach == SimStackerColumnReach.OneColumn
                    ? servingSide
                    : SimStackerServingSide.Left,
            };
            return true;
        }

        /// <summary>从 Fleet 显式定义或地图交互点推导堆垛机列域（拓扑构建前可用）。</summary>
        public static bool TryResolveDefinitionForMapValidation(
            IStackerFleetDescriptor fleet,
            WarehouseConveyorMap map,
            int stackerId,
            out SimStackerDefinition definition)
        {
            if (fleet?.StackerDefinitions != null)
            {
                for (var i = 0; i < fleet.StackerDefinitions.Length; i++)
                {
                    var entry = fleet.StackerDefinitions[i];
                    if (entry.StackerId == stackerId)
                    {
                        definition = entry;
                        return true;
                    }
                }
            }

            return TryDeriveFromMap(fleet, map, stackerId, out definition);
        }

        public static bool IsColumnServicedByAnyStacker(
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology,
            int column)
        {
            if (fleet == null)
            {
                return false;
            }

            var count = Math.Max(1, fleet.StackerCount);
            for (var stackerId = 0; stackerId < count; stackerId++)
            {
                if (!TryGetDefinition(fleet, topology, stackerId, out var def))
                {
                    continue;
                }

                if (CanReachColumn(in def, column))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
