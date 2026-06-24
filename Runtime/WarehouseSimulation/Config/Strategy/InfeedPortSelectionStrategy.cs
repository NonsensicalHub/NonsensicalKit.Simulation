namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>多入库口 / 出库口时下一箱货物选择哪个端口。</summary>
    public enum InfeedPortSelectionStrategy
    {
        /// <summary>在可用端口间轮询分配。</summary>
        RoundRobin,

        /// <summary>优先选择当前预定/排队最少的端口。</summary>
        ShortestQueue,
    }
}
