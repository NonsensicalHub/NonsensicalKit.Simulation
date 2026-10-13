using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>仓库列表解析。列表为空时，退回绑定资产上的数据名和场景 Runner 网格，作为唯一的 0 号库。</summary>
    public static class WarehouseCatalog
    {
        public static bool HasExplicitWarehouses(IWarehouseSimulationBindings bindings)
        {
            var warehouses = bindings?.Warehouses;
            return warehouses != null && warehouses.Length > 0;
        }

        public static bool TryGet(
            IWarehouseSimulationBindings bindings,
            int warehouseId,
            out SimWarehouseDefinition definition)
        {
            definition = null;
            var warehouses = bindings?.Warehouses;
            if (warehouses == null)
            {
                return false;
            }

            for (var i = 0; i < warehouses.Length; i++)
            {
                var entry = warehouses[i];
                if (entry != null && entry.WarehouseId == warehouseId)
                {
                    definition = entry;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 得到本次仿真的全部货架。列表为空时合成一个 0 号库，网格用 <paramref name="legacyGrid"/>。
        /// </summary>
        public static bool TryResolve(
            IWarehouseSimulationBindings bindings,
            WarehouseGridConfig legacyGrid,
            out List<SimWarehouseDefinition> warehouses,
            out string error)
        {
            if (!HasExplicitWarehouses(bindings))
            {
                warehouses = null;
                if (!IsGridReady(legacyGrid))
                {
                    error = "网格配置未配置。";
                    return false;
                }

                warehouses = new List<SimWarehouseDefinition>
                {
                    new SimWarehouseDefinition
                    {
                        WarehouseId = 0,
                        Name = "库0",
                        Grid = legacyGrid,
                    },
                };
                error = null;
                return true;
            }

            return TryCollect(bindings, out warehouses, out error);
        }

        /// <summary>填写了仓库列表时，交互列/排必须落在所属仓库的网格内。</summary>
        private static bool TryValidatePickups(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            IWarehouseGridLookup grids,
            out string error)
        {
            error = null;
            if (!HasExplicitWarehouses(bindings) || topology?.Map?.Nodes == null || grids == null)
            {
                return true;
            }

            var nodes = topology.Map.Nodes;
            for (var i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if (node.Kind != SimConveyorNodeKind.PickupPoint)
                {
                    continue;
                }

                var warehouseId = StackerColumnReachUtility.GetWarehouseId(bindings, topology, node.StackerId);
                if (!grids.TryGetGrid(warehouseId, out var grid) || !IsGridReady(grid))
                {
                    error = $"交互点 {SimEntityNaming.FormatLogicalId(in node, i)} 所属仓库 {warehouseId} 没有可用网格。";
                    return false;
                }

                var column = SimConveyorNodeBinding.ResolvePickupColumn(node, bindings, topology);
                var row = node.PickupRow;
                if (column < 0 || column >= grid.ColumnCount)
                {
                    error = $"仓库 {warehouseId} 的交互点 {SimEntityNaming.FormatLogicalId(in node, i)} 列 {column} 超出网格列数 {grid.ColumnCount}。";
                    return false;
                }

                if (row < 0 || row >= grid.RowCount)
                {
                    error = $"仓库 {warehouseId} 的交互点 {SimEntityNaming.FormatLogicalId(in node, i)} 排 {row} 超出网格排数 {grid.RowCount}。";
                    return false;
                }
            }

            return true;
        }

        internal static bool TryValidate(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            IWarehouseGridLookup grids,
            out string error)
        {
            if (!HasExplicitWarehouses(bindings))
            {
                return TryRequireStackerWarehouses(bindings, new HashSet<int> { 0 }, out error);
            }

            if (!TryCollect(bindings, out var warehouses, out error))
            {
                return false;
            }

            if (!TryRequireLoadedPositions(bindings, warehouses, out error))
            {
                return false;
            }

            if (!TryRequireStackerWarehouses(bindings, ToIdSet(warehouses), out error))
            {
                return false;
            }

            return TryValidatePickups(bindings, topology, grids, out error);
        }

        private static bool TryRequireLoadedPositions(
            IWarehouseSimulationBindings bindings,
            List<SimWarehouseDefinition> warehouses,
            out string error)
        {
            error = null;
            if (!HasExplicitWarehouses(bindings) || !(bindings.SlotPositions is WarehouseSlotPositionCatalog catalog))
            {
                return true;
            }

            for (var i = 0; i < warehouses.Count; i++)
            {
                var warehouseId = warehouses[i].WarehouseId;
                if (IsPositionReady(catalog, warehouseId))
                {
                    continue;
                }

                error = $"仓库 {warehouseId} 的货位数据未加载。";
                return false;
            }

            return true;
        }

        private static bool TryCollect(
            IWarehouseSimulationBindings bindings,
            out List<SimWarehouseDefinition> warehouses,
            out string error)
        {
            warehouses = new List<SimWarehouseDefinition>();
            error = null;
            var source = bindings.Warehouses;
            var seen = new HashSet<int>();
            for (var i = 0; i < source.Length; i++)
            {
                var entry = source[i];
                if (entry == null)
                {
                    error = "仓库列表含空项。";
                    warehouses = null;
                    return false;
                }

                if (entry.WarehouseId < 0)
                {
                    error = "仓库号不能小于 0。";
                    warehouses = null;
                    return false;
                }

                if (!seen.Add(entry.WarehouseId))
                {
                    error = $"仓库号 {entry.WarehouseId} 重复。";
                    warehouses = null;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(entry.WarehouseDataName))
                {
                    error = $"仓库 {entry.WarehouseId} 未填写仓库数据名。";
                    warehouses = null;
                    return false;
                }

                if (!IsGridReady(entry.Grid))
                {
                    error = $"仓库 {entry.WarehouseId} 的货架网格未配置。";
                    warehouses = null;
                    return false;
                }

                warehouses.Add(entry);
            }

            return true;
        }

        private static bool TryRequireStackerWarehouses(
            IWarehouseSimulationBindings bindings,
            HashSet<int> warehouseIds,
            out string error)
        {
            error = null;
            if (bindings == null)
            {
                return true;
            }

            var count = System.Math.Max(1, bindings.StackerCount);
            for (var stackerId = 0; stackerId < count; stackerId++)
            {
                if (!IsKnownWarehouse(
                        StackerColumnReachUtility.GetWarehouseId(bindings, null, stackerId),
                        warehouseIds,
                        out error,
                        $"堆垛机 {SimEntityNaming.FormatStacker(stackerId)}"))
                {
                    return false;
                }
            }

            var definitions = bindings.StackerDefinitions;
            if (definitions == null)
            {
                return true;
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                if (!IsKnownWarehouse(
                        definitions[i].WarehouseId,
                        warehouseIds,
                        out error,
                        $"堆垛机 {SimEntityNaming.FormatStacker(definitions[i].StackerId)}"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsKnownWarehouse(
            int warehouseId,
            HashSet<int> warehouseIds,
            out string error,
            string ownerLabel)
        {
            error = null;
            if (warehouseIds != null && warehouseIds.Contains(warehouseId))
            {
                return true;
            }

            error = $"{ownerLabel} 属于仓库 {warehouseId}，但未配置该仓库。";
            return false;
        }

        private static HashSet<int> ToIdSet(List<SimWarehouseDefinition> warehouses)
        {
            var ids = new HashSet<int>();
            for (var i = 0; i < warehouses.Count; i++)
            {
                ids.Add(warehouses[i].WarehouseId);
            }

            return ids;
        }

        private static bool IsGridReady(WarehouseGridConfig grid) =>
            grid != null && grid.LevelCount > 0 && grid.ColumnCount > 0 && grid.RowCount > 0;

        private static bool IsPositionReady(WarehouseSlotPositionCatalog catalog, int warehouseId) =>
            catalog != null
            && catalog.TryGetIndex(warehouseId, out var index)
            && index != null
            && index.IsReady;
    }
}
