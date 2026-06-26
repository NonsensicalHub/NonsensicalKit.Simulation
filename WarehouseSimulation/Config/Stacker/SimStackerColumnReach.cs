using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>堆垛机固定轨道所在列起，沿列向可伸叉覆盖的货位列数。</summary>
    public enum SimStackerColumnReach
    {
        [InspectorName("1 列（单向）")]
        OneColumn = 1,

        [InspectorName("2 列（双向）")]
        TwoColumns = 2,

        [InspectorName("4 列")]
        FourColumns = 4,
    }
}
