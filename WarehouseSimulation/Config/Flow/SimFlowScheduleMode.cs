namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>流程计划中一批货物的释放节奏。</summary>
    public enum SimFlowScheduleMode
    {
        /// <summary>在起始时刻一次性释放全部数量。</summary>
        Instant = 0,

        /// <summary>在随机间隔内释放随机数量；上下限相等时即为固定间隔与固定批量。</summary>
        Staggered = 1,
    }
}
