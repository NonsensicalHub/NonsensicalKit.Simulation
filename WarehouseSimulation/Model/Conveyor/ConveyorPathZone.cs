namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>输送路径上的单个预约区域（路段槽位 zone、路口 zone 或取货 zone）。</summary>
    public enum ConveyorPathZoneKind
    {
        /// <summary>路段 ZPA 槽位 zone（光电区）。</summary>
        EdgeSlot,
        /// <summary>路口节点 zone（与路段末端 slot-0 为不同资源）。</summary>
        Junction,
        /// <summary>取货点 zone。</summary>
        Pickup,

        /// <summary>出库口 zone。</summary>
        Outfeed,

        /// <summary>加工站点 zone（停留加工等）。</summary>
        ProcessStation,

        /// <summary>垂直提升机 zone（跨层提升）。</summary>
        VerticalTransfer,
    }

    /// <summary>
    /// 路径上按行进顺序排列的 zone；<see cref="HopSeconds"/> 为从上一 zone 驶入本 zone 所需 hop 时长。
    /// </summary>
    public struct ConveyorPathZone
    {
        public ConveyorPathZoneKind Kind;
        public string ResourceId;
        public int PathEdgeIndex;
        public int FromNodeIndex;
        public int ToNodeIndex;
        public int SlotIndex;
        public float HopSeconds;
        /// <summary>路口 zone：下一路径节点下标（用于驶出约束）。</summary>
        public int JunctionNextNodeIndex;

        public double DesiredArriveSimTime;
        public double ArriveSimTime;
        public double LeaveSimTime;
    }
}
