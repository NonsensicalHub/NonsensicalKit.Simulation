using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>运行时组装的硬件绑定（无需 ScriptableObject）。</summary>
    public sealed class WarehouseSimulationBindings : IWarehouseSimulationBindings
    {
        public WarehouseSimulationBindings(
            WarehouseConveyorMap conveyorMap,
            ISlotPositionIndex slotPositions = null,
            StackerFleetConfig fleet = null,
            SimResourcePolicyConfig resourcePolicy = null)
        {
            ConveyorMap = conveyorMap;
            SlotPositions = slotPositions ?? new SlotPositionIndex();
            Fleet = fleet ?? StackerFleetConfig.CreateDefault();
            ResourcePolicy = resourcePolicy ?? SimResourcePolicyConfig.CreateDefault();
        }

        public WarehouseConveyorMap ConveyorMap { get; }

        public ISlotPositionIndex SlotPositions { get; }

        public StackerFleetConfig Fleet { get; }

        public SimResourcePolicyConfig ResourcePolicy { get; }

        public void EnsureSlotPositionsLoaded()
        {
        }

        WarehouseConveyorMap IConveyorMapSource.ConveyorMap => ConveyorMap;
        int IStackerFleetDescriptor.StackerCount => Fleet.StackerCount;
        SimStackerColumnReach IStackerFleetDescriptor.DefaultStackerColumnReach => Fleet.DefaultStackerColumnReach;
        SimStackerDefinition[] IStackerFleetDescriptor.StackerDefinitions => Fleet.StackerDefinitions;
        IStackerKinematics IStackerFleetDescriptor.DefaultStackerKinematics => Fleet.DefaultKinematics;
        int ISimResourcePolicy.MaxInfeedReservationsPerPort => ResourcePolicy.MaxInfeedReservationsPerPort;
        float ISimResourcePolicy.InfeedServiceSeconds => ResourcePolicy.InfeedServiceSeconds;
        int ISimResourcePolicy.MaxOutfeedReservationsPerPort => ResourcePolicy.MaxOutfeedReservationsPerPort;
        int ISimResourcePolicy.MaxOutfeedQueuePerPort => ResourcePolicy.MaxOutfeedQueuePerPort;
        float ISimResourcePolicy.OutfeedServiceSeconds => ResourcePolicy.OutfeedServiceSeconds;
        float ISimResourcePolicy.OccupancyNotifyDelaySeconds => ResourcePolicy.OccupancyNotifyDelaySeconds;
        int ISimResourcePolicy.MaxPickupReservationsPerPoint => ResourcePolicy.MaxPickupReservationsPerPoint;
        bool ISimResourcePolicy.UseAisleColumnReservation => ResourcePolicy.UseAisleColumnReservation;
        bool ISimResourcePolicy.UseStackerReservation => ResourcePolicy.UseStackerReservation;
        int ISimResourcePolicy.MaxSimEvents => ResourcePolicy.MaxSimEvents;
    }
}
