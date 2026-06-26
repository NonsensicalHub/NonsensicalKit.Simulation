using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>堆垛机驶向/取货/移动/放货子任务的目标坐标（起点在录制时按当前排/层动态解析）。</summary>
    internal readonly struct StackerPhasePoses
    {
        public readonly int RailColumn;
        public readonly GridIndex ApproachTo;
        public readonly GridIndex PickFrom;
        public readonly GridIndex PickTo;
        public readonly GridIndex MoveFrom;
        public readonly GridIndex MoveTo;
        public readonly GridIndex PlaceFrom;
        public readonly GridIndex PlaceTo;

        public StackerPhasePoses(
            int railColumn,
            GridIndex approachTo,
            GridIndex pickFrom,
            GridIndex pickTo,
            GridIndex moveFrom,
            GridIndex moveTo,
            GridIndex placeFrom,
            GridIndex placeTo)
        {
            RailColumn = railColumn;
            ApproachTo = approachTo;
            PickFrom = pickFrom;
            PickTo = pickTo;
            MoveFrom = moveFrom;
            MoveTo = moveTo;
            PlaceFrom = placeFrom;
            PlaceTo = placeTo;
        }
    }

    /// <summary>按仿真规划结果生成堆垛机各阶段子任务的回放坐标。</summary>
    internal static class StackerSubTaskPoseBuilder
    {
        private const int PickupLevel = 0;

        public static StackerPhasePoses Build(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            WarehouseJob job,
            in SimConveyorMapNode pickupNode)
        {
            var stackerId = job.AssignedStackerId;
            var railColumn = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(pickupNode, bindings, topology);
            var pickupColumn = SimConveyorNodeBinding.ResolvePickupColumn(pickupNode, bindings, topology);
            var pickupRow = pickupNode.PickupRow;
            var slot = job.TargetSlot;

            StackerColumnReachUtility.TryGetDefinition(bindings, topology, stackerId, out var definition);
            var clampedSlotColumn = StackerColumnReachUtility.ClampColumn(definition, slot.Column);
            var clampedPickupColumn = StackerColumnReachUtility.ClampColumn(definition, pickupColumn);

            if (job.Direction == SimFlowDirection.Outbound)
            {
                return new StackerPhasePoses(
                    railColumn,
                    CarriagePose(slot.Level, railColumn, slot.Row),
                    CarriagePose(slot.Level, railColumn, slot.Row),
                    ForkPose(slot.Level, clampedSlotColumn, slot.Row),
                    CarriagePose(slot.Level, railColumn, slot.Row),
                    CarriagePose(PickupLevel, railColumn, pickupRow),
                    ForkPose(PickupLevel, clampedPickupColumn, pickupRow),
                    CarriagePose(PickupLevel, railColumn, pickupRow));
            }

            return new StackerPhasePoses(
                railColumn,
                CarriagePose(PickupLevel, railColumn, pickupRow),
                CarriagePose(PickupLevel, railColumn, pickupRow),
                ForkPose(PickupLevel, clampedPickupColumn, pickupRow),
                CarriagePose(PickupLevel, railColumn, pickupRow),
                CarriagePose(slot.Level, railColumn, slot.Row),
                ForkPose(slot.Level, clampedSlotColumn, slot.Row),
                CarriagePose(slot.Level, railColumn, slot.Row));
        }

        public static GridIndex ResolveCarriagePose(
            StackerCarriageBookkeeper carriage,
            int stackerId,
            int railColumn)
        {
            var (row, level) = carriage?.GetCarriageAt(stackerId) ?? (0, 0);
            return CarriagePose(level, railColumn, row);
        }

        public static bool CarriagePosesEqual(in GridIndex a, in GridIndex b) =>
            a.Level == b.Level && a.Column == b.Column && a.Row == b.Row;

        private static GridIndex CarriagePose(int level, int railColumn, int row) =>
            new(level, railColumn, row);

        private static GridIndex ForkPose(int level, int column, int row) =>
            new(level, column, row);
    }
}
