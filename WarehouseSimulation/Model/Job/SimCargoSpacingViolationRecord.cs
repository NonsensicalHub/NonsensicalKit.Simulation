namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>回放间距自检：两件货物中心沿输送线的距离小于单箱长度的一段时间。</summary>
    public sealed class SimCargoSpacingViolationRecord
    {
        public int JobA;
        public int JobB;
        public double StartTime;
        public double EndTime;
        /// <summary>这段时间内的最小中心距（米）。</summary>
        public double MinDistanceMeters;
        public double CargoLengthMeters;
        public string LocationA;
        public string LocationB;
    }
}
