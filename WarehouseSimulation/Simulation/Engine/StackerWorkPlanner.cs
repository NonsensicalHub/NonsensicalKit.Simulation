using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 一项已算完的堆垛机作业。时长、阶段端点和伸叉参数来自同一次规划，录制时不得另算起点。
    /// </summary>
    internal readonly struct StackerWorkPlan
    {
        public readonly float ApproachSeconds;
        public readonly float PickSeconds;
        public readonly float MoveSeconds;
        public readonly float PlaceSeconds;
        public readonly int StartRow;
        public readonly int StartLevel;
        public readonly int EndRow;
        public readonly int EndLevel;
        public readonly int WarehouseId;
        public readonly StackerPhasePoses Poses;
        public readonly bool PickUsesForkDepth;
        public readonly float PickForkDepthMeters;
        public readonly bool PlaceUsesForkDepth;
        public readonly float PlaceForkDepthMeters;

        public StackerWorkPlan(
            float approachSeconds,
            float pickSeconds,
            float moveSeconds,
            float placeSeconds,
            int startRow,
            int startLevel,
            int endRow,
            int endLevel,
            int warehouseId,
            StackerPhasePoses poses,
            bool pickUsesForkDepth,
            float pickForkDepthMeters,
            bool placeUsesForkDepth,
            float placeForkDepthMeters)
        {
            ApproachSeconds = approachSeconds;
            PickSeconds = pickSeconds;
            MoveSeconds = moveSeconds;
            PlaceSeconds = placeSeconds;
            StartRow = startRow;
            StartLevel = startLevel;
            EndRow = endRow;
            EndLevel = endLevel;
            WarehouseId = warehouseId;
            Poses = poses;
            PickUsesForkDepth = pickUsesForkDepth;
            PickForkDepthMeters = pickForkDepthMeters;
            PlaceUsesForkDepth = placeUsesForkDepth;
            PlaceForkDepthMeters = placeForkDepthMeters;
        }

        public float TotalSeconds => ApproachSeconds + PickSeconds + MoveSeconds + PlaceSeconds;

        public GridIndex StartPose =>
            new(StartLevel, Poses.RailColumn, StartRow, 0, WarehouseId);
    }

    /// <summary>按已提交计划尾（不是当前物理位置）生成唯一作业计划。</summary>
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
            var warehouseId = ResolveWarehouseId(bindings, topology, stackerId, job.TargetSlot.WarehouseId);
            var pickupLevel = System.Math.Max(0, pickupNode.PickupLevel);
            var pickupAnchor = new GridIndex(pickupLevel, pickupCol, pickupNode.PickupRow, 0, warehouseId);
            var (fromRow, fromLevel) = carriage?.GetPlanningOrigin(stackerId) ?? (0, 0);
            var approach = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, fromRow, fromLevel, pickupAnchor, isLoaded: false);
            var move = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, pickupNode.PickupRow, pickupLevel, WithWarehouse(job.TargetSlot, warehouseId), isLoaded: true);
            var poses = StackerSubTaskPoseBuilder.Build(
                bindings, topology, job, pickupNode, warehouseId);
            var usesForkDepth = SimConveyorNodeBinding.HasForkDepth(in pickupNode);

            return new StackerWorkPlan(
                approach,
                kinematics.PickSeconds,
                move,
                kinematics.PlaceSeconds,
                fromRow,
                fromLevel,
                job.TargetSlot.Row,
                job.TargetSlot.Level,
                warehouseId,
                poses,
                pickUsesForkDepth: usesForkDepth,
                pickForkDepthMeters: usesForkDepth ? pickupNode.ForkDepthMeters : 0f,
                placeUsesForkDepth: false,
                placeForkDepthMeters: 0f);
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
            var warehouseId = ResolveWarehouseId(bindings, topology, stackerId, job.TargetSlot.WarehouseId);
            var pickupLevel = System.Math.Max(0, pickupNode.PickupLevel);
            var pickupAnchor = new GridIndex(pickupLevel, pickupCol, pickupNode.PickupRow, 0, warehouseId);
            var (fromRow, fromLevel) = carriage?.GetPlanningOrigin(stackerId) ?? (0, 0);
            var slot = WithWarehouse(job.TargetSlot, warehouseId);
            var approach = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, fromRow, fromLevel, slot, isLoaded: false);
            var move = ComputeMoveSeconds(
                bindings, topology, stackerId, railColumn, slot.Row, slot.Level, pickupAnchor, isLoaded: true);
            var poses = StackerSubTaskPoseBuilder.Build(
                bindings, topology, job, pickupNode, warehouseId);
            var usesForkDepth = SimConveyorNodeBinding.HasForkDepth(in pickupNode);

            return new StackerWorkPlan(
                approach,
                kinematics.PickSeconds,
                move,
                kinematics.PlaceSeconds,
                fromRow,
                fromLevel,
                pickupNode.PickupRow,
                pickupLevel,
                warehouseId,
                poses,
                pickUsesForkDepth: false,
                pickForkDepthMeters: 0f,
                placeUsesForkDepth: usesForkDepth,
                placeForkDepthMeters: usesForkDepth ? pickupNode.ForkDepthMeters : 0f);
        }

        private static GridIndex WithWarehouse(GridIndex slot, int warehouseId) =>
            slot.WarehouseId != 0 || warehouseId == 0
                ? slot
                : new GridIndex(slot.Level, slot.Column, slot.Row, slot.Depth, warehouseId);

        /// <summary>货位上的仓库号优先；为 0 时用堆垛机定义上的仓库，避免同号排层落到默认库。</summary>
        private static int ResolveWarehouseId(
            IWarehouseSimulationBindings bindings,
            ConveyorMapTopology topology,
            int stackerId,
            int slotWarehouseId) =>
            slotWarehouseId != 0
                ? slotWarehouseId
                : StackerColumnReachUtility.GetWarehouseId(bindings, topology, stackerId);

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
