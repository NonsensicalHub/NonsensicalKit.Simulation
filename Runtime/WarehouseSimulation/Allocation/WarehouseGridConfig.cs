using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Allocation
{
    /// <summary>
    /// 货架网格参数，嵌入场景组件 Inspector。
    /// </summary>
    [Serializable]
    public class WarehouseGridConfig
    {
        [Header("货架网格（默认：8 层 × 12 列 × 10 排）")]
        [Label("层数")]
        public int LevelCount = 8;

        [Label("列数")]
        public int ColumnCount = 12;

        [Label("排数")]
        public int RowCount = 10;

        [Header("货位排除区")]
        [Tooltip("指定列/层/排不作为存储货位（如巷道前段堆垛机交互点占用的底层区域）")]
        [Label("排除区列表")]
        public SimSlotExclusionZone[] SlotExclusionZones;

        [Tooltip("按输送地图堆垛机交互点的列/排，自动排除浅位及向深处延伸的深位列，各含向上若干层")]
        [Label("自动排除交互点底层")]
        public bool AutoExcludePickupPointZones = true;

        [Min(1)]
        [Tooltip("含交互点所在层；例如 2 表示排除底层及上一层共 2 层")]
        [Label("交互点排除层数")]
        public int PickupPointExcludedLevelCount = 2;

        [Min(0)]
        [Tooltip("交互列（浅位）向货架深处额外排除的列数；例如 1 表示再排除紧邻深位列")]
        [Label("交互点排除深位列数")]
        public int PickupPointExcludedRearColumnCount = 1;
    }
}
