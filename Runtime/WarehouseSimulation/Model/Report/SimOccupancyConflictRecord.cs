using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>两任务在同一独占资源上的时间重叠（仿真后自检）。</summary>
    [Serializable]
    public struct SimOccupancyConflictRecord
    {
        public string ResourceKey;
        public string ResourceLabel;
        public SimOccupancyResourceCategory Category;
        public int JobA;
        public int JobB;
        public double JobAStart;
        public double JobAEnd;
        public double JobBStart;
        public double JobBEnd;
        public double OverlapStart;
        public double OverlapEnd;
        public double OverlapSeconds;
        public SimSubTaskKind KindA;
        public SimSubTaskKind KindB;
    }
}
