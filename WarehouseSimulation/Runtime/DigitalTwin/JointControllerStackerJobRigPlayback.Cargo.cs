using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private void AttachCargoByJob(int jobId)
        {
            if (_cargoRegistry == null)
            {
                return;
            }

            var cargo = _cargoRegistry.Acquire(jobId);
            if (cargo == null)
            {
                return;
            }

            var mount = ResolveCargoMount();
            if (mount == null)
            {
                return;
            }

            cargo.gameObject.SetActive(true);
            cargo.SetParent(mount, true);
        }

        private void ReleaseCargoByJob(int jobId) => _cargoRegistry?.Release(jobId);

        private void PlaceCargoAtSlotByJob(int jobId, GridIndex slot)
        {
            if (_cargoRegistry == null)
            {
                return;
            }

            // 存储货位由 WarehouseManager 占用态渲染；回收料箱实例避免与 GPU 货位重复显示。
            if (IsStorageSlot(slot))
            {
                _cargoRegistry.Release(jobId);
                return;
            }

            if (!_cargoRegistry.TryGet(jobId, out var cargo))
            {
                return;
            }

            cargo.SetParent(null, true);
            if (_positionIndex.TryGetWorldPosition(slot, out var world))
            {
                cargo.position = world;
            }

            cargo.gameObject.SetActive(true);
        }

        private Transform ResolveCargoMount()
        {
            if (m_cargoMount != null)
            {
                return m_cargoMount;
            }

            if (m_jointController != null
                && StackerJointAxisMap.TryGetRowJointNode(m_jointController, out var rowNode))
            {
                return rowNode;
            }

            return null;
        }

        private bool IsStorageSlot(GridIndex slot) =>
            _grid == null
            || SlotGridUtility.IsStorageSlot(_grid, slot.Level, slot.Column, slot.Row, _exclusionZones);
    }
}
