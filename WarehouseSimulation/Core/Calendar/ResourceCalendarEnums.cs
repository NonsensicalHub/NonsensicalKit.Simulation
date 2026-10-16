namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar
{
    /// <summary>统一资源种类（阶段2日历；展示/调度读同一事实）。</summary>
    public enum ResourceKind
    {
        ConveyorZone = 0,
        DirectionLock = 1,
        JunctionMovement = 2,
        ProcessStation = 3,
        StationTraversal = 4,
        StationExclusiveWithService = 5,
        BufferSlot = 6,
        Port = 7,
        TransferDevice = 8,
        MovementConflictGroup = 9,
    }

    /// <summary>共享可逆段上的流动方向（DirectionLock 兼容性用）。</summary>
    public enum TravelDirection
    {
        None = 0,
        Forward = 1,
        Reverse = 2,
    }

    public enum ResourceCalendarErrorCode
    {
        Ok = 0,
        InvalidWindow = 1,
        UnknownResource = 2,
        CapacityExceeded = 3,
        DirectionConflict = 4,
        ConflictGroupBusy = 5,
        DuplicateResourceId = 6,
        ReservationNotFound = 7,
        PartialBatchRejected = 8,
        NullRequest = 9,
    }
}
