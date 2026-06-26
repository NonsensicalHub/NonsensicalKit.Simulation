using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>创建 <see cref="IWarehouseSimulationBindings"/> 的工厂与默认值。</summary>
    public static class WarehouseSimulationBindingsFactory
    {
        /// <summary>按默认参数创建运行时绑定。</summary>
        public static WarehouseSimulationBindings CreateDefault(
            WarehouseConveyorMap conveyorMap,
            ISlotPositionIndex slotPositions = null) =>
            new(conveyorMap, slotPositions);

        /// <summary>从已有资产复制为运行时绑定（便于单元测试或非 SO 场景）。</summary>
        public static WarehouseSimulationBindings FromAsset(DefaultWarehouseSimulationBindingsAsset asset)
        {
            if (asset == null)
            {
                return null;
            }

            asset.EnsureSlotPositionsLoaded();

            return new WarehouseSimulationBindings(
                asset.ConveyorMap,
                asset.SlotPositions,
                Clone(asset.Fleet),
                Clone(asset.ResourcePolicy));
        }

        private static StackerFleetConfig Clone(StackerFleetConfig source) =>
            source == null
                ? StackerFleetConfig.CreateDefault()
                : new StackerFleetConfig
                {
                    StackerCount = source.StackerCount,
                    DefaultStackerColumnReach = source.DefaultStackerColumnReach,
                    DefaultKinematics = Clone(source.DefaultKinematics),
                    StackerDefinitions = source.StackerDefinitions,
                };

        private static StackerKinematicsConfig Clone(StackerKinematicsConfig source) =>
            source == null
                ? StackerKinematicsConfig.CreateDefault()
                : new StackerKinematicsConfig
                {
                    PickSeconds = source.PickSeconds,
                    PlaceSeconds = source.PlaceSeconds,
                    HorizontalSpeedMetersPerSecond = source.HorizontalSpeedMetersPerSecond,
                    VerticalSpeedMetersPerSecond = source.VerticalSpeedMetersPerSecond,
                    VerticalSpeedLoadedMetersPerSecond = source.VerticalSpeedLoadedMetersPerSecond,
                    DepthSpeedMetersPerSecond = source.DepthSpeedMetersPerSecond,
                    DepthSpeedLoadedMetersPerSecond = source.DepthSpeedLoadedMetersPerSecond,
                    VerticalAccelerationMetersPerSecondSquared =
                        source.VerticalAccelerationMetersPerSecondSquared,
                    DepthAccelerationMetersPerSecondSquared =
                        source.DepthAccelerationMetersPerSecondSquared,
                };

        private static SimResourcePolicyConfig Clone(SimResourcePolicyConfig source) =>
            source == null
                ? SimResourcePolicyConfig.CreateDefault()
                : new SimResourcePolicyConfig
                {
                    MaxInfeedReservationsPerPort = source.MaxInfeedReservationsPerPort,
                    InfeedServiceSeconds = source.InfeedServiceSeconds,
                    MaxOutfeedReservationsPerPort = source.MaxOutfeedReservationsPerPort,
                    MaxOutfeedQueuePerPort = source.MaxOutfeedQueuePerPort,
                    OutfeedServiceSeconds = source.OutfeedServiceSeconds,
                    ProcessStationServiceSeconds = source.ProcessStationServiceSeconds,
                    VerticalTransferSeconds = source.VerticalTransferSeconds,
                    OccupancyNotifyDelaySeconds = source.OccupancyNotifyDelaySeconds,
                    MaxPickupReservationsPerPoint = source.MaxPickupReservationsPerPoint,
                    UseAisleColumnReservation = source.UseAisleColumnReservation,
                    UseStackerReservation = source.UseStackerReservation,
                    MaxSimEvents = source.MaxSimEvents,
                };
    }
}
