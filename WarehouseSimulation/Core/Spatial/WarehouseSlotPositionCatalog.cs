using System.Collections.Generic;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 按 <see cref="GridIndex.WarehouseId"/> 选择对应仓库的货位坐标。
    /// 单个 <see cref="SlotPositionIndex"/> 只存本库层/列/排，查找前去掉仓库号。
    /// </summary>
    public sealed class WarehouseSlotPositionCatalog : ISlotPositionIndex
    {
        private readonly Dictionary<int, SlotPositionIndex> _byWarehouse = new();

        public bool IsReady
        {
            get
            {
                if (_byWarehouse.Count == 0)
                {
                    return false;
                }

                foreach (var index in _byWarehouse.Values)
                {
                    if (index == null || !index.IsReady)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public static WarehouseSlotPositionCatalog Load(
            string legacyWarehouseDataName,
            SimWarehouseDefinition[] warehouses)
        {
            var catalog = new WarehouseSlotPositionCatalog();
            if (warehouses == null || warehouses.Length == 0)
            {
                catalog.Add(
                    0,
                    SlotPositionIndex.TryLoadFromStreamingAssets(legacyWarehouseDataName) ?? new SlotPositionIndex());
                return catalog;
            }

            for (var i = 0; i < warehouses.Length; i++)
            {
                var definition = warehouses[i];
                if (definition == null
                    || definition.WarehouseId < 0
                    || catalog._byWarehouse.ContainsKey(definition.WarehouseId))
                {
                    continue;
                }

                catalog.Add(
                    definition.WarehouseId,
                    SlotPositionIndex.TryLoadFromStreamingAssets(definition.WarehouseDataName) ?? new SlotPositionIndex());
            }

            return catalog;
        }

        public bool TryGetIndex(int warehouseId, out ISlotPositionIndex index)
        {
            if (_byWarehouse.TryGetValue(warehouseId, out var concrete) && concrete != null)
            {
                index = concrete;
                return true;
            }

            index = null;
            return false;
        }

        /// <summary>读取指定仓库 .dat 中的 DSBL 结构禁用规则。</summary>
        public bool TryGetSlotDisableRules(
            int warehouseId,
            out NonsensicalKit.DigitalTwin.Warehouse.WarehouseSlotDisableRule[] rules)
        {
            rules = null;
            if (!_byWarehouse.TryGetValue(warehouseId, out var concrete) || concrete == null)
            {
                return false;
            }

            rules = concrete.SlotDisableRules;
            return rules != null && rules.Length > 0;
        }

        public bool HasExactLocalPosition(GridIndex slot)
        {
            if (!TryGetIndex(slot.WarehouseId, out var index) || index == null)
            {
                return false;
            }

            return index.HasExactLocalPosition(slot);
        }

        public bool TryGetLocalPosition(GridIndex slot, out Vector3 local)
        {
            local = default;
            if (!TryGetIndex(slot.WarehouseId, out var index))
            {
                return false;
            }

            if (index.TryGetLocalPosition(slot, out local))
            {
                return true;
            }

            return index.TryGetAxisPosition(slot.Level, slot.Column, slot.Row, out local);
        }

        public bool TryGetAxisPosition(int level, int column, int row, out Vector3 local)
        {
            local = default;
            if (_byWarehouse.Count != 1)
            {
                return false;
            }

            foreach (var index in _byWarehouse.Values)
            {
                if (index == null)
                {
                    return false;
                }

                return index.TryGetAxisPosition(level, column, row, out local);
            }

            return false;
        }

        private void Add(int warehouseId, SlotPositionIndex index) =>
            _byWarehouse[warehouseId] = index;
    }
}
