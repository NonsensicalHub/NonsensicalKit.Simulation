using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>
    /// 货架上不可作为存储货位的区域。
    /// 列、层、排均为闭区间 [Start, End]；<see cref="RowEnd"/> &lt; 0 表示该列/层范围内全部排。
    /// </summary>
    [Serializable]
    public struct SimSlotExclusionZone
    {
        [Label("起始列")]
        public int ColumnStart;

        [Label("结束列")]
        public int ColumnEnd;

        [Label("起始层")]
        public int LevelStart;

        [Label("结束层")]
        public int LevelEnd;

        [Label("起始排")]
        public int RowStart;

        [Label("结束排")]
        public int RowEnd;
    }
}
