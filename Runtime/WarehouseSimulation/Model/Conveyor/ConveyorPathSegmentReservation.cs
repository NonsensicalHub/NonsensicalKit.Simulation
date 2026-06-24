namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单条路径边的输送预约结果（逐步 DES 单段推进）。</summary>
    public sealed class ConveyorPathSegmentReservation
    {
        public bool Success;
        public bool PathComplete = true;
        public ConveyorSegmentScheduleEntry ScheduleEntry;
        public double NextSegmentStartTime;
        /// <summary>仅首段（入库口→下一段）可能设置：货箱尾端离开入库口碰撞区时刻。</summary>
        public double? InfeedPhysicalReleaseSimTime;
    }
}
