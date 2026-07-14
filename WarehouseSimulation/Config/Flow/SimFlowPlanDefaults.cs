namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>常用流程计划片段工厂。</summary>
    public static class SimFlowPlanDefaults
    {
        /// <summary>单段入库、瞬间到达。</summary>
        public static SimFlowPlanEntry InstantInbound(int quantity) => new()
        {
            Direction = SimFlowDirection.Inbound,
            Quantity = quantity,
            ScheduleMode = SimFlowScheduleMode.Instant,
        };

        /// <summary>单段出库、瞬间到达。</summary>
        public static SimFlowPlanEntry InstantOutbound(int quantity) => new()
        {
            Direction = SimFlowDirection.Outbound,
            Quantity = quantity,
            ScheduleMode = SimFlowScheduleMode.Instant,
        };

        /// <summary>单段入库、分次到达（上下限相等时为固定间隔与固定批量）。</summary>
        public static SimFlowPlanEntry StaggeredInbound(
            int quantity,
            float intervalMinSeconds,
            float intervalMaxSeconds,
            int quantityMin,
            int quantityMax) => new()
        {
            Direction = SimFlowDirection.Inbound,
            Quantity = quantity,
            ScheduleMode = SimFlowScheduleMode.Staggered,
            RandomIntervalMinSeconds = intervalMinSeconds,
            RandomIntervalMaxSeconds = intervalMaxSeconds,
            RandomQuantityMin = quantityMin,
            RandomQuantityMax = quantityMax,
        };

        /// <summary>未配置流程计划时的运行时默认（100 箱瞬间入库）。</summary>
        public static SimFlowPlanEntry[] DefaultWhenEmpty => new[] { InstantInbound(100) };
    }
}
