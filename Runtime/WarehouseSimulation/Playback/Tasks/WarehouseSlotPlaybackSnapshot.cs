using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks
{
    /// <summary>仓库货位在某一仿真时刻的宏观可视状态（占用与高亮）。</summary>
    public struct WarehouseSlotPlaybackSnapshot
    {
        public HashSet<GridIndex> OccupiedSlots;
        public GridIndex? HighlightSlot;
    }
}
