namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>堆垛机货位放置策略。</summary>
    public enum StackerSlotPlacementStrategy
    {
        /// <summary>距取货点（列/排/层）综合距离更近的货位优先。</summary>
        NearestToPickup,

        /// <summary>按列填满：先放满一列（所有排、层）再换下一列。</summary>
        FillColumnFirst,
    }
}
