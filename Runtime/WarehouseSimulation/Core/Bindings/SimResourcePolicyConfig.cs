using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>仿真资源并发策略。</summary>
    [Serializable]
    public sealed class SimResourcePolicyConfig : ISimResourcePolicy
    {
        [Label("入库口预定上限")]
        public int MaxInfeedReservationsPerPort = 2;

        [Label("入库放货服务时间（秒）")]
        public float InfeedServiceSeconds = 5f;

        [Label("出库口预定上限")]
        [Tooltip("保留字段，出库前路段排队请使用「出库口前排队数量」。")]
        public int MaxOutfeedReservationsPerPort = 2;

        [Label("出库口前排队数量")]
        [Tooltip("同一出库口前输送路段停留点上允许依次排队的最大箱数（含正在出库口发运的箱）。超出则在交互点等待选路。")]
        public int MaxOutfeedQueuePerPort = 3;

        [Label("出库发运服务时间（秒）")]
        public float OutfeedServiceSeconds = 5f;

        [Min(0f)]
        [Label("占用变更通知延迟（秒）")]
        public float OccupancyNotifyDelaySeconds = 0.1f;

        [Label("堆垛机交互点预定上限")]
        public int MaxPickupReservationsPerPoint = 2;

        [Label("巷道列互斥")]
        public bool UseAisleColumnReservation = true;

        [Label("堆垛机互斥")]
        public bool UseStackerReservation = true;

        [Label("最大仿真事件数")]
        public int MaxSimEvents = 5_000_000;

        int ISimResourcePolicy.MaxInfeedReservationsPerPort => MaxInfeedReservationsPerPort;
        float ISimResourcePolicy.InfeedServiceSeconds => InfeedServiceSeconds;
        int ISimResourcePolicy.MaxOutfeedReservationsPerPort => MaxOutfeedReservationsPerPort;
        int ISimResourcePolicy.MaxOutfeedQueuePerPort => MaxOutfeedQueuePerPort;
        float ISimResourcePolicy.OutfeedServiceSeconds => OutfeedServiceSeconds;
        float ISimResourcePolicy.OccupancyNotifyDelaySeconds => OccupancyNotifyDelaySeconds;
        int ISimResourcePolicy.MaxPickupReservationsPerPoint => MaxPickupReservationsPerPoint;
        bool ISimResourcePolicy.UseAisleColumnReservation => UseAisleColumnReservation;
        bool ISimResourcePolicy.UseStackerReservation => UseStackerReservation;
        int ISimResourcePolicy.MaxSimEvents => MaxSimEvents;

        public static SimResourcePolicyConfig CreateDefault() => new();
    }
}
