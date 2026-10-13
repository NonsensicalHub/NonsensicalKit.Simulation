using System;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>回放用的一座仓库。仓库列表里的每一项都包括 0 号库。</summary>
    [Serializable]
    public sealed class WarehousePlaybackBinding
    {
        [Min(0)]
        [Label("仓库号")]
        public int WarehouseId;

        [Label("数字孪生仓库")]
        public WarehouseManager Manager;

        [Label("货位坐标索引")]
        public WarehouseManagerBinPositionIndex PositionIndex;
    }
}
