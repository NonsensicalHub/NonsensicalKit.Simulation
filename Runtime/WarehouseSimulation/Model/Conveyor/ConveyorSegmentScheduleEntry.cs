using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>仿真预约的输送路段时刻：用于回放按仿真时钟定位料箱，含槽位与占用尾端时刻。</summary>
    [Serializable]
    public struct ConveyorSegmentScheduleEntry
    {
        public int FromNodeIndex;
        public int ToNodeIndex;
        /// <summary>路段槽位序号（0=最靠近终点，capacity-1=最靠近起点）。</summary>
        public int SlotIndex;
        /// <summary>预约时期望进入路段的时刻（用于子任务排队统计）。</summary>
        public double DesiredEntrySimTime;
        /// <summary>前端进入路段的时刻。</summary>
        public double EntrySimTime;
        /// <summary>前端到达路段终点的时刻。</summary>
        public double ExitSimTime;
        /// <summary>尾端离开路段的时刻（含碰撞尾距）。</summary>
        public double OccupancyEndSimTime;
        /// <summary>
        /// 各停留点（光电传感器）前端到达时刻。
        /// 索引 0 = 最靠近终点，capacity-1 = 最靠近起点；长度等于路段容量。
        /// </summary>
        public double[] StopArriveSimTimes;
    }
}
