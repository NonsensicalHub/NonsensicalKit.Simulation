namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>子任务类型分组，供仿真记录、回放与报告共用。</summary>
    public static class SimSubTaskKinds
    {
        public static bool IsJunction(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.JunctionEnter
                or SimSubTaskKind.JunctionWait
                or SimSubTaskKind.JunctionExit
                or SimSubTaskKind.ProcessStationService;

        /// <summary>输送时间轴上的运动/等待（不含入库口静止放货）。</summary>
        public static bool ExtendsConveyorTimeline(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.InfeedMove
                or SimSubTaskKind.OutboundMove
                or SimSubTaskKind.SegmentTransit
                or SimSubTaskKind.SegmentHopMove
                or SimSubTaskKind.SegmentStopDwell
                or SimSubTaskKind.JunctionEnter
                or SimSubTaskKind.JunctionWait
                or SimSubTaskKind.JunctionExit
                or SimSubTaskKind.ProcessStationService
                or SimSubTaskKind.VerticalTransferMove;

        /// <summary>加工站点服务（进入后等待加工完成）。</summary>
        public static bool IsProcessStationService(SimSubTaskKind kind) =>
            kind == SimSubTaskKind.ProcessStationService;

        /// <summary>垂直提升机跨层移动。</summary>
        public static bool IsVerticalTransferMove(SimSubTaskKind kind) =>
            kind == SimSubTaskKind.VerticalTransferMove;

        /// <summary>应按输送路段/预约表定位料箱的子任务。</summary>
        public static bool UsesConveyorPlacement(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.InfeedMove
                or SimSubTaskKind.OutboundMove
                or SimSubTaskKind.SegmentQueue
                or SimSubTaskKind.SegmentTransit
                or SimSubTaskKind.SegmentHopMove
                or SimSubTaskKind.SegmentStopDwell
                or SimSubTaskKind.JunctionEnter
                or SimSubTaskKind.JunctionWait
                or SimSubTaskKind.JunctionExit
                or SimSubTaskKind.ProcessStationService
                or SimSubTaskKind.VerticalTransferMove;

        /// <summary>按路段 zone 插值（不走路段表主路径）。</summary>
        public static bool UsesZoneInterpolation(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.InfeedMove
                or SimSubTaskKind.OutboundMove
                or SimSubTaskKind.SegmentHopMove
                or SimSubTaskKind.SegmentStopDwell;

        /// <summary>SegmentHopMove 的末段：从路段 slot-0 驶入取货点（<see cref="SimSubTask.SegmentSlotIndex"/> = -1）。</summary>
        public static bool IsPickupArrivalHop(in SimSubTask task) =>
            task.Kind == SimSubTaskKind.SegmentHopMove && task.SegmentSlotIndex < 0;

        /// <summary>OutboundMove：从堆垛机交互点驶入首段入口停留点。</summary>
        public static bool IsOutboundPickupDepartMove(in SimSubTask task) =>
            task.Kind == SimSubTaskKind.OutboundMove;

        /// <summary>出库交互点排队等待（<see cref="SimSubTask.SegmentSlotIndex"/> = -1）。</summary>
        public static bool IsOutboundPickupQueue(in SimSubTask task) =>
            task.Kind == SimSubTaskKind.SegmentQueue
            && task.PickupPointIndex >= 0
            && task.SegmentSlotIndex < 0;

        /// <summary>无法从路段表定位时，可回退显示在入库口。</summary>
        public static bool CanHoldAtInfeed(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.InfeedPlace
                or SimSubTaskKind.InfeedMove
                or SimSubTaskKind.SegmentQueue;

        /// <summary>
        /// 取货完成至放货完成之间：料箱应挂在堆垛机叉上，由堆垛机回放接管可视，输送线回放不应 Detach/Hide。
        /// </summary>
        public static bool IsStackerForkCargoWindow(in SimSubTask active, double simTime) =>
            active.Kind switch
            {
                SimSubTaskKind.StackerPick => active.NormalizedProgress(simTime) >= 0.999f,
                SimSubTaskKind.StackerMove => true,
                SimSubTaskKind.StackerPlace => active.NormalizedProgress(simTime) < 0.999f,
                _ => false,
            };
    }
}
