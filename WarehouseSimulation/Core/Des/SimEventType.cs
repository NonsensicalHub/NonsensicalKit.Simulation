namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 离散事件类型枚举：每个值对应仓库仿真主循环 <c>Dispatch</c> 的一个分支。
    /// </summary>
    /// <remarks>事件按仿真时刻排序处理；同刻事件按枚举序、JobId、Payload 打破平局以保证确定性。</remarks>
    public enum SimEventType
    {
        /// <summary>指定入库口尝试从待放货队列取下一箱（Payload = 入库口序号）。</summary>
        InfeedPortFeed,

        /// <summary>流程计划：释放 1 箱货物到等候队列（Payload = 计划条目下标）。</summary>
        FlowCargoRelease,

        /// <summary>流程计划：按批次模式释放下一批（Payload = 计划条目下标）。</summary>
        FlowPlanBatchRelease,

        /// <summary>流程计划：Instant 模式一次性释放全部数量（Payload = 计划条目下标）。</summary>
        FlowPlanInstantRelease,

        /// <summary>入库口服务结束，进入输送阶段。</summary>
        InfeedServiceComplete,
        /// <summary>货箱尾端离开入库口碰撞区，释放入库口物理占用。</summary>
        InfeedPortPhysicalRelease,

        /// <summary>出库口服务结束，任务完成。</summary>
        OutfeedServiceComplete,

        /// <summary>货箱尾端离开出库口碰撞区，释放出库口物理占用。</summary>
        OutfeedPortPhysicalRelease,

        /// <summary>出库口尝试从等候队列取下一箱（Payload = 出库口序号；<c>-1</c> 为合并等候唤醒）。</summary>
        OutfeedPortDispatch,

        /// <summary>资源占用结束（Payload = 通知序号，见 OccupancyNotifyResources）。</summary>
        OccupancyReleased,
        /// <summary>取货点预定未满后重试输送路径规划。</summary>
        ConveyorRouteRetry,
        /// <summary>输送路径中单段结束，推进下一段（Payload = 已完成段下标）。</summary>
        ConveyorSegmentComplete,
        /// <summary>输送路径中单个 zone 结束，推进下一 zone（Payload = 已完成 zone 下标）。</summary>
        ConveyorZoneComplete,
        /// <summary>输送路径全部 zone 完成，货箱到达取货点。</summary>
        ConveyorTransitComplete,

        /// <summary>堆垛机驶向作业点结束，进入取货阶段。</summary>
        StackerApproachComplete,

        /// <summary>堆垛机取货动作结束，进入移动阶段。</summary>
        StackerPickComplete,

        /// <summary>堆垛机移动至目标列结束，进入放货阶段。</summary>
        StackerMoveComplete,

        /// <summary>堆垛机放货结束，货位正式占用。</summary>
        StackerPlaceComplete,

        /// <summary>出库货箱离开堆垛机交互点，释放堆垛机负载并尝试派下一出库任务。</summary>
        OutboundPickupDeparture,

        /// <summary>整单完成，写入统计。</summary>
        JobCompleted,
    }
}
