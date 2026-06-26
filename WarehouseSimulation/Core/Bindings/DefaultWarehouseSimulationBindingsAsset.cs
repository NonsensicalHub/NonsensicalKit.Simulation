using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 默认硬件绑定资产：模块化配置块 + 输送地图，供 Scenario 引用。
    /// </summary>
    [CreateAssetMenu(
        fileName = "WarehouseBindings",
        menuName = "Warehouse Simulation/Bindings Asset")]
    public sealed class DefaultWarehouseSimulationBindingsAsset : WarehouseSimulationBindingsAsset, IWarehouseSimulationBindings
    {
        [Header("货位坐标")]
        [Label("仓库数据名（不含扩展名）")]
        [Tooltip("StreamingAssets/Warehouse/{名称}.dat，与 WarehouseManager 共用。")]
        public string WarehouseDataName = "SimulationTest";

        [Header("输送地图")]
        [Label("输送地图")]
        public WarehouseConveyorMap ConveyorMap;

        [Header("堆垛机 Fleet")]
        [Label("Fleet 拓扑")]
        public StackerFleetConfig Fleet = new();

        [Header("资源策略")]
        [Label("资源并发策略")]
        public SimResourcePolicyConfig ResourcePolicy = new();

        [NonSerialized] private ISlotPositionIndex _slotPositions;

        WarehouseConveyorMap IConveyorMapSource.ConveyorMap => ConveyorMap;
        ISlotPositionIndex IWarehouseSimulationBindings.SlotPositions => ResolveSlotPositions();
        int IStackerFleetDescriptor.StackerCount => Fleet.StackerCount;
        SimStackerColumnReach IStackerFleetDescriptor.DefaultStackerColumnReach => Fleet.DefaultStackerColumnReach;
        SimStackerDefinition[] IStackerFleetDescriptor.StackerDefinitions => Fleet.StackerDefinitions;
        IStackerKinematics IStackerFleetDescriptor.DefaultStackerKinematics => Fleet.DefaultKinematics;
        int ISimResourcePolicy.MaxInfeedReservationsPerPort => ResourcePolicy.MaxInfeedReservationsPerPort;
        float ISimResourcePolicy.InfeedServiceSeconds => ResourcePolicy.InfeedServiceSeconds;
        int ISimResourcePolicy.MaxOutfeedReservationsPerPort => ResourcePolicy.MaxOutfeedReservationsPerPort;
        int ISimResourcePolicy.MaxOutfeedQueuePerPort => ResourcePolicy.MaxOutfeedQueuePerPort;
        float ISimResourcePolicy.OutfeedServiceSeconds => ResourcePolicy.OutfeedServiceSeconds;
        float ISimResourcePolicy.ProcessStationServiceSeconds => ResourcePolicy.ProcessStationServiceSeconds;
        float ISimResourcePolicy.VerticalTransferSeconds => ResourcePolicy.VerticalTransferSeconds;
        float ISimResourcePolicy.OccupancyNotifyDelaySeconds => ResourcePolicy.OccupancyNotifyDelaySeconds;
        int ISimResourcePolicy.MaxPickupReservationsPerPoint => ResourcePolicy.MaxPickupReservationsPerPoint;
        bool ISimResourcePolicy.UseAisleColumnReservation => ResourcePolicy.UseAisleColumnReservation;
        bool ISimResourcePolicy.UseStackerReservation => ResourcePolicy.UseStackerReservation;
        int ISimResourcePolicy.MaxSimEvents => ResourcePolicy.MaxSimEvents;

        public ISlotPositionIndex SlotPositions => ResolveSlotPositions();

        public void EnsureSlotPositionsLoaded() => _ = ResolveSlotPositions();

        private ISlotPositionIndex ResolveSlotPositions()
        {
            if (_slotPositions != null && _slotPositions.IsReady)
            {
                return _slotPositions;
            }

            _slotPositions = SlotPositionIndex.TryLoadFromStreamingAssets(WarehouseDataName)
                ?? new SlotPositionIndex();
            return _slotPositions;
        }
    }
}
