using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>两件货物在同一物理路段上的在段时间重叠（仿真后自检）。</summary>
    [Serializable]
    public struct SimSegmentCoOccupancyRecord
    {
        public string SegmentKey;
        public string SegmentLabel;
        public int Capacity;
        public bool OppositeDirection;
        public int JobA;
        public int JobB;
        public string DirectionA;
        public string DirectionB;
        public double JobAStart;
        public double JobAEnd;
        public double JobBStart;
        public double JobBEnd;
        public double OverlapStart;
        public double OverlapEnd;
        public double OverlapSeconds;
    }
}
