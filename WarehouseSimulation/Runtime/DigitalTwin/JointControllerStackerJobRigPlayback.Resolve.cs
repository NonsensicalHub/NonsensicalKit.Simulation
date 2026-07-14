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
                _railColumn = _activeAisleLeftColumn;
                return;
            }

            var aisle = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(node, _bindings, _topology);
            if (aisle > 0)
            {
                _activeAisleLeftColumn = aisle;
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
            pickup = new GridIndex(_pickupLevel, pickupColumn, node.PickupRow, task.TargetSlot.Depth);
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
