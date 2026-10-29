namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>从入库口到取货点的输送路径与取货点择优策略。</summary>
    public enum ConveyorRoutingStrategy
    {
        /// <summary>选输送时间最短的路径与取货点。</summary>
        ShortestDistance = 0,

        /// <summary>优先使用任务已分配堆垛机对应的取货点，再比输送时间。</summary>
        /// <remarks>取值固定为 2，兼容旧资产中曾占用 1 的已删除 LeastCongestion。</remarks>
        PreferAssignedStacker = 2,
    }
}
