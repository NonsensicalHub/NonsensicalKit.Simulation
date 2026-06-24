using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    public enum SimPlaybackPhase
    {
        Arrived,
        InfeedDone,
        /// <summary>出库口发运服务完成，货物离开出库口。</summary>
        OutfeedDone,
        /// <summary>入库完成且已择优选定输送路径（含节点序列）。</summary>
        ConveyorRouted,
        ConveyorDone,
        StackerWait,
        StackerApproach,
        StackerPick,
        StackerMove,
        StackerPlace,
        Completed,
    }

    /// <summary>回放时间轴上的一条记录，对应任务在某阶段的瞬时状态。</summary>
    [Serializable]
    public struct SimPlaybackEvent
    {
        public double SimTime;
        public int JobId;
        public SimPlaybackPhase Phase;
        public int StackerId;
        public GridIndex Slot;

        /// <summary>任务所属入库口序号（<see cref="SimPlaybackPhase.Arrived"/> 起有效）。</summary>
        public int InfeedPortIndex;

        /// <summary>取货点节点下标（<see cref="SimPlaybackPhase.ConveyorRouted"/> 起有效）。</summary>
        public int PickupPointIndex;

        /// <summary>输送路径节点下标序列（<see cref="SimPlaybackPhase.ConveyorRouted"/> 起有效）。</summary>
        public int[] PathNodeIndices;

        /// <summary>输送路段预约时刻（<see cref="SimPlaybackPhase.ConveyorRouted"/> 起有效）。</summary>
        public ConveyorSegmentScheduleEntry[] SegmentSchedule;
    }
}
