using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class JointControllerStackerJobRigPlayback
    {
        private bool TryResolveTargetGrid(
            in StackerJobPlaybackTask task,
            out GridIndex target,
            bool outboundPickup)
        {
            if (outboundPickup)
            {
                return TryResolvePickupGridFromJob(in task, out target);
            }

            if (task.IsOutbound)
            {
                target = task.TargetSlot;
                return true;
            }

            return TryResolvePickupGridFromJob(in task, out target);
        }

        private void SyncPickupSideFromJob(in StackerJobPlaybackTask task)
        {
            _activeAisleLeftColumn = _definition.AisleLeftColumn;
            if (!TryGetPickupNodeFromJob(in task, out var node))
            {
                if (_railColumn != _activeAisleLeftColumn)
                {
                    _railColumn = _activeAisleLeftColumn;
                    CalibrateJointInitialValues();
                }

                return;
            }

            // 巷道左列可为 0（二号库堆垛机 8），不能再用 > 0 判断。
            var aisle = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(node, _bindings, _topology);
            _activeAisleLeftColumn = aisle;
            if (_railColumn == _activeAisleLeftColumn)
            {
                return;
            }

            _railColumn = _activeAisleLeftColumn;
            CalibrateJointInitialValues();
        }

        private bool TryResolvePickupGridFromJob(in StackerJobPlaybackTask task, out GridIndex pickup)
        {
            pickup = default;
            if (!TryGetPickupNodeFromJob(in task, out var node))
            {
                return false;
            }

            var pickupColumn = SimConveyorNodeBinding.ResolvePickupColumn(node, _bindings, _topology);
            // 地图交互层优先；未标（0）时用 Handler 上的取货默认层。
            var level = node.PickupLevel > 0 ? node.PickupLevel : System.Math.Max(0, _pickupLevel);
            pickup = new GridIndex(level, pickupColumn, node.PickupRow, task.TargetSlot.Depth);
            return true;
        }

        private bool TryGetPickupNodeFromJob(in StackerJobPlaybackTask task, out SimConveyorMapNode node)
        {
            node = default;
            if (task.PickupPointIndex < 0)
            {
                return false;
            }

            var map = _bindings.ConveyorMap;
            if (map?.Nodes == null || task.PickupPointIndex >= map.Nodes.Length)
            {
                return false;
            }

            node = map.Nodes[task.PickupPointIndex];
            return true;
        }
    }
}
