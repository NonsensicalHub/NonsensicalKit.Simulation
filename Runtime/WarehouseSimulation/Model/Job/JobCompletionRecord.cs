using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单箱入出库任务完成后的统计快照（耗时、等待、货位与设备）。</summary>
    [Serializable]
    public struct JobCompletionRecord
    {
        public int JobId;
        public SimFlowDirection Direction;
        public double CompletedAt;
        public double Duration;
        public double ServiceTime;
        public double WaitTime;
        public string BottleneckResource;
        public GridIndex Slot;
        public int StackerId;
        public int InfeedPortIndex;
        public int OutfeedPortIndex;
        public int PickupPointIndex;
    }
}
