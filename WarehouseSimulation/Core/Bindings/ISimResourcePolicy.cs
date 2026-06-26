namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>仿真资源并发策略（预定上限、互斥开关、服务时间）。</summary>
    public interface ISimResourcePolicy
    {
        int MaxInfeedReservationsPerPort { get; }
        float InfeedServiceSeconds { get; }
        int MaxOutfeedReservationsPerPort { get; }

        /// <summary>同一出库口前输送路段上允许排队的最大箱数（含正发往出库口的在途箱）。</summary>
        int MaxOutfeedQueuePerPort { get; }

        float OutfeedServiceSeconds { get; }
        float ProcessStationServiceSeconds { get; }
        float VerticalTransferSeconds { get; }
        float OccupancyNotifyDelaySeconds { get; }
        int MaxPickupReservationsPerPoint { get; }
        bool UseAisleColumnReservation { get; }
        bool UseStackerReservation { get; }
        int MaxSimEvents { get; }
    }
}
