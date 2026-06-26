using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>资源利用率统计所覆盖的设备类别。</summary>
    public enum SimResourceUtilizationKind
    {
        Stacker,
        InfeedPort,
        OutfeedPort,
    }

    /// <summary>单台设备在整次仿真中的忙碌时长与利用率。</summary>
    [Serializable]
    public struct SimResourceUtilizationStat
    {
        public SimResourceUtilizationKind Kind;
        public int ResourceIndex;
        public string Label;
        public double BusySeconds;
        public double TotalSeconds;

        public double UtilizationRatio => TotalSeconds > 1e-9 ? BusySeconds / TotalSeconds : 0;

        public double UtilizationPercent => UtilizationRatio * 100;
    }
}
