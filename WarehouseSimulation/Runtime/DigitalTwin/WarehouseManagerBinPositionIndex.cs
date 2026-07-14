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

        public bool IsReady => ResolvePositions().IsReady;

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
            if (m_bindings != null)
            {
                m_bindings.EnsureSlotPositionsLoaded();
                if (m_bindings.SlotPositions.IsReady)
                {
                    return m_bindings.SlotPositions;
                }
            }

            return SlotPositionIndex.TryLoadFromStreamingAssets(m_warehouseName)
                ?? new SlotPositionIndex();
        }
    }
}
