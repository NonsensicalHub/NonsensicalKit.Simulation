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

        /// <summary>路段入口槽占用的方向令牌。同一物理路段同向可并行到容量上限，对向互斥。</summary>
        public string LaneResourceId;
        public double LaneReserveStart;
        public double LaneReserveEnd;

        /// <summary>
        /// 这条路靠近下游路口的直线占用。
        /// 同一路口、同一条直线上，别的路段车身靠近时不能重叠。
        /// </summary>
        public string AxisResourceId;
        public double AxisReserveStart;
        public double AxisReserveEnd;

        /// <summary>
        /// 预先占住下一段的方向令牌，直到本段车尾离开。
        /// 这样还没驶入下一段，对向车也不能先开进来把出口堵住。
        /// </summary>
        public string ExitLaneResourceId;
        public double ExitLaneReserveStart;
        public double ExitLaneReserveEnd;
    }
}
