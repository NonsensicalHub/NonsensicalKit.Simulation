using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    internal readonly struct StackerWorkPlan
    {
        public readonly float ApproachSeconds;
        public readonly float PickSeconds;
        public readonly float MoveSeconds;
        public readonly float PlaceSeconds;
        public readonly int EndRow;
        public readonly int EndLevel;

        public StackerWorkPlan(
            float approachSeconds,
            float pickSeconds,
            float moveSeconds,
            float placeSeconds,
            int endRow,
            int endLevel)
        {
            ApproachSeconds = approachSeconds;
            PickSeconds = pickSeconds;
            MoveSeconds = moveSeconds;
            PlaceSeconds = placeSeconds;
            EndRow = endRow;
            EndLevel = endLevel;
        }

        public float TotalSeconds => ApproachSeconds + PickSeconds + MoveSeconds + PlaceSeconds;
    }

    /// <summary>按堆垛机当前排/层位置估算驶向取货点或货位后的取/移/放时长。</summary>
    internal static class StackerWorkPlanner
    {
        public static StackerWorkPlan PlanInbound(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            StackerCarriageBookkeeper carriage,
            WarehouseJob job,
            in SimConveyorMapNode pickupNode)
        {
            var stackerId = job.AssignedStackerId >= 0 ? job.AssignedStackerId : pickupNode.StackerId;
            var kinematics = StackerKinematicsResolver.Resolve(bindings, topology, stackerId);
            var railColumn = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(
                pickupNode, bindings, topology);
            var pickupCol = SimConveyorNodeBinding.ResolvePickupColumn(pickupNode, bindings, topology);
            var pickupAnchor = new GridIndex(0, pickupCol, pickupNode.PickupRow, 0);

            var (fromRow, fromLevel) = carriage?.GetCarriageAt(stackerId) ?? (0, 0);
            var approach = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, fromRow, fromLevel, pickupAnchor, isLoaded: false);
            var move = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, pickupNode.PickupRow, 0, job.TargetSlot, isLoaded: true);

            return new StackerWorkPlan(
                approach,
                kinematics.PickSeconds,
                move,
                kinematics.PlaceSeconds,
                job.TargetSlot.Row,
                job.TargetSlot.Level);
        }

        public static StackerWorkPlan PlanOutbound(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            StackerCarriageBookkeeper carriage,
            WarehouseJob job,
            in SimConveyorMapNode pickupNode)
        {
            var stackerId = job.AssignedStackerId;
            var kinematics = StackerKinematicsResolver.Resolve(bindings, topology, stackerId);
            var railColumn = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(
                pickupNode, bindings, topology);
            var pickupCol = SimConveyorNodeBinding.ResolvePickupColumn(pickupNode, bindings, topology);
            var pickupAnchor = new GridIndex(0, pickupCol, pickupNode.PickupRow, 0);

            var (fromRow, fromLevel) = carriage?.GetCarriageAt(stackerId) ?? (0, 0);
            var approach = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, fromRow, fromLevel, job.TargetSlot, isLoaded: false);
            var move = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, job.TargetSlot.Row, job.TargetSlot.Level, pickupAnchor, isLoaded: true);

            return new StackerWorkPlan(
                approach,
                kinematics.PickSeconds,
                move,
                kinematics.PlaceSeconds,
                pickupNode.PickupRow,
                0);
        }

        private static float ComputeMoveSeconds(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId,
            int railColumn,
            int fromRow,
            int fromLevel,
            GridIndex target,
            bool isLoaded) =>
            ConveyorPathPlanner.ComputeMoveSecondsFrom(
                bindings, topology, stackerId, railColumn, fromRow, fromLevel, target, isLoaded);
    }
}
