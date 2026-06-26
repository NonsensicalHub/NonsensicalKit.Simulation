namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单个路径 zone 预约结果（逐步 DES）。</summary>
    public sealed class ConveyorPathZoneReservation
    {
        public bool Success;
        public ConveyorPathZone Zone;
        public double NextStepStartTime;
        public double? InfeedPhysicalReleaseSimTime;
    }
}
