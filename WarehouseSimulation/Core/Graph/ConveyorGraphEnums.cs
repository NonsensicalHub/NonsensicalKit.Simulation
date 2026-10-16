namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph
{
    /// <summary>物理段通行模式（与有向弧分层）。</summary>
    public enum ConveyorFlowMode
    {
        FixedOneWay = 0,
        SeparateBidirectionalLanes = 1,
        SharedReversibleLane = 2,
        TransferControlled = 3,
    }

    /// <summary>支路末端 / 加工站返程策略。不可由节点度数静默推断反向能力。</summary>
    public enum ConveyorReturnPolicy
    {
        /// <summary>存在与进站弧共享 PhysicalSegmentId 的反向弧。</summary>
        ReverseSameLane = 0,
        /// <summary>经其它弧回路返回，无掉头步骤。</summary>
        LoopBack = 1,
        /// <summary>转台 / 移载设备换向。</summary>
        TurntableOrTransfer = 2,
        /// <summary>明确不可返回；规划应报 NoLegalReturnRoute。</summary>
        NoReturn = 3,
    }

    /// <summary>无加工直通时的资源策略（阶段1仅记录；预约在阶段2）。</summary>
    public enum StationPassThroughPolicy
    {
        None = 0,
        TraversalOnly = 1,
        ExclusiveWithService = 2,
    }

    public enum ConveyorGraphNodeKind
    {
        Junction = 0,
        InfeedPort = 1,
        PickupPoint = 2,
        OutfeedPort = 3,
        ProcessStation = 4,
        VerticalTransfer = 5,
    }

    /// <summary>地图构建 / 校验错误码。错误时不允许进入仿真。</summary>
    public enum ConveyorGraphErrorCode
    {
        Ok = 0,
        NullOrEmptyMap = 1,
        MissingNodeId = 2,
        DuplicateNodeId = 3,
        MissingLogicalId = 4,
        DuplicateLogicalId = 5,
        MissingArcId = 6,
        DuplicateArcId = 7,
        MissingPhysicalSegmentId = 8,
        DuplicatePhysicalSegmentId = 9,
        ArcEndpointMissing = 10,
        InvalidDistance = 11,
        InvalidSpeed = 12,
        NegativeServiceOrActionTime = 13,
        PhysicalSegmentArcMismatch = 14,
        FixedOneWayMustNotHaveImplicitReverse = 15,
        ProcessTagMissingOnDwellStation = 16,
        NoProcessStationForRequirement = 17,
        DeadEndNoLegalReturn = 18,
        NoLegalReturnRoute = 19,
        UnreachableFromInfeed = 20,
        InvalidMovementRule = 21,
        InvalidPortId = 22,
    }

    /// <summary>路线规划失败码（与图校验码分离）。</summary>
    public enum RoutePlanErrorCode
    {
        Ok = 0,
        NullGraph = 1,
        MissingStartOrGoal = 2,
        StartNodeMissing = 3,
        GoalNodeMissing = 4,
        Unreachable = 5,
        NoLegalReturnRoute = 6,
        NoProcessStationForRequirement = 7,
        ProcessOrderUnsatisfiable = 8,
    }
}
