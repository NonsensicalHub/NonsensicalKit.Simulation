using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>堆垛机驶向/取货/移动/放货的阶段端点。驶向起点由作业计划的计划尾给出，不在这里再读当前位置。</summary>
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
        public static StackerPhasePoses Build(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            WarehouseJob job,
            in SimConveyorMapNode pickupNode,
            int warehouseId)
        {
            var stackerId = job.AssignedStackerId;
            var railColumn = SimConveyorNodeBinding.ResolvePickupAisleLeftColumn(pickupNode, bindings, topology);
            var pickupColumn = SimConveyorNodeBinding.ResolvePickupColumn(pickupNode, bindings, topology);
            var pickupRow = pickupNode.PickupRow;
            var pickupLevel = System.Math.Max(0, pickupNode.PickupLevel);
            var slot = job.TargetSlot;

            if (!StackerColumnReachUtility.TryGetDefinition(bindings, topology, stackerId, out var definition))
            {
                definition = new SimStackerDefinition
                {
                    StackerId = stackerId,
                    ColumnReach = bindings != null
                        ? bindings.DefaultStackerColumnReach
                        : SimStackerColumnReach.TwoColumns,
                };
            }

            var clampedSlotColumn = StackerColumnReachUtility.ClampColumn(definition, slot.Column);
            // 交互参考列允许落在货位列域对侧浅位；取货伸叉姿态保留参考列，勿钳回唯一货位列。
            var pickupForkColumn = StackerColumnReachUtility.CanUsePickupReferenceColumn(in definition, pickupColumn)
                ? pickupColumn
                : StackerColumnReachUtility.ClampColumn(definition, pickupColumn);

            if (job.Direction == SimFlowDirection.Outbound)
            {
                // 出库放货与入库对称：Place 从 Carriage 伸到 Fork，段内再收回。
                return new StackerPhasePoses(
                    railColumn,
                    CarriagePose(slot.Level, railColumn, slot.Row, warehouseId),
                    CarriagePose(slot.Level, railColumn, slot.Row, warehouseId),
                    ForkPose(slot.Level, clampedSlotColumn, slot.Row, warehouseId),
                    CarriagePose(slot.Level, railColumn, slot.Row, warehouseId),
                    CarriagePose(pickupLevel, railColumn, pickupRow, warehouseId),
                    CarriagePose(pickupLevel, railColumn, pickupRow, warehouseId),
                    ForkPose(pickupLevel, pickupForkColumn, pickupRow, warehouseId));
            }

            // 入库放货：从巷道中心收回位 Absolute 伸到货位列（含左浅位列号=轨道列；Rig 显式区分收回/伸叉）。
            // 交互点取货侧向只认配置 ForkDepthMeters 原样，不在此改记。
            return new StackerPhasePoses(
                railColumn,
                CarriagePose(pickupLevel, railColumn, pickupRow, warehouseId),
                CarriagePose(pickupLevel, railColumn, pickupRow, warehouseId),
                ForkPose(pickupLevel, pickupForkColumn, pickupRow, warehouseId),
                CarriagePose(pickupLevel, railColumn, pickupRow, warehouseId),
                CarriagePose(slot.Level, railColumn, slot.Row, warehouseId),
                CarriagePose(slot.Level, railColumn, slot.Row, warehouseId),
                ForkPose(slot.Level, clampedSlotColumn, slot.Row, warehouseId));
        }

        public static GridIndex ResolveCarriagePose(
            StackerCarriageBookkeeper carriage,
            int stackerId,
            int railColumn,
            int warehouseId = 0)
        {
            var (row, level) = carriage?.GetPlanningOrigin(stackerId) ?? (0, 0);
            return CarriagePose(level, railColumn, row, warehouseId);
        }

        public static bool CarriagePosesEqual(in GridIndex a, in GridIndex b) =>
            a.WarehouseId == b.WarehouseId
            && a.Level == b.Level
            && a.Column == b.Column
            && a.Row == b.Row;

        private static GridIndex CarriagePose(int level, int railColumn, int row, int warehouseId) =>
            new(level, railColumn, row, 0, warehouseId);

        private static GridIndex ForkPose(int level, int column, int row, int warehouseId) =>
            new(level, column, row, 0, warehouseId);
    }
}
