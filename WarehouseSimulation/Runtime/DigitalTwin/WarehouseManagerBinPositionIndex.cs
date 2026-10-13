using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 桥接 WarehouseManager：提供货位局部/世界坐标，底层复用 <see cref="SlotPositionIndex"/>。
    /// </summary>
    public sealed class WarehouseManagerBinPositionIndex : MonoBehaviour
    {
        [SerializeField, Label("硬件绑定（优先）")]
        private DefaultWarehouseSimulationBindingsAsset m_bindings;

        [SerializeField, Label("数字孪生仓库")]
        private WarehouseManager m_warehouseManager;

        [SerializeField, Label("仓库数据名（无绑定时）")]
        private string m_warehouseName = "SimulationTest";

        [SerializeField, Label("仓库号")]
        [Tooltip("0 为默认库。堆垛机回放会按堆垛机所属仓库调用 UseWarehouse。")]
        private int m_warehouseId;

        private int _activeWarehouseId;
        private bool _hasWarehouseOverride;
        private ISlotPositionIndex _resolvedIndex;

        public int WarehouseId => _hasWarehouseOverride ? _activeWarehouseId : m_warehouseId;

        public bool IsReady => ResolvePositions().IsReady;

        /// <summary>指定本次解析使用的仓库号，并在仓库变化时丢掉已解析的坐标索引。</summary>
        public void UseWarehouse(int warehouseId)
        {
            if (_hasWarehouseOverride
                && _activeWarehouseId == warehouseId
                && _resolvedIndex != null
                && _resolvedIndex.IsReady)
            {
                return;
            }

            _hasWarehouseOverride = true;
            _activeWarehouseId = warehouseId;
            _resolvedIndex = null;
        }

        public ISlotPositionIndex Positions => ResolvePositions();

        public bool TryLoad()
        {
            if (m_bindings != null)
            {
                m_bindings.EnsureSlotPositionsLoaded();
            }

            return IsReady;
        }

        public bool TryGetLocalPosition(GridIndex slot, out Vector3 local) =>
            Positions.TryGetLocalPosition(slot, out local);

        public bool TryGetWorldPosition(GridIndex slot, out Vector3 world)
        {
            world = default;
            if (!TryGetLocalPosition(slot, out var local))
            {
                return false;
            }

            if (m_warehouseManager == null)
            {
                return false;
            }

            world = m_warehouseManager.transform.TransformPoint(local);
            return true;
        }

        private ISlotPositionIndex ResolvePositions()
        {
            if (_resolvedIndex != null && _resolvedIndex.IsReady)
            {
                return _resolvedIndex;
            }

            _resolvedIndex = LoadPositions();
            return _resolvedIndex;
        }

        private ISlotPositionIndex LoadPositions()
        {
            var warehouseId = WarehouseId;
            if (m_bindings != null)
            {
                m_bindings.EnsureSlotPositionsLoaded();
                if (m_bindings.SlotPositions is WarehouseSlotPositionCatalog catalog
                    && catalog.TryGetIndex(warehouseId, out var indexed)
                    && indexed.IsReady)
                {
                    return indexed;
                }

                if (warehouseId == 0 && m_bindings.SlotPositions != null && m_bindings.SlotPositions.IsReady)
                {
                    return m_bindings.SlotPositions;
                }
            }

            if (warehouseId == 0)
            {
                return SlotPositionIndex.TryLoadFromStreamingAssets(m_warehouseName) ?? new SlotPositionIndex();
            }

            return new SlotPositionIndex();
        }
    }
}
