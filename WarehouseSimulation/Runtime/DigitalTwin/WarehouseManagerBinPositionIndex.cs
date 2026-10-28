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

        /// <summary>索引上序列化的数字孪生仓库。世界坐标用它的 Transform，不随 <see cref="UseWarehouse"/> 更换。</summary>
        public WarehouseManager BoundManager => m_warehouseManager;

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
            // 精确货位优先；缺失时用轴向合成局部坐标再变换到世界，
            // 避免关节绑定在「HasLocal 靠合成、World 却失败」时把局部增量当成世界方向，
            // 把行走轴与伸叉轴绑反，放货时 X 走到无货位处。
            if (!TryGetLocalPosition(slot, out var local)
                && !Positions.TryGetAxisPosition(slot.Level, slot.Column, slot.Row, out local))
            {
                return false;
            }

            return TryLocalToWorld(local, out world);
        }

        /// <summary>
        /// 把仓库局部坐标（.dat / 轴合成）转到世界坐标。堆垛机新驱动用 L/R/F 合成点对齐时需要。
        /// </summary>
        public bool TryLocalToWorld(Vector3 local, out Vector3 world)
        {
            world = default;
            if (m_warehouseManager == null)
            {
                return false;
            }

            world = m_warehouseManager.transform.TransformPoint(local);
            return true;
        }

        /// <summary>
        /// 世界坐标 → 仓库局部（.dat 轴向空间）。Carriage 收叉时用：只改 Fork 分量、保持当前排/层。
        /// </summary>
        public bool TryWorldToLocal(Vector3 world, out Vector3 local)
        {
            local = default;
            if (m_warehouseManager == null)
            {
                return false;
            }

            local = m_warehouseManager.transform.InverseTransformPoint(world);
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
