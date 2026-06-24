namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>
    /// 仓库任务在离散事件仿真中的生命周期状态。
    /// <para>入库：<c>PendingArrival → WaitingInfeed → OnConveyor → WaitingStacker → StackerApproach/Pick/Move/Place → Completed</c></para>
    /// <para>出库：<c>PendingArrival → WaitingStacker → StackerApproach/Pick/Move/Place → OnConveyor → WaitingOutfeed → Completed</c></para>
    /// <para>异常：<see cref="FailedNoSlot"/> / <see cref="FailedNoCargo"/>。</para>
    /// </summary>
    public enum WarehouseJobState
    {
        /// <summary>已创建，尚未开始入库口服务。</summary>
        PendingArrival,

        /// <summary>在入库口排队或接受扫描服务（服务结束后进入输送）。</summary>
        WaitingInfeed,

        /// <summary>在输送网上按 zone 链移动。</summary>
        OnConveyor,

        /// <summary>已到达取货点，等待堆垛机资源。</summary>
        WaitingStacker,

        /// <summary>堆垛机正从当前位置驶向取货点（入库）或源货位（出库）。</summary>
        StackerApproach,

        /// <summary>堆垛机正在取货（对应 <c>StackerPickComplete</c> 之前）。</summary>
        StackerPick,

        /// <summary>堆垛机正在向目标货位列移动。</summary>
        StackerMove,

        /// <summary>堆垛机正在目标货位放货。</summary>
        StackerPlace,

        /// <summary>入库流程成功结束。</summary>
        Completed,

        /// <summary>在出库口排队或接受发运服务。</summary>
        WaitingOutfeed,

        /// <summary>无空货位或无法规划输送路径。</summary>
        FailedNoSlot,

        /// <summary>出库时无可用库存或无法规划输送路径。</summary>
        FailedNoCargo,
    }
}
