using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>单向堆垛机（1 列伸叉）所服务的巷道侧别。</summary>
    public enum SimStackerServingSide
    {
        [InspectorName("左侧货位")]
        Left = 0,

        [InspectorName("右侧货位")]
        Right = 1,
    }
}
