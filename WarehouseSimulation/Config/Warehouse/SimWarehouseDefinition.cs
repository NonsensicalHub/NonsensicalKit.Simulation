using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>一套货架：仓库号、货位数据和网格。仓库列表里的每一项都用这个类型，包含 0 号库。</summary>
    [Serializable]
    public sealed class SimWarehouseDefinition
    {
        [Min(0)]
        [Label("仓库号")]
        [Tooltip("同一列表内不能重复。单库通常为 0。")]
        public int WarehouseId;

        [Label("名称")]
        public string Name;

        [Label("仓库数据名（不含扩展名）")]
        [Tooltip("StreamingAssets/Warehouse/{名称}.dat，与该库的 WarehouseManager 共用。")]
        public string WarehouseDataName;

        [Label("货架网格")]
        public WarehouseGridConfig Grid = new();
    }
}
