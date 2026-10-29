namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>从入库口到取货点的输送路径与取货点择优策略。</summary>
    public enum ConveyorRoutingStrategy
    {
        /// <summary>选输送时间最短的路径与取货点。</summary>
        ShortestDistance,

        /// <summary>与最短输送时间相同。路段占用由输送内核调度，不再按预约表加权。</summary>
        LeastCongestion,

        /// <summary>优先使用任务已分配堆垛机对应的取货点，再比输送时间。</summary>
        PreferAssignedStacker,
    }
}
